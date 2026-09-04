// The client's picture of one causal loop diagram: the variables, links and loop labels the
// backend streamed, decoded. Pure functions over plain data, so the reducer is testable without
// a canvas or a stream.
//
// Three payload types rather than one tagged payload, so the type check comes BEFORE the decode:
// several element kinds share one delta stream, and decoding a link as a variable would throw
// and take the whole delta with it.

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  CausalLoopLinkPayloadSchema,
  CausalLoopLoopPayloadSchema,
  CausalLoopPolarityProto,
  CausalLoopVariablePayloadSchema,
  LoopPolarityProto,
  type CausalLoopLinkPayload,
  type CausalLoopLoopPayload,
  type CausalLoopVariablePayload,
} from "@client/generated/causal-loop_pb";

/** The mime-style element types the backend sends. */
export const VARIABLE = "systems/causal-loop+variable";
export const LINK = "systems/causal-loop+link";
export const LOOP = "systems/causal-loop+loop";

export { CausalLoopPolarityProto, LoopPolarityProto };

/** One variable, at the position the backend placed it and the size it reserved. */
export interface CausalLoopVariable {
  id: string;
  x: number;
  y: number;
  payload: CausalLoopVariablePayload;
}

/** One causal link. It has no position of its own; the canvas draws it between its endpoints. */
export interface CausalLoopLink {
  id: string;
  /** Where the polarity mark and any delay strokes are drawn: the midpoint of the two boxes. */
  x: number;
  y: number;
  payload: CausalLoopLinkPayload;
}

/** One loop label, drawn at the centroid of the variables it runs through. */
export interface CausalLoopLoop {
  id: string;
  x: number;
  y: number;
  payload: CausalLoopLoopPayload;
}

export interface CausalLoopModel {
  variables: ReadonlyMap<string, CausalLoopVariable>;
  links: ReadonlyMap<string, CausalLoopLink>;
  loops: ReadonlyMap<string, CausalLoopLoop>;
}

export const emptyModel: CausalLoopModel = {
  variables: new Map(),
  links: new Map(),
  loops: new Map(),
};

/** A mutable working copy: the model's own maps are readonly to everyone outside this file. */
interface MutableCausalLoopModel {
  variables: Map<string, CausalLoopVariable>;
  links: Map<string, CausalLoopLink>;
  loops: Map<string, CausalLoopLoop>;
}

function copy(model: CausalLoopModel): MutableCausalLoopModel {
  return {
    variables: new Map(model.variables),
    links: new Map(model.links),
    loops: new Map(model.loops),
  };
}

/** Applies one delta, returning a new model. Never mutates its input. */
export function applyDelta(model: CausalLoopModel, delta: Delta): CausalLoopModel {
  switch (delta.action?.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      // group and ungroup are never emitted by this type: nothing here folds. One arriving
      // means something upstream is confused, and doing nothing beats inventing fold state.
      return model;
  }
}

function added(model: CausalLoopModel, elements: readonly Element[]): CausalLoopModel {
  const next = copy(model);

  for (const element of elements) {
    const id = element.id?.value;
    if (!id || !element.payload) {
      continue;
    }

    const bytes = element.payload.value;
    const x = element.position?.x ?? 0;
    const y = element.position?.y ?? 0;

    // The type check precedes the decode, always.
    switch (element.type) {
      case VARIABLE:
        next.variables.set(id, { id, x, y, payload: fromBinary(CausalLoopVariablePayloadSchema, bytes) });
        break;
      case LINK:
        next.links.set(id, { id, x, y, payload: fromBinary(CausalLoopLinkPayloadSchema, bytes) });
        break;
      case LOOP:
        next.loops.set(id, { id, x, y, payload: fromBinary(CausalLoopLoopPayloadSchema, bytes) });
        break;
      default:
        break;
    }
  }

  return next;
}

function removed(model: CausalLoopModel, ids: readonly { value: string }[]): CausalLoopModel {
  const next = copy(model);

  for (const id of ids) {
    next.variables.delete(id.value);
    next.links.delete(id.value);
    next.loops.delete(id.value);
  }

  return next;
}

/** The two boxes a link runs between, or null when the client does not hold both. */
export function anchorsOf(
  model: CausalLoopModel,
  link: CausalLoopLink,
): { from: CausalLoopVariable; to: CausalLoopVariable } | null {
  const from = model.variables.get(link.payload.fromElementId);
  const to = model.variables.get(link.payload.toElementId);
  return from && to ? { from, to } : null;
}

/** What a link's polarity reads as beside its arrowhead. */
export function polarityMark(polarity: CausalLoopPolarityProto): string {
  switch (polarity) {
    case CausalLoopPolarityProto.POSITIVE:
      return "+";
    case CausalLoopPolarityProto.NEGATIVE:
      return "−";
    default:
      // Nothing rather than a guess: an unmarked link is one the author has not decided, and
      // drawing a "+" would put a claim on the canvas the document does not make.
      return "";
  }
}

/**
 * The thickness step a weight is drawn at.
 *
 * Deliberately a short ladder rather than a continuous stroke width. A weight is an author's
 * annotation and not a measurement, so a coarse scale is honest about what it is - and the
 * shared stylesheet offers a small set of widths, which a module may use but must not replace
 * with private appearance.
 */
export function weightStep(link: CausalLoopLink): "light" | "normal" | "heavy" {
  if (!link.payload.hasWeight) {
    return "normal";
  }

  if (link.payload.weight >= 2) {
    return "heavy";
  }

  return link.payload.weight <= 0.5 ? "light" : "normal";
}

/** How a loop's identifier and name read together. */
export function loopCaption(loop: CausalLoopLoop): string {
  return loop.payload.name.length > 0
    ? `${loop.payload.identifier} · ${loop.payload.name}`
    : loop.payload.identifier;
}
