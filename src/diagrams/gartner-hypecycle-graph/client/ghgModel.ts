// The client's picture of one hype cycle graph: the trends and influences the backend streamed,
// decoded. Pure functions over plain data, so the fold is testable without a canvas or a stream.
//
// The type check comes BEFORE the decode, always: trends and influences share one delta stream,
// and decoding an influence as a trend would throw and take the whole delta with it.

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  GhgInfluencePayloadSchema,
  GhgTrendPayloadSchema,
  type GhgInfluencePayload,
  type GhgTrendPayload,
} from "@client/generated/gartner-hypecycle-graph_pb";
import { GHG_TYPE_PREFIX, GhgElementTypes, GhgRelationTypes } from "./ghgIds";

/** One trend, at its CENTRE - which is what the backend sends and what the library draws from. */
export interface GhgTrend {
  id: string;
  x: number;
  y: number;
  payload: GhgTrendPayload;
}

/** One influence. It has no position of its own; the canvas draws it between its ends. */
export interface GhgInfluence {
  id: string;
  payload: GhgInfluencePayload;
}

export interface GhgModel {
  trends: ReadonlyMap<string, GhgTrend>;
  influences: ReadonlyMap<string, GhgInfluence>;
}

export const emptyModel: GhgModel = {
  trends: new Map(),
  influences: new Map(),
};

const TREND_TYPE = `${GHG_TYPE_PREFIX}${GhgElementTypes.trend}`;
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
    } else if (element.type === INFLUENCE_TYPE) {
      influences.set(id, { id, payload: fromBinary(GhgInfluencePayloadSchema, bytes) });
    }
  }

  return { trends, influences };
}

function removed(model: GhgModel, ids: readonly { value: string }[]): GhgModel {
  const trends = new Map(model.trends);
  const influences = new Map(model.influences);

  for (const id of ids) {
    trends.delete(id.value);
    influences.delete(id.value);
  }

  return { trends, influences };
}
