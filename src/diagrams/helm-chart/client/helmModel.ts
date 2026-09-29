// The client's picture of one helm chart diagram: the nodes and edges the backend streamed,
// decoded. Pure functions over plain data, so the reducer that applies deltas is testable
// without a canvas or a stream.
//
// Two delta cases rather than four: nothing on this diagram folds, so the backend never sends
// a group or ungroup delta and there is nothing here to hold.

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  HelmElementKind,
  HelmElementPayloadSchema,
  type HelmElementPayload,
} from "@client/generated/helm-charts_pb";

/** One node or edge as the canvas holds it: where the backend put it, and its decoded payload. */
export interface HelmElement {
  id: string;
  x: number;
  y: number;
  payload: HelmElementPayload;
}

export interface HelmModel {
  /** Every element currently delivered, by id - the whole chart, since a chart is bounded. */
  elements: ReadonlyMap<string, HelmElement>;
}

export const emptyModel: HelmModel = { elements: new Map() };

/**
 * The element kinds this type puts on the wire. Anything else on the stream is ignored - the
 * canvas draws what it understands and never guesses at what it does not.
 */
const ELEMENT_TYPES = new Set([
  "helm/chart+chart",
  "helm/chart+values",
  "helm/chart+schema",
  "helm/chart+template",
  "helm/chart+partial",
  "helm/chart+crds",
  "helm/chart+dependency",
  "helm/chart+subchart",
  "helm/chart+archive",
  "helm/chart+lock",
  "helm/chart+edge",
]);

function decode(element: Element): HelmElement | null {
  if (!ELEMENT_TYPES.has(element.type) || !element.id || !element.payload) {
    return null;
  }

  return {
    id: element.id.value,
    x: element.position?.x ?? 0,
    y: element.position?.y ?? 0,
    payload: fromBinary(HelmElementPayloadSchema, element.payload.value),
  };
}

/**
 * Applies one delta, returning a new model. `add` is an upsert keyed on id; `remove` drops
 * ids. `group` and `ungroup` are ignored rather than handled: this diagram type never emits
 * them, so one arriving means something upstream is confused, and silently doing nothing is
 * better than inventing fold state this diagram has no use for. Never mutates its input.
 */
export function applyDelta(model: HelmModel, delta: Delta): HelmModel {
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
export function nodesOf(model: HelmModel): readonly HelmElement[] {
  return [...model.elements.values()]
    .filter((element) => element.payload.kind !== HelmElementKind.EDGE)
    .sort((left, right) => (left.id < right.id ? -1 : left.id > right.id ? 1 : 0));
}

/** Every edge in the model, in the same stable order. */
export function edgesOf(model: HelmModel): readonly HelmElement[] {
  return [...model.elements.values()]
    .filter((element) => element.payload.kind === HelmElementKind.EDGE)
    .sort((left, right) => (left.id < right.id ? -1 : left.id > right.id ? 1 : 0));
}

/**
 * Where an edge should be drawn from and to, or null when either end is not delivered - which
 * includes every open end, whose stub the canvas draws from the source instead. The canvas
 * anchors on the boxes the backend measured rather than guessing at their size.
 */
export function anchorsOf(
  model: HelmModel,
  edge: HelmElement,
): { from: HelmElement; to: HelmElement } | null {
  const wire = edge.payload.edge;
  if (!wire) {
    return null;
  }

  const from = model.elements.get(wire.sourceId);
  const to = wire.targetId ? model.elements.get(wire.targetId) : undefined;
  return from && to ? { from, to } : null;
}
