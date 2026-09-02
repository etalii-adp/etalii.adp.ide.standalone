import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  DependencyGraphElementPayloadSchema,
  DependencyGraphRelationPayloadSchema,
} from "@client/generated/dependency-graph_pb";

/** The mime-style element types the backend sends, all under `generic/dependencies`. */
export const NODE = "generic/dependencies+node";
export const RELATION = "generic/dependencies+relation";

/**
 * A node, positioned in the module's own coordinate space: x is the authored canvas coordinate,
 * y is row × row height. The canvas layers its zoom on top; nothing here is a pixel, and nothing
 * here is a time.
 */
export interface DependencyGraphElement {
  id: string;
  x: number;
  y: number;
  label: string;
  row: number;
}

/** A directed depends-on edge: `fromElementId` depends on `toElementId`. */
export interface DependencyGraphRelation {
  id: string;
  fromElementId: string;
  toElementId: string;
  label: string;
}

export interface DependencyGraphModel {
  elements: Map<string, DependencyGraphElement>;
  relations: Map<string, DependencyGraphRelation>;
}

export const emptyModel: DependencyGraphModel = {
  elements: new Map(),
  relations: new Map(),
};

/**
 * Folds one delta into the model.
 *
 * `add` is an upsert keyed on id: an edit arrives as an add carrying the element in its new
 * state, never as a remove-then-add, which would momentarily drop the selection on the thing
 * being edited.
 */
export function applyDelta(model: DependencyGraphModel, delta: Delta): DependencyGraphModel {
  switch (delta.action.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      return model;
  }
}

function added(model: DependencyGraphModel, elements: readonly Element[]): DependencyGraphModel {
  const next: DependencyGraphModel = {
    elements: new Map(model.elements),
    relations: new Map(model.relations),
  };

  for (const element of elements) {
    const id = element.id?.value;
    if (!id) {
      continue;
    }

    // The type check comes BEFORE the decode. Several modules share one delta stream, and a
    // foreign element decoded as ours throws and takes the whole delta with it - a sibling
    // module shipped exactly that bug before finding it.
    switch (element.type) {
      case NODE: {
        const payload = fromBinary(DependencyGraphElementPayloadSchema, element.payload?.value ?? new Uint8Array());
        next.elements.set(id, {
          id,
          x: element.position?.x ?? 0,
          y: element.position?.y ?? 0,
          label: payload.label,
          row: payload.row,
        });
        break;
      }
      case RELATION: {
        const payload = fromBinary(DependencyGraphRelationPayloadSchema, element.payload?.value ?? new Uint8Array());
        next.relations.set(id, {
          id,
          fromElementId: payload.fromElementId,
          toElementId: payload.toElementId,
          label: payload.label,
        });
        break;
      }
      default:
        // Not ours; skipped, never decoded.
        break;
    }
  }

  return next;
}

function removed(model: DependencyGraphModel, ids: readonly { value: string }[]): DependencyGraphModel {
  const next: DependencyGraphModel = {
    elements: new Map(model.elements),
    relations: new Map(model.relations),
  };

  for (const id of ids) {
    next.elements.delete(id.value);
    next.relations.delete(id.value);
  }

  return next;
}
