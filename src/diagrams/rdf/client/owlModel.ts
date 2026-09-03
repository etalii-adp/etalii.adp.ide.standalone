import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  OwlEdgePayloadSchema,
  OwlExpressionPayloadSchema,
  OwlNodePayloadSchema,
  RdfTruncationPayloadSchema,
} from "@client/generated/rdf_pb";
import type { RdfRow, RdfTruncation } from "./rdfModel";

/** The mime-style element types the ontology reading sends. */
export const OWL_NODE = "w3c/owl+node";
export const OWL_EDGE = "w3c/owl+edge";
export const OWL_EXPRESSION = "w3c/owl+expression";
export const OWL_TRUNCATION = "w3c/owl+truncation";

/** What a node draws as - the shape vocabulary, not a colour scheme (owl-diagram Requirement 1). */
export type OwlNodeKind = "class" | "datatype" | "individual" | "thing" | "ontology" | "operator" | "restriction";

/** What an edge draws as - the axiom it states (owl-diagram Requirement 2). */
export type OwlEdgeKind =
  | "subclass"
  | "equivalent"
  | "disjoint"
  | "object-property"
  | "datatype-property"
  | "assertion"
  | "expression";

/**
 * One drawn ontology element, positioned in the module's own coordinate space - computed layout
 * with the `.adp`'s authored positions already overlaid backend-side.
 */
export interface OwlNode {
  id: string;
  x: number;
  y: number;
  kind: OwlNodeKind;
  /** The full IRI; "" for expression nodes and materialized anchors. */
  iri: string;
  /** The label: `rdfs:label` preferred, prefixed name as fallback (Requirement 1.4). */
  display: string;
  /** Type badges on an individual card; empty elsewhere. */
  badges: string[];
  rows: RdfRow[];
  /** `owl:deprecated true` - drawn dimmed (Requirement 1.6). */
  deprecated: boolean;
  /** Used here, declared nowhere in this file - drawn dimmed (Requirement 1.6). */
  external: boolean;
  /** Structure deeper than the label's cap was elided; the full form is one selection away (3.4). */
  elided: boolean;
  /** A structurally broken expression - drawn as a marked problem node (3.5). */
  malformed: boolean;
  /** For an expression node, the class whose axiom attaches it. */
  ownerElementId: string;
}

/** One drawn axiom or assertion. */
export interface OwlDiagramEdge {
  id: string;
  kind: OwlEdgeKind;
  fromElementId: string;
  toElementId: string;
  /** A property's display with its characteristic words folded in; "" for pure axiom edges. */
  label: string;
  propertyIri: string;
}

export interface OwlModel {
  nodes: Map<string, OwlNode>;
  edges: Map<string, OwlDiagramEdge>;
  truncation: RdfTruncation | null;
}

export const emptyOwlModel: OwlModel = {
  nodes: new Map(),
  edges: new Map(),
  truncation: null,
};

/** Whether a node is one of the blank-node-rooted expression shapes - never positionable. */
export function isExpression(node: OwlNode): boolean {
  return node.kind === "restriction" || node.kind === "operator";
}

/** Whether a node is drawn as a card rather than a shape: individuals and the ontology header. */
export function isCard(node: OwlNode): boolean {
  return node.kind === "individual" || node.kind === "ontology";
}

/**
 * Folds one delta into the model. `add` is an upsert keyed on id: an edit arrives as an add
 * carrying the element in its new state.
 */
export function applyOwlDelta(model: OwlModel, delta: Delta): OwlModel {
  switch (delta.action.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      return model;
  }
}

function added(model: OwlModel, elements: readonly Element[]): OwlModel {
  const next: OwlModel = {
    nodes: new Map(model.nodes),
    edges: new Map(model.edges),
    truncation: model.truncation,
  };

  for (const element of elements) {
    const id = element.id?.value;
    if (!id) {
      continue;
    }

    // The type check comes BEFORE the decode: several modules share one delta stream - this
    // module's own two readings among them - and a foreign element decoded as ours throws and
    // takes the whole delta with it.
    const bytes = element.payload?.value ?? new Uint8Array();
    const x = element.position?.x ?? 0;
    const y = element.position?.y ?? 0;
    switch (element.type) {
      case OWL_NODE: {
        const payload = fromBinary(OwlNodePayloadSchema, bytes);
        next.nodes.set(id, {
          id,
          x,
          y,
          kind: kindOf(payload.kind),
          iri: payload.iri,
          display: payload.display,
          badges: [...payload.badges],
          rows: payload.rows.map((row) => ({ predicate: row.predicate, value: row.value, annotation: row.annotation })),
          deprecated: payload.deprecated,
          external: payload.external,
          elided: false,
          malformed: false,
          ownerElementId: "",
        });
        break;
      }
      case OWL_EXPRESSION: {
        const payload = fromBinary(OwlExpressionPayloadSchema, bytes);
        next.nodes.set(id, {
          id,
          x,
          y,
          kind: payload.kind === "operator" ? "operator" : "restriction",
          iri: "",
          display: payload.label,
          badges: [],
          rows: [],
          deprecated: false,
          external: false,
          elided: payload.elided,
          malformed: payload.malformed,
          ownerElementId: payload.ownerElementId,
        });
        break;
      }
      case OWL_EDGE: {
        const payload = fromBinary(OwlEdgePayloadSchema, bytes);
        next.edges.set(id, {
          id,
          kind: edgeKindOf(payload.kind),
          fromElementId: payload.fromElementId,
          toElementId: payload.toElementId,
          label: payload.label,
          propertyIri: payload.propertyIri,
        });
        break;
      }
      case OWL_TRUNCATION: {
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

function removed(model: OwlModel, ids: readonly { value: string }[]): OwlModel {
  const next: OwlModel = {
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

/** An unknown kind draws as a class rather than not at all - the backend names the shapes. */
function kindOf(kind: string): OwlNodeKind {
  switch (kind) {
    case "datatype":
    case "individual":
    case "thing":
    case "ontology":
      return kind;
    default:
      return "class";
  }
}

function edgeKindOf(kind: string): OwlEdgeKind {
  switch (kind) {
    case "equivalent":
    case "disjoint":
    case "object-property":
    case "datatype-property":
    case "assertion":
    case "expression":
      return kind;
    default:
      return "subclass";
  }
}
