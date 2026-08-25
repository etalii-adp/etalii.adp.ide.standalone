// The client's picture of one C4 view: the elements, relationships and boundaries the backend
// streamed, plus the view's own furniture. Pure functions over plain data, so the reducer that
// applies deltas is testable without a canvas or a stream (c4-diagrams Requirement 1).

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import {
  C4BoundaryPayloadSchema,
  C4ElementPayloadSchema,
  C4RelationshipPayloadSchema,
  C4ViewPayloadSchema,
  type C4BoundaryPayload,
  type C4ElementPayload,
  type C4RelationshipPayload,
  type C4ViewPayload,
} from "@client/generated/c4_pb";

/** The element kinds the C4 modules put on the wire; anything else on the stream is ignored. */
export const NODE_TYPE = "c4/model+node";
export const RELATIONSHIP_TYPE = "c4/model+relationship";
export const BOUNDARY_TYPE = "c4/model+boundary";
export const VIEW_TYPE = "c4/model+view";

/** One box: where the backend's layout put it, and its decoded payload. */
export interface C4Node {
  id: string;
  x: number;
  y: number;
  payload: C4ElementPayload;
}

/** One line. Both ends carry their measured boxes, so the canvas anchors on real edges. */
export interface C4Relationship {
  id: string;
  payload: C4RelationshipPayload;
}

/** The dashed rectangle around a system's containers or a container's components. */
export interface C4BoundaryBox {
  id: string;
  x: number;
  y: number;
  payload: C4BoundaryPayload;
}

export interface C4Model {
  nodes: ReadonlyMap<string, C4Node>;
  relationships: ReadonlyMap<string, C4Relationship>;
  boundaries: ReadonlyMap<string, C4BoundaryBox>;
  /** The title and legend C4 requires every diagram to carry, or null before the baseline lands. */
  view: C4ViewPayload | null;
}

export const emptyModel: C4Model = {
  nodes: new Map(),
  relationships: new Map(),
  boundaries: new Map(),
  view: null,
};

/**
 * Applies one delta, returning a new model. `add` is an upsert keyed on id, so an edit replaces
 * the element in place and a re-delivery after a document change simply overwrites. Never
 * mutates its input.
 */
export function applyDelta(model: C4Model, delta: Delta): C4Model {
  switch (delta.action.case) {
    case "add": {
      const nodes = new Map(model.nodes);
      const relationships = new Map(model.relationships);
      const boundaries = new Map(model.boundaries);
      let view = model.view;

      for (const raw of delta.action.value.elements) {
        const id = raw.id?.value;
        if (!id || !raw.payload) {
          continue;
        }

        switch (raw.type) {
          case NODE_TYPE:
            nodes.set(id, {
              id,
              x: raw.position?.x ?? 0,
              y: raw.position?.y ?? 0,
              payload: fromBinary(C4ElementPayloadSchema, raw.payload.value),
            });
            break;

          case RELATIONSHIP_TYPE:
            relationships.set(id, { id, payload: fromBinary(C4RelationshipPayloadSchema, raw.payload.value) });
            break;

          case BOUNDARY_TYPE:
            boundaries.set(id, {
              id,
              x: raw.position?.x ?? 0,
              y: raw.position?.y ?? 0,
              payload: fromBinary(C4BoundaryPayloadSchema, raw.payload.value),
            });
            break;

          case VIEW_TYPE:
            view = fromBinary(C4ViewPayloadSchema, raw.payload.value);
            break;

          default:
            break;
        }
      }

      return { nodes, relationships, boundaries, view };
    }

    case "remove": {
      const nodes = new Map(model.nodes);
      const relationships = new Map(model.relationships);
      const boundaries = new Map(model.boundaries);
      for (const id of delta.action.value.elementIds) {
        nodes.delete(id.value);
        relationships.delete(id.value);
        boundaries.delete(id.value);
      }
      return { ...model, nodes, relationships, boundaries };
    }

    default:
      // C4 never groups or ungroups: a view's membership is the document's business, not a
      // fold the client performs.
      return model;
  }
}

/** Every element as a box, which is what the canvas draws and what fit-to-view measures. */
export function boxesOf(model: C4Model): { x: number; y: number; width: number; height: number }[] {
  const boxes = [...model.nodes.values()].map((node) => ({
    x: node.x - node.payload.width / 2,
    y: node.y - node.payload.height / 2,
    width: node.payload.width,
    height: node.payload.height,
  }));

  for (const boundary of model.boundaries.values()) {
    boxes.push({
      x: boundary.x - boundary.payload.width / 2,
      y: boundary.y - boundary.payload.height / 2,
      width: boundary.payload.width,
      height: boundary.payload.height,
    });
  }

  return boxes;
}
