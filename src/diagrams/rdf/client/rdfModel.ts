import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  RdfEdgePayloadSchema,
  RdfResourcePayloadSchema,
  RdfTruncationPayloadSchema,
} from "@client/generated/rdf_pb";

/** The mime-style element types the backend sends. */
export const RESOURCE = "w3c/rdf+resource";
export const EDGE = "w3c/rdf+edge";
export const TRUNCATION = "w3c/rdf+truncation";

/** One literal property row inside a resource card (Requirement 3.2). */
export interface RdfRow {
  predicate: string;
  value: string;
  /** A language tag with its `@`, a prefixed datatype, or "" for a plain string. */
  annotation: string;
}

/**
 * One resource card, positioned in the module's own coordinate space - computed layout with the
 * `.adp`'s authored positions already overlaid backend-side. The canvas layers its zoom on top;
 * nothing here is a pixel.
 */
export interface RdfNode {
  id: string;
  x: number;
  y: number;
  /** The full IRI; "" for a blank node. */
  iri: string;
  /** The card's title: a prefixed name, a local name, or a `_:label`. */
  display: string;
  /** The rdf:type badges, display form (Requirement 3.3). */
  typeBadges: string[];
  rows: RdfRow[];
  /** A blank node: drawn, styled apart, and refusing positions and edits (Requirement 3.4). */
  blank: boolean;
}

/** One drawn triple between two cards. */
export interface RdfDiagramEdge {
  id: string;
  fromElementId: string;
  toElementId: string;
  /** The predicate's display form - the edge's label. */
  predicate: string;
  predicateIri: string;
}

/** The budget banner's facts, present only when the view is cut (Requirement 8.2). */
export interface RdfTruncation {
  shown: number;
  total: number;
}

export interface RdfModel {
  nodes: Map<string, RdfNode>;
  edges: Map<string, RdfDiagramEdge>;
  truncation: RdfTruncation | null;
}

export const emptyModel: RdfModel = {
  nodes: new Map(),
  edges: new Map(),
  truncation: null,
};

/**
 * Folds one delta into the model. `add` is an upsert keyed on id: an edit arrives as an add
 * carrying the element in its new state, never as a remove-then-add.
 */
export function applyDelta(model: RdfModel, delta: Delta): RdfModel {
  switch (delta.action.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      return model;
  }
}

function added(model: RdfModel, elements: readonly Element[]): RdfModel {
  const next: RdfModel = {
    nodes: new Map(model.nodes),
    edges: new Map(model.edges),
    truncation: model.truncation,
  };

  for (const element of elements) {
    const id = element.id?.value;
    if (!id) {
      continue;
    }

    // The type check comes BEFORE the decode. Several modules share one delta stream, and a
    // foreign element decoded as ours throws and takes the whole delta with it - a sibling
    // module shipped exactly that bug before finding it.
    const bytes = element.payload?.value ?? new Uint8Array();
    const x = element.position?.x ?? 0;
    const y = element.position?.y ?? 0;
    switch (element.type) {
      case RESOURCE: {
        const payload = fromBinary(RdfResourcePayloadSchema, bytes);
        next.nodes.set(id, {
          id,
          x,
          y,
          iri: payload.iri,
          display: payload.display,
          typeBadges: [...payload.typeBadges],
          rows: payload.rows.map((row) => ({ predicate: row.predicate, value: row.value, annotation: row.annotation })),
          blank: payload.blank,
        });
        break;
      }
      case EDGE: {
        const payload = fromBinary(RdfEdgePayloadSchema, bytes);
        next.edges.set(id, {
          id,
          fromElementId: payload.fromElementId,
          toElementId: payload.toElementId,
          predicate: payload.predicate,
          predicateIri: payload.predicateIri,
        });
        break;
      }
      case TRUNCATION: {
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

function removed(model: RdfModel, ids: readonly { value: string }[]): RdfModel {
  const next: RdfModel = {
    nodes: new Map(model.nodes),
    edges: new Map(model.edges),
    truncation: model.truncation,
  };

  for (const id of ids) {
    next.nodes.delete(id.value);
    next.edges.delete(id.value);
    if (id.value === "truncation") {
      next.truncation = null;
    }
  }

  return next;
}
