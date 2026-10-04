// The client's picture of one supply chain diagram: the groups, nodes and flows the backend
// streamed, decoded. Pure functions over plain data, so the fold is testable without a canvas.
//
// The type check comes BEFORE the decode, always: everything shares one delta stream, and decoding
// a flow as a node would throw and take the whole delta with it.

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  SupplyChainFlowPayloadSchema,
  SupplyChainGroupPayloadSchema,
  SupplyChainNodePayloadSchema,
  type SupplyChainFlowPayload,
  type SupplyChainGroupPayload,
  type SupplyChainNodePayload,
} from "@client/generated/supply-chain_pb";
import { FLOW_TYPE, GROUP_TYPE, SUPPLY_CHAIN_STAGES, SUPPLY_CHAIN_TYPE_PREFIX, type SupplyChainStage } from "./supplyChainIds";

/** A group's frame, at its CENTRE - which is what the backend sends and the library draws from. */
export interface SupplyChainGroup {
  id: string;
  x: number;
  y: number;
  payload: SupplyChainGroupPayload;
}

/** One node, at its centre. */
export interface SupplyChainNode {
  id: string;
  stage: SupplyChainStage;
  x: number;
  y: number;
  payload: SupplyChainNodePayload;
}

/** One flow. It has no position of its own; the canvas draws it between its ends. */
export interface SupplyChainFlow {
  id: string;
  payload: SupplyChainFlowPayload;
}

export interface SupplyChainModel {
  groups: ReadonlyMap<string, SupplyChainGroup>;
  nodes: ReadonlyMap<string, SupplyChainNode>;
  flows: ReadonlyMap<string, SupplyChainFlow>;
}

export const emptyModel: SupplyChainModel = {
  groups: new Map(),
  nodes: new Map(),
  flows: new Map(),
};

/** The document's own word for a wire type, or undefined for one this module does not send. */
function shortTypeOf(wireType: string): string | undefined {
  return wireType.startsWith(SUPPLY_CHAIN_TYPE_PREFIX) ? wireType.slice(SUPPLY_CHAIN_TYPE_PREFIX.length) : undefined;
}

function isStage(type: string): type is SupplyChainStage {
  return (SUPPLY_CHAIN_STAGES as readonly string[]).includes(type);
}

/**
 * Applies one delta, returning a new model. Never mutates its input.
 *
 * An add is an UPSERT keyed on id - a changed element, a re-marked trace included, arrives as an
 * add of the same id - and only a remove removes.
 */
export function applyDelta(model: SupplyChainModel, delta: Delta): SupplyChainModel {
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

function added(model: SupplyChainModel, incoming: readonly Element[]): SupplyChainModel {
  const groups = new Map(model.groups);
  const nodes = new Map(model.nodes);
  const flows = new Map(model.flows);

  for (const element of incoming) {
    const id = element.id?.value;
    const type = shortTypeOf(element.type);
    if (!id || !element.payload || type === undefined) {
      continue;
    }

    const bytes = element.payload.value;
    const x = element.position?.x ?? 0;
    const y = element.position?.y ?? 0;
    if (type === GROUP_TYPE) {
      groups.set(id, { id, x, y, payload: fromBinary(SupplyChainGroupPayloadSchema, bytes) });
    } else if (type === FLOW_TYPE) {
      flows.set(id, { id, payload: fromBinary(SupplyChainFlowPayloadSchema, bytes) });
    } else if (isStage(type)) {
      nodes.set(id, { id, stage: type, x, y, payload: fromBinary(SupplyChainNodePayloadSchema, bytes) });
    }
  }

  return { groups, nodes, flows };
}

function removed(model: SupplyChainModel, ids: readonly { value: string }[]): SupplyChainModel {
  const groups = new Map(model.groups);
  const nodes = new Map(model.nodes);
  const flows = new Map(model.flows);

  for (const id of ids) {
    groups.delete(id.value);
    nodes.delete(id.value);
    flows.delete(id.value);
  }

  return { groups, nodes, flows };
}

/**
 * A number the way a card shows it: whole numbers plain, fractions to at most two places, and
 * thousands grouped - `5,500`, `2.8`, `0.16`.
 */
export function formatAmount(value: number): string {
  return value.toLocaleString("en-US", { maximumFractionDigits: value !== 0 && Math.abs(value) < 1 ? 3 : 2 });
}
