// The client's picture of one behavior model: the nodes and parent lines the backend streamed,
// decoded. Pure functions over plain data, so the fold is testable without a canvas or a stream.
//
// The type check comes BEFORE the decode, always: nodes and lines share one delta stream, and
// decoding a line as a node would throw and take the whole delta with it.

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  AbmChildPayloadSchema,
  AbmNodePayloadSchema,
  type AbmChildPayload,
  type AbmNodePayload,
} from "@client/generated/agent-behavior-modelling_pb";
import { ABM_CHILD_RELATION, ABM_NODE_KINDS, ABM_TYPE_PREFIX, type AbmNodeKind } from "./abmIds";

/** One node, at its CENTRE - what the backend sends and what the library draws from. */
export interface AbmNode {
  id: string;
  kind: AbmNodeKind;
  x: number;
  y: number;
  payload: AbmNodePayload;
}

/** One parent line. It has no position of its own; the canvas draws it between its ends. */
export interface AbmLine {
  id: string;
  payload: AbmChildPayload;
}

export interface AbmModel {
  nodes: ReadonlyMap<string, AbmNode>;
  lines: ReadonlyMap<string, AbmLine>;
}

export const emptyModel: AbmModel = { nodes: new Map(), lines: new Map() };

/** The kind a wire type names, or undefined for a type this module does not know. */
export function kindOf(wireType: string): AbmNodeKind | undefined {
  if (!wireType.startsWith(ABM_TYPE_PREFIX)) {
    return undefined;
  }

  const short = wireType.slice(ABM_TYPE_PREFIX.length);
  return ABM_NODE_KINDS.find((candidate) => candidate === short);
}

/** Applies one delta, returning a new model; an add is an upsert keyed on id. Never mutates its input. */
export function applyDelta(model: AbmModel, delta: Delta): AbmModel {
  switch (delta.action?.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      return model;
  }
}

function added(model: AbmModel, incoming: readonly Element[]): AbmModel {
  const nodes = new Map(model.nodes);
  const lines = new Map(model.lines);

  for (const element of incoming) {
    const id = element.id?.value;
    if (!id || !element.payload) {
      continue;
    }

    const bytes = element.payload.value;
    const kind = kindOf(element.type);
    if (kind !== undefined) {
      nodes.set(id, { id, kind, x: element.position?.x ?? 0, y: element.position?.y ?? 0, payload: fromBinary(AbmNodePayloadSchema, bytes) });
      continue;
    }

    if (element.type === `${ABM_TYPE_PREFIX}${ABM_CHILD_RELATION}`) {
      lines.set(id, { id, payload: fromBinary(AbmChildPayloadSchema, bytes) });
    }
  }

  return { nodes, lines };
}

function removed(model: AbmModel, ids: readonly { value: string }[]): AbmModel {
  const nodes = new Map(model.nodes);
  const lines = new Map(model.lines);

  for (const id of ids) {
    nodes.delete(id.value);
    lines.delete(id.value);
  }

  return { nodes, lines };
}
