// The client's picture of one Ansible structure diagram: the nodes and edges the backend
// streamed, decoded. Pure functions over plain data, so the reducer that applies deltas is
// testable without a canvas or a stream.
//
// Note what is missing compared with the mindmap's model: no fold state. Nothing on this
// diagram folds, so the backend never sends a group or ungroup delta and there is nothing here
// to hold - which is why applyDelta handles two cases rather than four.

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  AnsibleElementKind,
  AnsibleElementPayloadSchema,
  type AnsibleElementPayload,
} from "@client/generated/ansible-structure_pb";

/** One node or edge as the canvas holds it: where the backend's layout put it, and its decoded payload. */
export interface AnsibleElement {
  id: string;
  x: number;
  y: number;
  payload: AnsibleElementPayload;
}

export interface AnsibleModel {
  /** Every element currently in view, by id. */
  elements: ReadonlyMap<string, AnsibleElement>;
}

export const emptyModel: AnsibleModel = { elements: new Map() };

/**
 * The element kinds this type puts on the wire. Anything else on the stream is ignored - the
 * canvas draws what it understands and never guesses at what it does not.
 */
const ELEMENT_TYPES = new Set([
  "ansible/structure+playbook",
  "ansible/structure+play",
  "ansible/structure+role",
  "ansible/structure+taskfile",
  "ansible/structure+inventory",
  "ansible/structure+vars",
  "ansible/structure+edge",
]);

function decode(element: Element): AnsibleElement | null {
  if (!ELEMENT_TYPES.has(element.type) || !element.id || !element.payload) {
    return null;
  }

  return {
    id: element.id.value,
    x: element.position?.x ?? 0,
    y: element.position?.y ?? 0,
    payload: fromBinary(AnsibleElementPayloadSchema, element.payload.value),
  };
}

/**
 * Applies one delta, returning a new model. `add` is an upsert keyed on id; `remove` drops ids.
 * `group` and `ungroup` are ignored rather than handled: this diagram type never emits them, so
 * one arriving means something upstream is confused, and silently doing nothing is better than
 * inventing fold state a read-only diagram has no use for. Never mutates its input.
 */
export function applyDelta(model: AnsibleModel, delta: Delta): AnsibleModel {
  switch (delta.action.case) {
    case "add": {
      const elements = new Map(model.elements);
      for (const raw of delta.action.value.elements) {
        const element = decode(raw);
        if (element) {
          elements.set(element.id, element);
        }
      }
      return { elements };
    }

    case "remove": {
      const elements = new Map(model.elements);
      for (const id of delta.action.value.elementIds) {
        elements.delete(id.value);
      }
      return { elements };
    }

    default:
      return model;
  }
}

/** Every node in the model, in a stable order so the canvas renders the same list each time. */
export function nodesOf(model: AnsibleModel): readonly AnsibleElement[] {
  return [...model.elements.values()]
    .filter((element) => element.payload.kind !== AnsibleElementKind.EDGE)
    .sort((left, right) => (left.id < right.id ? -1 : left.id > right.id ? 1 : 0));
}

/** Every edge in the model, in the same stable order. */
export function edgesOf(model: AnsibleModel): readonly AnsibleElement[] {
  return [...model.elements.values()]
    .filter((element) => element.payload.kind === AnsibleElementKind.EDGE)
    .sort((left, right) => (left.id < right.id ? -1 : left.id > right.id ? 1 : 0));
}

/**
 * Where an edge should be drawn from and to, or null when either end is not in view. The canvas
 * anchors on the boxes the backend measured rather than guessing at their size - a client-side
 * size guess is what made wide nodes overlap their neighbours in the mindmap module.
 */
export function anchorsOf(
  model: AnsibleModel,
  edge: AnsibleElement,
): { from: AnsibleElement; to: AnsibleElement } | null {
  const wire = edge.payload.edge;
  if (!wire) {
    return null;
  }

  const from = model.elements.get(wire.sourceId);
  const to = wire.targetId ? model.elements.get(wire.targetId) : undefined;
  return from && to ? { from, to } : null;
}

/**
 * The palette slot a node belongs in. An index, never a colour on the wire: the palette itself
 * lives in the stylesheet, so a theme change is a stylesheet change rather than a protocol one.
 * Negative means "belongs to no play", which the stylesheet renders as its neutral slot.
 */
export function paletteSlotOf(element: AnsibleElement, slots: number): number {
  const index = element.payload.playIndex;
  return index < 0 ? -1 : index % slots;
}
