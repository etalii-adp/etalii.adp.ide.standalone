import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import { RdfTruncationPayloadSchema } from "@client/generated/rdf_pb";
import { ShaclEdgePayloadSchema, ShaclShapePayloadSchema } from "@client/generated/shacl_pb";
import type { RdfTruncation } from "./rdfModel";

/** The mime-style element types the shapes reading sends. */
export const SHAPE = "w3c/shacl+shape";
export const SHACL_EDGE = "w3c/shacl+edge";
export const SHACL_TRUNCATION = "w3c/shacl+truncation";

/** What a target declaration selects by; the numbers are the proto enum's. */
export const TARGET_CLASS = 1;
export const TARGET_NODE = 2;
export const TARGET_SUBJECTS_OF = 3;
export const TARGET_OBJECTS_OF = 4;
export const TARGET_IMPLICIT_CLASS = 5;

/**
 * One target declaration, drawn as a chip on its shape's card.
 *
 * It is card content rather than an element on purpose: the data a shapes graph aims at usually
 * lives in another file, so an edge would either dangle or invent a node. `describedInFile` says
 * whether the term happens to occur in this file, and the canvas renders both cases identically -
 * absence is normal here, not a problem to flag.
 */
export interface ShaclTarget {
  kind: number;
  termDisplay: string;
  termIri: string;
  describedInFile: boolean;
}

/**
 * One constraint row on a card - a property shape drawn as a line rather than as the blank-node
 * constellation it is written as. A row has no position of its own, which is exactly why this
 * reading can draw blank-rooted content without breaking the identity boundary.
 */
export interface ShaclRow {
  path: string;
  name: string;
  summary: string;
  cardinality: string;
  sparql: boolean;
  blank: boolean;
  severity: string;
}

/** One drawn shape - a card, positioned with the `.adp`'s authored position already overlaid. */
export interface ShaclShape {
  id: string;
  x: number;
  y: number;
  /** The full IRI; "" for an anonymous shape, which draws but never stores a position. */
  iri: string;
  display: string;
  blank: boolean;
  deactivated: boolean;
  severity: string;
  closed: boolean;
  name: string;
  description: string;
  targets: ShaclTarget[];
  rows: ShaclRow[];
}

/** One drawn shape-to-shape reference: `node`, `class`, or a logical combinator. */
export interface ShaclEdge {
  id: string;
  fromElementId: string;
  toElementId: string;
  kind: string;
  label: string;
}

export interface ShaclModel {
  shapes: Map<string, ShaclShape>;
  edges: Map<string, ShaclEdge>;
  truncation: RdfTruncation | null;
}

export const emptyShaclModel: ShaclModel = {
  shapes: new Map(),
  edges: new Map(),
  truncation: null,
};

/** Folds one delta into the model, as the family's canvases all do. */
export function applyShaclDelta(model: ShaclModel, delta: Delta): ShaclModel {
  switch (delta.action?.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      return model;
  }
}

function copy(model: ShaclModel): ShaclModel {
  return {
    shapes: new Map(model.shapes),
    edges: new Map(model.edges),
    truncation: model.truncation,
  };
}

function added(model: ShaclModel, elements: readonly Element[]): ShaclModel {
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
      case SHAPE: {
        const payload = fromBinary(ShaclShapePayloadSchema, bytes);
        next.shapes.set(id, {
          id,
          x,
          y,
          iri: payload.iri,
          display: payload.display,
          blank: payload.blank,
          deactivated: payload.deactivated,
          severity: payload.severity,
          closed: payload.closed,
          name: payload.name,
          description: payload.description,
          targets: payload.targets.map((target) => ({
            kind: target.kind,
            termDisplay: target.termDisplay,
            termIri: target.termIri,
            describedInFile: target.describedInFile,
          })),
          rows: payload.rows.map((row) => ({
            path: row.path,
            name: row.name,
            summary: row.summary,
            cardinality: row.cardinality,
            sparql: row.sparql,
            blank: row.blank,
            severity: row.severity,
          })),
        });
        break;
      }
      case SHACL_EDGE: {
        const payload = fromBinary(ShaclEdgePayloadSchema, bytes);
        next.edges.set(id, {
          id,
          fromElementId: payload.fromElementId,
          toElementId: payload.toElementId,
          kind: payload.kind,
          label: payload.label,
        });
        break;
      }
      case SHACL_TRUNCATION: {
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

function removed(model: ShaclModel, ids: readonly { value: string }[]): ShaclModel {
  const next = copy(model);

  for (const id of ids) {
    next.shapes.delete(id.value);
    next.edges.delete(id.value);
    if (id.value === "truncation") {
      next.truncation = null;
    }
  }

  return next;
}

/** The words a target chip reads as - the backend sends the kind, the canvas words it. */
export function targetWords(target: ShaclTarget): string {
  switch (target.kind) {
    case TARGET_CLASS:
      return `targets class ${target.termDisplay}`;
    case TARGET_NODE:
      return `targets node ${target.termDisplay}`;
    case TARGET_SUBJECTS_OF:
      return `targets subjects of ${target.termDisplay}`;
    case TARGET_OBJECTS_OF:
      return `targets objects of ${target.termDisplay}`;
    default:
      return `targets its own instances`;
  }
}

/** Card height in canvas units: the title band, then a line per chip and per row. */
export function shapeHeight(shape: ShaclShape): number {
  return 44 + (shape.targets.length + shape.rows.length) * 22 + 12;
}
