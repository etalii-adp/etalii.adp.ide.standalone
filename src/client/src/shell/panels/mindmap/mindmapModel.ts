// The client's picture of one mindmap: the elements the backend streamed, and which of them
// are hidden under a fold. Pure functions over plain data, so the reducer that applies deltas
// is testable without a canvas or a stream (mindmap-diagram Requirement 11).

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "../../../generated/deltas_pb";
import type { Element } from "../../../generated/elements_pb";
import { MindmapNodePayloadSchema, type MindmapNodePayload } from "../../../generated/mindmap_pb";

/** One node as the canvas holds it: where the backend's layout put it, and its decoded payload. */
export interface MindmapElement {
  id: string;
  x: number;
  y: number;
  payload: MindmapNodePayload;
}

export interface MindmapModel {
  /** Every element currently in view, by id. */
  elements: ReadonlyMap<string, MindmapElement>;
  /** A folded parent id -> the descendant ids it stands in for, so the node can show it is folded. */
  folded: ReadonlyMap<string, readonly string[]>;
}

export const emptyModel: MindmapModel = { elements: new Map(), folded: new Map() };

/** The element kind a mindmap node carries; anything else on the stream is ignored (core contract Requirement 4). */
const NODE_TYPE = "freeplane/mindmap+node";

function decode(element: Element): MindmapElement | null {
  if (element.type !== NODE_TYPE || !element.id || !element.payload) {
    return null;
  }

  return {
    id: element.id.value,
    x: element.position?.x ?? 0,
    y: element.position?.y ?? 0,
    payload: fromBinary(MindmapNodePayloadSchema, element.payload.value),
  };
}

/**
 * Applies one delta, returning a new model. `add` is an upsert keyed on id (an edit replaces
 * the element in place); `remove` drops ids; `group` hides a branch under its parent; `ungroup`
 * brings it back. Never mutates its input.
 */
export function applyDelta(model: MindmapModel, delta: Delta): MindmapModel {
  switch (delta.action.case) {
    case "add": {
      const elements = new Map(model.elements);
      for (const raw of delta.action.value.elements) {
        const element = decode(raw);
        if (element) {
          elements.set(element.id, element);
        }
      }
      return { ...model, elements };
    }

    case "remove": {
      const elements = new Map(model.elements);
      for (const id of delta.action.value.elementIds) {
        elements.delete(id.value);
      }
      return { ...model, elements };
    }

    case "group": {
      const elements = new Map(model.elements);
      const hidden = delta.action.value.sourceElementIds.map((id) => id.value);
      for (const id of hidden) {
        elements.delete(id);
      }
      const groupElement = delta.action.value.groupElement ? decode(delta.action.value.groupElement) : null;
      const folded = new Map(model.folded);
      if (groupElement) {
        elements.set(groupElement.id, groupElement);
        folded.set(groupElement.id, hidden);
      }
      return { elements, folded };
    }

    case "ungroup": {
      const elements = new Map(model.elements);
      for (const raw of delta.action.value.elements) {
        const element = decode(raw);
        if (element) {
          elements.set(element.id, element);
        }
      }
      const folded = new Map(model.folded);
      folded.delete(delta.action.value.groupElementId?.value ?? "");
      return { elements, folded };
    }

    default:
      return model;
  }
}

/** Whether a node has hidden children on this connection - what the fold indicator shows. */
export function isFolded(model: MindmapModel, id: string): boolean {
  return model.folded.has(id);
}

/** The id of the node whose text chain ends at `path`, or undefined - used to follow a pushed selection to a node. */
export function nodeIdForPath(model: MindmapModel, path: readonly string[]): string | undefined {
  // The backend's element carries no path, so the canvas matches on the innermost text, which
  // is the node's own. Ambiguity across same-named siblings is resolved by the backend's
  // verified selection; this is only the client's best guess for centring the view.
  const last = path[path.length - 1];
  for (const element of model.elements.values()) {
    if (element.payload.text === last) {
      return element.id;
    }
  }
  return undefined;
}
