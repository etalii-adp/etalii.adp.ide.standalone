// The client's picture of one functional decomposition graph: the elements and connections the
// backend streamed, decoded. Pure functions over plain data, so the fold is testable without a
// canvas or a stream.
//
// The type check comes BEFORE the decode, always: elements and connections share one delta
// stream, and decoding a connection as an element would throw and take the whole delta with it.

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  FdgConnectionPayloadSchema,
  FdgElementPayloadSchema,
  type FdgConnectionPayload,
  type FdgElementPayload,
} from "@client/generated/functional-decomposition-graph_pb";
import {
  FDG_ELEMENT_TYPES,
  FDG_RELATION_TYPES,
  FDG_TYPE_PREFIX,
  type FdgElementType,
  type FdgRelationType,
} from "./fdgIds";

/** One element, at its CENTRE - which is what the backend sends and what the library draws from. */
export interface FdgElement {
  id: string;
  type: FdgElementType;
  x: number;
  y: number;
  payload: FdgElementPayload;
}

/** One connection. It has no position of its own; the canvas draws it between its endpoints. */
export interface FdgConnection {
  id: string;
  type: FdgRelationType;
  payload: FdgConnectionPayload;
}

export interface FdgModel {
  elements: ReadonlyMap<string, FdgElement>;
  connections: ReadonlyMap<string, FdgConnection>;
}

export const emptyModel: FdgModel = {
  elements: new Map(),
  connections: new Map(),
};

/** The document's own type word for a wire type, or undefined for a type this module does not know. */
function shortTypeOf<T extends string>(wireType: string, known: readonly T[]): T | undefined {
  if (!wireType.startsWith(FDG_TYPE_PREFIX)) {
    return undefined;
  }

  const short = wireType.slice(FDG_TYPE_PREFIX.length);
  return known.find((candidate) => candidate === short);
}

/**
 * Applies one delta, returning a new model. Never mutates its input.
 *
 * An add is an UPSERT keyed on id - a changed element arrives as an add of the same id - and only a
 * remove removes (the design's delta rule).
 */
export function applyDelta(model: FdgModel, delta: Delta): FdgModel {
  switch (delta.action?.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      // Group and ungroup are never emitted by this type. One arriving means something upstream
      // is confused, and doing nothing beats inventing fold state.
      return model;
  }
}

function added(model: FdgModel, incoming: readonly Element[]): FdgModel {
  const elements = new Map(model.elements);
  const connections = new Map(model.connections);

  for (const element of incoming) {
    const id = element.id?.value;
    if (!id || !element.payload) {
      continue;
    }

    const bytes = element.payload.value;
    const elementType = shortTypeOf(element.type, FDG_ELEMENT_TYPES);
    if (elementType !== undefined) {
      elements.set(id, {
        id,
        type: elementType,
        x: element.position?.x ?? 0,
        y: element.position?.y ?? 0,
        payload: fromBinary(FdgElementPayloadSchema, bytes),
      });
      continue;
    }

    const relationType = shortTypeOf(element.type, FDG_RELATION_TYPES);
    if (relationType !== undefined) {
      connections.set(id, { id, type: relationType, payload: fromBinary(FdgConnectionPayloadSchema, bytes) });
    }
  }

  return { elements, connections };
}

function removed(model: FdgModel, ids: readonly { value: string }[]): FdgModel {
  const elements = new Map(model.elements);
  const connections = new Map(model.connections);

  for (const id of ids) {
    elements.delete(id.value);
    connections.delete(id.value);
  }

  return { elements, connections };
}
