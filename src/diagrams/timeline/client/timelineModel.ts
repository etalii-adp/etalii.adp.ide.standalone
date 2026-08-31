import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import { TimelineConnectionPayloadSchema, TimelineElementPayloadSchema } from "@client/generated/timeline_pb";

/** The mime-style element types the backend sends, all under `generic/timeline`. */
export const PERIOD = "generic/timeline+period";
export const MOMENT = "generic/timeline+moment";
export const CONNECTION = "generic/timeline+connection";

/**
 * A period or a moment, positioned in the module's own coordinate space: x is seconds since the
 * epoch, y is row × row height. The canvas layers its zoom on top; nothing here is a pixel.
 */
export interface TimelineElement {
  id: string;
  x: number;
  y: number;
  label: string;
  /** ISO 8601, exactly as the document says - what the grid shows and edits. */
  begin: string;
  /** Empty for a moment. */
  end: string;
  row: number;
  dateOnly: boolean;
  isPeriod: boolean;
}

export interface TimelineConnection {
  id: string;
  fromElementId: string;
  toElementId: string;
  label: string;
}

export interface TimelineModel {
  elements: Map<string, TimelineElement>;
  connections: Map<string, TimelineConnection>;
}

export const emptyModel: TimelineModel = {
  elements: new Map(),
  connections: new Map(),
};

/**
 * Folds one delta into the model.
 *
 * `add` is an upsert keyed on id: an edit arrives as an add carrying the element in its new
 * state, never as a remove-then-add, which would momentarily drop the selection on the thing
 * being edited.
 */
export function applyDelta(model: TimelineModel, delta: Delta): TimelineModel {
  switch (delta.action.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    default:
      return model;
  }
}

function added(model: TimelineModel, elements: readonly Element[]): TimelineModel {
  const next: TimelineModel = {
    elements: new Map(model.elements),
    connections: new Map(model.connections),
  };

  for (const element of elements) {
    const id = element.id?.value;
    if (!id) {
      continue;
    }

    // The type check comes BEFORE the decode. Several modules share one delta stream, and a
    // foreign element decoded as ours throws and takes the whole delta with it - a sibling
    // module shipped exactly that bug before finding it.
    switch (element.type) {
      case PERIOD:
      case MOMENT: {
        const payload = fromBinary(TimelineElementPayloadSchema, element.payload?.value ?? new Uint8Array());
        next.elements.set(id, {
          id,
          x: element.position?.x ?? 0,
          y: element.position?.y ?? 0,
          label: payload.label,
          begin: payload.begin,
          end: payload.end,
          row: payload.row,
          dateOnly: payload.dateOnly,
          isPeriod: element.type === PERIOD,
        });
        break;
      }
      case CONNECTION: {
        const payload = fromBinary(TimelineConnectionPayloadSchema, element.payload?.value ?? new Uint8Array());
        next.connections.set(id, {
          id,
          fromElementId: payload.fromElementId,
          toElementId: payload.toElementId,
          label: payload.label,
        });
        break;
      }
      default:
        // Not ours; skipped, never decoded.
        break;
    }
  }

  return next;
}

function removed(model: TimelineModel, ids: readonly string[]): TimelineModel {
  const next: TimelineModel = {
    elements: new Map(model.elements),
    connections: new Map(model.connections),
  };

  for (const id of ids) {
    next.elements.delete(id);
    next.connections.delete(id);
  }

  return next;
}
