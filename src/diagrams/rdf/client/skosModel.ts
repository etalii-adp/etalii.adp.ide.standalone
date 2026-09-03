import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import { RdfTruncationPayloadSchema } from "@client/generated/rdf_pb";
import {
  SkosCollectionPayloadSchema,
  SkosConceptPayloadSchema,
  SkosEdgePayloadSchema,
  SkosSchemePayloadSchema,
} from "@client/generated/skos_pb";
import type { RdfTruncation } from "./rdfModel";

/** The mime-style element types the scheme reading sends. */
export const CONCEPT = "w3c/skos+concept";
export const SCHEME = "w3c/skos+scheme";
export const COLLECTION = "w3c/skos+collection";
export const SKOS_EDGE = "w3c/skos+edge";
export const SKOS_TRUNCATION = "w3c/skos+truncation";

/** What kind of name a node wears - the backend chose it; the canvas only styles it. */
export const PREFERRED = 0;
export const ALTERNATE = 1;
export const IRI_FALLBACK = 2;

/** The three edge languages (skos-diagram Requirements 1.2, 1.3). */
export const HIERARCHY = 0;
export const RELATED = 1;
export const MAPPING = 2;

/**
 * One drawn concept, positioned in the module's own coordinate space - the layered layout with
 * the `.adp`'s authored positions already overlaid backend-side.
 */
export interface SkosConcept {
  id: string;
  x: number;
  y: number;
  /** The full IRI; "" for a blank-node concept. */
  iri: string;
  /** The name `SkosLabels.Choose` settled on - never recomputed here (Requirement 3). */
  label: string;
  /** The chosen label's language tag, lowercased; "" for untagged and the IRI fallback. */
  languageTag: string;
  /** Preferred, alternate (marked) or IRI fallback (dimmed). */
  labelKind: number;
  /** The first `skos:notation`, badged before the label; "" where the concept has none. */
  notation: string;
  schemeIris: string[];
  blank: boolean;
  /**
   * Whether to wear the language chip. The backend decided it - the tag differs from the
   * session's display language, a comparison only the session can make (Requirement 3.3).
   */
  languageChip: boolean;
}

/** One concept scheme, drawn as a titled region (Requirement 1.1). */
export interface SkosScheme {
  id: string;
  x: number;
  y: number;
  iri: string;
  label: string;
  languageTag: string;
  labelKind: number;
  memberCount: number;
}

/** One collection, drawn as a labeled group (Requirement 1.5). */
export interface SkosCollection {
  id: string;
  x: number;
  y: number;
  iri: string;
  label: string;
  languageTag: string;
  labelKind: number;
  ordered: boolean;
  memberIds: string[];
}

/** One drawn edge in one of the three edge languages. */
export interface SkosEdge {
  id: string;
  fromElementId: string;
  toElementId: string;
  /** HIERARCHY, RELATED or MAPPING. */
  kind: number;
  /** The mapping property's display form; "" for hierarchy and related. */
  predicate: string;
  /** Whether the file asserts both directions - what the disconnect confirmation names. */
  assertedBothWays: boolean;
}

export interface SkosModel {
  concepts: Map<string, SkosConcept>;
  schemes: Map<string, SkosScheme>;
  collections: Map<string, SkosCollection>;
  edges: Map<string, SkosEdge>;
  truncation: RdfTruncation | null;
}

export const emptySkosModel: SkosModel = {
  concepts: new Map(),
  schemes: new Map(),
  collections: new Map(),
  edges: new Map(),
  truncation: null,
};

/** Folds one delta into the model, as the family's canvases all do. */
export function applySkosDelta(model: SkosModel, delta: Delta): SkosModel {
  switch (delta.action?.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      return model;
  }
}

function copy(model: SkosModel): SkosModel {
  return {
    concepts: new Map(model.concepts),
    schemes: new Map(model.schemes),
    collections: new Map(model.collections),
    edges: new Map(model.edges),
    truncation: model.truncation,
  };
}

function added(model: SkosModel, elements: readonly Element[]): SkosModel {
  const next = copy(model);

  for (const element of elements) {
    const id = element.id?.value;
    if (!id) {
      continue;
    }

    // The type check comes BEFORE the decode: several readings share one delta stream, and a
    // foreign element decoded as ours would throw and take the whole delta with it.
    const bytes = element.payload?.value ?? new Uint8Array();
    const x = element.position?.x ?? 0;
    const y = element.position?.y ?? 0;
    switch (element.type) {
      case CONCEPT: {
        const payload = fromBinary(SkosConceptPayloadSchema, bytes);
        next.concepts.set(id, {
          id,
          x,
          y,
          iri: payload.iri,
          label: payload.label,
          languageTag: payload.languageTag,
          labelKind: payload.labelKind,
          notation: payload.notation,
          schemeIris: [...payload.schemeIris],
          blank: payload.blank,
          languageChip: payload.languageChip,
        });
        break;
      }
      case SCHEME: {
        const payload = fromBinary(SkosSchemePayloadSchema, bytes);
        next.schemes.set(id, {
          id,
          x,
          y,
          iri: payload.iri,
          label: payload.label,
          languageTag: payload.languageTag,
          labelKind: payload.labelKind,
          memberCount: payload.memberCount,
        });
        break;
      }
      case COLLECTION: {
        const payload = fromBinary(SkosCollectionPayloadSchema, bytes);
        next.collections.set(id, {
          id,
          x,
          y,
          iri: payload.iri,
          label: payload.label,
          languageTag: payload.languageTag,
          labelKind: payload.labelKind,
          ordered: payload.ordered,
          memberIds: [...payload.memberIds],
        });
        break;
      }
      case SKOS_EDGE: {
        const payload = fromBinary(SkosEdgePayloadSchema, bytes);
        next.edges.set(id, {
          id,
          fromElementId: payload.fromElementId,
          toElementId: payload.toElementId,
          kind: payload.kind,
          predicate: payload.predicate,
          assertedBothWays: payload.assertedBothWays,
        });
        break;
      }
      case SKOS_TRUNCATION: {
        // The family's own banner payload - one fact, one message, no duplicate.
        const payload = fromBinary(RdfTruncationPayloadSchema, bytes);
        next.truncation = { shown: payload.shown, total: payload.total };
        break;
      }
      default:
        break;
    }
  }

  return next;
}

function removed(model: SkosModel, ids: readonly { value: string }[]): SkosModel {
  const next = copy(model);

  for (const id of ids) {
    next.concepts.delete(id.value);
    next.schemes.delete(id.value);
    next.collections.delete(id.value);
    next.edges.delete(id.value);
    if (id.value === "truncation") {
      next.truncation = null;
    }
  }

  return next;
}
