// The client's picture of one .NET dependency graph: the nodes and edges the backend streamed,
// decoded. Pure functions over plain data, so the reducer that applies deltas is testable
// without a canvas or a stream.
//
// Two delta cases rather than four: nothing on this diagram folds, so the backend never sends a
// group or ungroup delta and there is nothing here to hold.

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  DependencyElementKind,
  DependencyElementPayloadSchema,
  type DependencyElementPayload,
} from "@client/generated/dotnet-dependency-graph_pb";

/** One node or edge as the canvas holds it: where the backend put it, and its decoded payload. */
export interface DependencyElement {
  id: string;
  x: number;
  y: number;
  payload: DependencyElementPayload;
}

export interface DotNetDependencyGraphModel {
  /** Every element currently delivered, by id. */
  elements: ReadonlyMap<string, DependencyElement>;
}

export const emptyModel: DotNetDependencyGraphModel = { elements: new Map() };

/**
 * The element kinds this type puts on the wire. Anything else on the stream is ignored - the
 * canvas draws what it understands and never guesses at what it does not.
 */
const ELEMENT_TYPES = new Set([
  "dotnet/dependency-graph+project",
  "dotnet/dependency-graph+package",
  "dotnet/dependency-graph+edge",
]);

function decode(element: Element): DependencyElement | null {
  if (!ELEMENT_TYPES.has(element.type) || !element.id || !element.payload) {
    return null;
  }

  return {
    id: element.id.value,
    x: element.position?.x ?? 0,
    y: element.position?.y ?? 0,
    payload: fromBinary(DependencyElementPayloadSchema, element.payload.value),
  };
}

/**
 * Applies one delta, returning a new model. `add` is an upsert keyed on id; `remove` drops ids.
 * `group` and `ungroup` are ignored rather than handled: this diagram type never emits them, so
 * one arriving means something upstream is confused, and doing nothing is better than inventing
 * fold state this diagram has no use for. Never mutates its input.
 */
export function applyDelta(model: DotNetDependencyGraphModel, delta: Delta): DotNetDependencyGraphModel {
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

/** Whether an element is one of the two node kinds rather than an edge. */
export function isNode(element: DependencyElement): boolean {
  return (
    element.payload.kind === DependencyElementKind.PROJECT ||
    element.payload.kind === DependencyElementKind.PACKAGE
  );
}

/** Every node, in a stable order so the canvas renders the same list each time. */
export function nodesOf(model: DotNetDependencyGraphModel): readonly DependencyElement[] {
  return [...model.elements.values()]
    .filter(isNode)
    .sort((left, right) => (left.id < right.id ? -1 : left.id > right.id ? 1 : 0));
}

/** Every edge, in the same stable order. */
export function edgesOf(model: DotNetDependencyGraphModel): readonly DependencyElement[] {
  return [...model.elements.values()]
    .filter((element) => !isNode(element))
    .sort((left, right) => (left.id < right.id ? -1 : left.id > right.id ? 1 : 0));
}

/**
 * The two ends of an edge, taken from its own id.
 *
 * The backend builds an edge id as `depends:<from>-><to>`, so the ends are carried by the id
 * itself rather than duplicated into the payload - one source of truth, and an edge whose ends
 * disagreed with its id would be impossible rather than merely unlikely. Null when either end
 * has not been delivered, which is what stops a connector being drawn to nothing.
 */
export function endsOf(
  model: DotNetDependencyGraphModel,
  edge: DependencyElement,
): { from: DependencyElement; to: DependencyElement } | null {
  const withoutPrefix = edge.id.startsWith("depends:") ? edge.id.slice("depends:".length) : "";
  const separator = withoutPrefix.indexOf("->");
  if (separator < 0) {
    return null;
  }

  const from = model.elements.get(withoutPrefix.slice(0, separator));
  const to = model.elements.get(withoutPrefix.slice(separator + 2));
  return from && to ? { from, to } : null;
}
