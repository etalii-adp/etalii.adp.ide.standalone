// The client's picture of one hype cycle graph: the trends, triggers, notes and influences the
// backend streamed, decoded. Pure functions over plain data, so the fold is testable without a canvas or a stream.
//
// The type check comes BEFORE the decode, always: trends and influences share one delta stream,
// and decoding an influence as a trend would throw and take the whole delta with it.

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  GhgInfluencePayloadSchema,
  GhgNotePayloadSchema,
  GhgTrendPayloadSchema,
  GhgTriggerPayloadSchema,
  type GhgInfluencePayload,
  type GhgNotePayload,
  type GhgTrendPayload,
  type GhgTriggerPayload,
} from "@client/generated/gartner-hypecycle-graph_pb";
import { GHG_TYPE_PREFIX, GhgElementTypes, GhgRelationTypes } from "./ghgIds";

/** One trend, at its CENTRE - which is what the backend sends and what the library draws from. */
export interface GhgTrend {
  id: string;
  x: number;
  y: number;
  payload: GhgTrendPayload;
}

/** One trigger, at its CENTRE: the start of its month, on its row's middle. */
export interface GhgTrigger {
  id: string;
  x: number;
  y: number;
  payload: GhgTriggerPayload;
}

/** One note, at its CENTRE. */
export interface GhgNote {
  id: string;
  x: number;
  y: number;
  payload: GhgNotePayload;
}

/** One influence. It has no position of its own; the canvas draws it between its ends. */
export interface GhgInfluence {
  id: string;
  payload: GhgInfluencePayload;
}

export interface GhgModel {
  trends: ReadonlyMap<string, GhgTrend>;
  triggers: ReadonlyMap<string, GhgTrigger>;
  notes: ReadonlyMap<string, GhgNote>;
  influences: ReadonlyMap<string, GhgInfluence>;
}

export const emptyModel: GhgModel = {
  trends: new Map(),
  triggers: new Map(),
  notes: new Map(),
  influences: new Map(),
};

const TREND_TYPE = `${GHG_TYPE_PREFIX}${GhgElementTypes.trend}`;
const TRIGGER_TYPE = `${GHG_TYPE_PREFIX}${GhgElementTypes.trigger}`;
const NOTE_TYPE = `${GHG_TYPE_PREFIX}${GhgElementTypes.note}`;
const INFLUENCE_TYPE = `${GHG_TYPE_PREFIX}${GhgRelationTypes.influence}`;

/**
 * Applies one delta, returning a new model. Never mutates its input. An add is an UPSERT keyed on
 * id - a changed trend arrives as an add of the same id - and only a remove removes.
 */
export function applyDelta(model: GhgModel, delta: Delta): GhgModel {
  switch (delta.action?.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      // Group and ungroup are never emitted by this type; doing nothing beats inventing fold state.
      return model;
  }
}

function added(model: GhgModel, incoming: readonly Element[]): GhgModel {
  const trends = new Map(model.trends);
  const triggers = new Map(model.triggers);
  const notes = new Map(model.notes);
  const influences = new Map(model.influences);

  for (const element of incoming) {
    const id = element.id?.value;
    if (!id || !element.payload) {
      continue;
    }

    const bytes = element.payload.value;
    if (element.type === TREND_TYPE) {
      trends.set(id, {
        id,
        x: element.position?.x ?? 0,
        y: element.position?.y ?? 0,
        payload: fromBinary(GhgTrendPayloadSchema, bytes),
      });
    } else if (element.type === TRIGGER_TYPE) {
      triggers.set(id, {
        id,
        x: element.position?.x ?? 0,
        y: element.position?.y ?? 0,
        payload: fromBinary(GhgTriggerPayloadSchema, bytes),
      });
    } else if (element.type === NOTE_TYPE) {
      notes.set(id, {
        id,
        x: element.position?.x ?? 0,
        y: element.position?.y ?? 0,
        payload: fromBinary(GhgNotePayloadSchema, bytes),
      });
    } else if (element.type === INFLUENCE_TYPE) {
      influences.set(id, { id, payload: fromBinary(GhgInfluencePayloadSchema, bytes) });
    }
  }

  return { trends, triggers, notes, influences };
}

function removed(model: GhgModel, ids: readonly { value: string }[]): GhgModel {
  const trends = new Map(model.trends);
  const triggers = new Map(model.triggers);
  const notes = new Map(model.notes);
  const influences = new Map(model.influences);

  for (const id of ids) {
    trends.delete(id.value);
    triggers.delete(id.value);
    notes.delete(id.value);
    influences.delete(id.value);
  }

  return { trends, triggers, notes, influences };
}
