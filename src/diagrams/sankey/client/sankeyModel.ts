// The client's picture of one Sankey diagram: the nodes and flows the backend streamed, decoded.
// Pure functions over plain data, so the fold is testable without a canvas.
//
// The type check comes BEFORE the decode, always: everything shares one delta stream, and decoding
// a flow as a node would throw and take the whole delta with it.

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import { SankeyFlowPayloadSchema, SankeyNodePayloadSchema, type SankeyFlowPayload, type SankeyNodePayload } from "@client/generated/sankey_pb";
import { FLOW_TYPE, NODE_TYPE, SANKEY_TYPE_PREFIX } from "./sankeyIds";

/** One node, at its CENTRE - which is what the backend sends and the library draws from. */
export interface SankeyNode {
  id: string;
  x: number;
  y: number;
  payload: SankeyNodePayload;
}

/** One flow. It has no position of its own; the canvas draws it between its ends. */
export interface SankeyFlow {
  id: string;
  payload: SankeyFlowPayload;
}

export interface SankeyModel {
  nodes: ReadonlyMap<string, SankeyNode>;
  flows: ReadonlyMap<string, SankeyFlow>;
}

export const emptyModel: SankeyModel = {
  nodes: new Map(),
  flows: new Map(),
};

/**
 * Applies one delta, returning a new model. Never mutates its input.
 *
 * An add is an UPSERT keyed on id - a node the layout moved arrives as an add of the same id -
 * and only a remove removes.
 */
export function applyDelta(model: SankeyModel, delta: Delta): SankeyModel {
  switch (delta.action?.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      // Group and ungroup deltas are never emitted by this type.
      return model;
  }
}

function added(model: SankeyModel, incoming: readonly Element[]): SankeyModel {
  const nodes = new Map(model.nodes);
  const flows = new Map(model.flows);

  for (const element of incoming) {
    const id = element.id?.value;
    if (!id || !element.payload || !element.type.startsWith(SANKEY_TYPE_PREFIX)) {
      continue;
    }

    const type = element.type.slice(SANKEY_TYPE_PREFIX.length);
    const bytes = element.payload.value;
    if (type === NODE_TYPE) {
      nodes.set(id, { id, x: element.position?.x ?? 0, y: element.position?.y ?? 0, payload: fromBinary(SankeyNodePayloadSchema, bytes) });
    } else if (type === FLOW_TYPE) {
      flows.set(id, { id, payload: fromBinary(SankeyFlowPayloadSchema, bytes) });
    }
  }

  return { nodes, flows };
}

function removed(model: SankeyModel, ids: readonly { value: string }[]): SankeyModel {
  const nodes = new Map(model.nodes);
  const flows = new Map(model.flows);

  for (const id of ids) {
    nodes.delete(id.value);
    flows.delete(id.value);
  }

  return { nodes, flows };
}

/** The gap the layout keeps between two nodes of one column - the backend's `SankeyGeometry.NodeGap`. */
export const NODE_GAP = 36;

/** Where a dragged node would go in its column, and where every other node of that column moves to make room. */
export interface ColumnPlace {
  /** The node's index among the column's nodes once dropped. */
  index: number;
  /** Whether that differs from where it is. */
  moved: boolean;
  /** The centre y the dragged node settles at - its slot. */
  slotY: number;
  /** How far each other node of the column moves to make room, by id; nodes that stay are absent. */
  shifts: ReadonlyMap<string, number>;
}

/**
 * The place a node dragged to `centreY` takes in its column, and the room the others make for it.
 *
 * <b>The order within a column is the only thing a reader arranges</b>; the backend writes it as the
 * order of the entries, and its layout settles everything else. So a drag answers one question -
 * which neighbours does it pass - and those neighbours step aside by the dragged node's height and
 * the gap, the same rule `SankeySession.PlaceOf` applies when the drop arrives.
 */
export function columnPlaceOf(model: SankeyModel, id: string, centreY: number): ColumnPlace | null {
  const dragged = model.nodes.get(id);
  if (dragged === undefined) {
    return null;
  }

  const column = [...model.nodes.values()]
    .filter((node) => node.payload.column === dragged.payload.column)
    .sort((one, other) => one.y - other.y);
  const from = column.findIndex((node) => node.id === id);
  const others = column.filter((node) => node.id !== id);
  const index = others.filter((node) => node.y < centreY).length;
  const room = dragged.payload.height + NODE_GAP;
  const shifts = new Map<string, number>();

  if (index === from) {
    return { index, moved: false, slotY: dragged.y, shifts };
  }

  if (index > from) {
    // Moving down: the nodes it passes move up into the space it left.
    for (const node of others.slice(from, index)) {
      shifts.set(node.id, -room);
    }
    const last = others[index - 1];
    return { index, moved: true, slotY: last.y + last.payload.height / 2 - room + NODE_GAP + dragged.payload.height / 2, shifts };
  }

  // Moving up: the nodes it passes move down below it.
  for (const node of others.slice(index, from)) {
    shifts.set(node.id, room);
  }
  const first = others[index];
  return { index, moved: true, slotY: first.y - first.payload.height / 2 + dragged.payload.height / 2, shifts };
}
