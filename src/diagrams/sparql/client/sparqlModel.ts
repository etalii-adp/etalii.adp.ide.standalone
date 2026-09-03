import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  SparqlAnnotationPayloadSchema,
  SparqlEdgePayloadSchema,
  SparqlHeaderPayloadSchema,
  SparqlRegionPayloadSchema,
  SparqlTermKind,
  SparqlTermPayloadSchema,
  SparqlTruncationPayloadSchema,
  SparqlVariablePayloadSchema,
} from "@client/generated/sparql_pb";

/** The mime-style element types the backend sends. */
export const VARIABLE = "w3c/sparql+variable";
export const TERM = "w3c/sparql+term";
export const EDGE = "w3c/sparql+edge";
export const REGION = "w3c/sparql+region";
export const ANNOTATION = "w3c/sparql+annotation";
export const HEADER = "w3c/sparql+header";
export const TRUNCATION = "w3c/sparql+truncation";

/** The header band's element id - the one element every query diagram has. */
export const HEADER_ID = "header:query";

/**
 * One drawn node, positioned in the module's own coordinate space - computed layout with the
 * `.adp`'s authored positions already overlaid backend-side. The canvas layers its zoom on top;
 * nothing here is a pixel.
 */
export interface SparqlNode {
  id: string;
  x: number;
  y: number;
  /** What the node shows: `?name`, a prefixed name, or a literal's lexical form. */
  display: string;
  kind: "variable" | "anonymous" | "iri" | "literal" | "subquery";
  /** Whether the projection carries this variable outward (Requirement 4.2). */
  projected: boolean;
  /** The variable's triple-pattern degree - its join count, which is the diagram's whole point. */
  joinCount: number;
  /** A literal's datatype or language tag, or a subquery's marker; "" otherwise. */
  annotation: string;
  /** A term's full IRI or written form, or a subquery's whole text. */
  full: string;
}

/** One triple-pattern edge, or a subquery's projected-name join. */
export interface SparqlDiagramEdge {
  id: string;
  fromElementId: string;
  toElementId: string;
  /** The predicate, or the property path exactly as written. */
  label: string;
  /** Whether the label is a multi-step path rather than one predicate (Requirement 3.1). */
  isPath: boolean;
}

/** One containment region: OPTIONAL, a UNION branch, MINUS, GRAPH, SERVICE, or the template. */
export interface SparqlRegion {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
  kind: string;
  label: string;
  parentRegionId: string;
}

/** One FILTER, BIND or VALUES badge, showing its text as written (Requirement 3.3). */
export interface SparqlAnnotation {
  id: string;
  kind: string;
  text: string;
  /** The element this badge anchors to; "" floats it on the open canvas. */
  attachedTo: string;
}

/** The query's frame: form and modifiers, drawn as a header band rather than as structure. */
export interface SparqlHeader {
  form: string;
  modifierRows: string[];
}

/** The sanity-bound banner's facts, present only when the view is cut (Requirement 7.5). */
export interface SparqlTruncation {
  shown: number;
  total: number;
}

export interface SparqlModel {
  nodes: Map<string, SparqlNode>;
  edges: Map<string, SparqlDiagramEdge>;
  regions: Map<string, SparqlRegion>;
  annotations: Map<string, SparqlAnnotation>;
  header: SparqlHeader | null;
  truncation: SparqlTruncation | null;
}

export const emptyModel: SparqlModel = {
  nodes: new Map(),
  edges: new Map(),
  regions: new Map(),
  annotations: new Map(),
  header: null,
  truncation: null,
};

/** Folds one delta into the model, leaving the previous one untouched. */
export function applyDelta(model: SparqlModel, delta: Delta): SparqlModel {
  switch (delta.action.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      return model;
  }
}

function copy(model: SparqlModel): SparqlModel {
  return {
    nodes: new Map(model.nodes),
    edges: new Map(model.edges),
    regions: new Map(model.regions),
    annotations: new Map(model.annotations),
    header: model.header,
    truncation: model.truncation,
  };
}

function added(model: SparqlModel, elements: readonly Element[]): SparqlModel {
  const next = copy(model);

  for (const element of elements) {
    const id = element.id?.value;
    if (!id) {
      continue;
    }

    // The type check comes BEFORE the decode. Several modules share one delta stream, and a
    // foreign element decoded as ours throws and takes the whole delta with it.
    const bytes = element.payload?.value ?? new Uint8Array();
    const x = element.position?.x ?? 0;
    const y = element.position?.y ?? 0;
    switch (element.type) {
      case VARIABLE: {
        const payload = fromBinary(SparqlVariablePayloadSchema, bytes);
        next.nodes.set(id, {
          id,
          x,
          y,
          display: payload.name,
          kind: payload.anonymous ? "anonymous" : "variable",
          projected: payload.projected,
          joinCount: payload.joinCount,
          annotation: payload.definingExpression,
          full: payload.definingExpression,
        });
        break;
      }
      case TERM: {
        const payload = fromBinary(SparqlTermPayloadSchema, bytes);
        next.nodes.set(id, {
          id,
          x,
          y,
          display: payload.display,
          kind:
            payload.kind === SparqlTermKind.IRI
              ? "iri"
              : payload.kind === SparqlTermKind.LITERAL
                ? "literal"
                : "subquery",
          projected: false,
          joinCount: 0,
          annotation: payload.annotation,
          full: payload.full,
        });
        break;
      }
      case EDGE: {
        const payload = fromBinary(SparqlEdgePayloadSchema, bytes);
        next.edges.set(id, {
          id,
          fromElementId: payload.fromElementId,
          toElementId: payload.toElementId,
          label: payload.label,
          isPath: payload.isPath,
        });
        break;
      }
      case REGION: {
        const payload = fromBinary(SparqlRegionPayloadSchema, bytes);
        next.regions.set(id, {
          id,
          x,
          y,
          width: payload.width,
          height: payload.height,
          kind: payload.kind,
          label: payload.label,
          parentRegionId: payload.parentRegionId,
        });
        break;
      }
      case ANNOTATION: {
        const payload = fromBinary(SparqlAnnotationPayloadSchema, bytes);
        next.annotations.set(id, {
          id,
          kind: payload.kind,
          text: payload.text,
          attachedTo: payload.attachedTo,
        });
        break;
      }
      case HEADER: {
        const payload = fromBinary(SparqlHeaderPayloadSchema, bytes);
        next.header = { form: payload.form, modifierRows: [...payload.modifierRows] };
        break;
      }
      case TRUNCATION: {
        const payload = fromBinary(SparqlTruncationPayloadSchema, bytes);
        next.truncation = { shown: payload.shown, total: payload.total };
        break;
      }
      default:
        break;
    }
  }

  return next;
}

function removed(model: SparqlModel, ids: readonly { value: string }[]): SparqlModel {
  const next = copy(model);

  for (const id of ids) {
    next.nodes.delete(id.value);
    next.edges.delete(id.value);
    next.regions.delete(id.value);
    next.annotations.delete(id.value);
    if (id.value === HEADER_ID) {
      next.header = null;
    }

    if (id.value === "truncation") {
      next.truncation = null;
    }
  }

  return next;
}
