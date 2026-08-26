import { create, fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import type { Element } from "@client/generated/elements_pb";
import {
  WardleyAcceleratorPayloadSchema,
  WardleyAnnotationPayloadSchema,
  WardleyAttitudePayloadSchema,
  WardleyElementPayloadSchema,
  WardleyEvolutionAxisPayloadSchema,
  WardleyLinkPayloadSchema,
  WardleyNotePayloadSchema,
  type WardleyAttitudeKind,
  type WardleyDecorator,
  type WardleyElementKind,
} from "@client/generated/wardley-map_pb";

/** The mime-style element types the backend sends, all under `wardley/map`. */
const ELEMENT = "wardley/map+element";
const LINK = "wardley/map+link";
const NOTE = "wardley/map+note";
const ANNOTATION = "wardley/map+annotation";
const ACCELERATOR = "wardley/map+accelerator";
const ATTITUDE = "wardley/map+attitude";
const EVOLUTION_AXIS = "wardley/map+evolution-axis";

export interface WardleyPoint {
  x: number;
  y: number;
}

/** A component, anchor or submap, positioned where its author put it. */
export interface WardleyElement extends WardleyPoint {
  id: string;
  name: string;
  kind: WardleyElementKind;
  /** The document's own axis values, which the property grid shows and edits. */
  visibility: number;
  maturity: number;
  evolutionStage: string;
  decorators: WardleyDecorator[];
  inertia: boolean;
  /** Where the author nudged the label, in pixels - the format's own convention. */
  labelOffset?: WardleyPoint;
  urlAddress: string;
  /** Set when this is a pipeline child, whose visibility is its parent's. */
  pipelineParentId: string;
  /** Where it is heading, when the document says. Drawn as a second position joined to the first. */
  evolve?: { maturity: number; evolutionStage: string; overrideName: string };
}

export interface WardleyLink {
  id: string;
  sourceId: string;
  targetId: string;
  isFlow: boolean;
  context: string;
  /** The names as written, so a link whose endpoint does not resolve can still say which. */
  sourceName: string;
  targetName: string;
}

export interface WardleyNote extends WardleyPoint {
  id: string;
  text: string;
}

export interface WardleyAnnotation extends WardleyPoint {
  id: string;
  number: number;
  text: string;
  /** Every place it is pinned. Never empty, and often longer than one. */
  occurrences: WardleyPoint[];
}

export interface WardleyAccelerator extends WardleyPoint {
  id: string;
  name: string;
  isDeaccelerator: boolean;
}

export interface WardleyAttitude extends WardleyPoint {
  id: string;
  kind: WardleyAttitudeKind;
  opposite: WardleyPoint;
}

export interface WardleyEvolutionStage {
  label: string;
  start: number;
  end: number;
}

/**
 * The evolution axis, as the backend describes it.
 *
 * The client holds NO copy of the stage boundaries. They are not published in the DSL and are
 * derived from the reference renderer's own offsets; keeping one copy, backend-side, is what
 * stops the two drifting into disagreement (Requirement 8.2).
 */
export interface WardleyAxis {
  stages: WardleyEvolutionStage[];
  title: string;
  width: number;
  height: number;
}

export interface WardleyModel {
  elements: Map<string, WardleyElement>;
  links: Map<string, WardleyLink>;
  notes: Map<string, WardleyNote>;
  annotations: Map<string, WardleyAnnotation>;
  accelerators: Map<string, WardleyAccelerator>;
  attitudes: Map<string, WardleyAttitude>;
  axis?: WardleyAxis;
}

export const emptyModel: WardleyModel = {
  elements: new Map(),
  links: new Map(),
  notes: new Map(),
  annotations: new Map(),
  accelerators: new Map(),
  attitudes: new Map(),
};

/**
 * Folds one delta into the model.
 *
 * `add` is an UPSERT keyed on element id: an edit arrives as an add carrying the element in its
 * new state, never as a remove followed by an add, which would make it momentarily absent and
 * drop the selection on the thing being edited (Requirement 10.3).
 */
export function applyDelta(model: WardleyModel, delta: Delta): WardleyModel {
  switch (delta.action.case) {
    case "add":
      return added(model, delta.action.value.elements);
    case "remove":
      return removed(model, delta.action.value.elementIds);
    // Group and ungroup carry a pipeline's membership. The canvas draws a pipeline from its
    // children's own pipelineParentId, so there is nothing further to fold here.
    default:
      return model;
  }
}

function added(model: WardleyModel, elements: readonly Element[]): WardleyModel {
  const next: WardleyModel = {
    elements: new Map(model.elements),
    links: new Map(model.links),
    notes: new Map(model.notes),
    annotations: new Map(model.annotations),
    accelerators: new Map(model.accelerators),
    attitudes: new Map(model.attitudes),
    axis: model.axis,
  };

  for (const element of elements) {
    // Ids are wrapped on the wire; an element without one cannot be addressed at all.
    const id = element.id?.value;
    if (!id) {
      continue;
    }

    const bytes = element.payload?.value ?? new Uint8Array();
    const x = element.position?.x ?? 0;
    const y = element.position?.y ?? 0;

    switch (element.type) {
      case ELEMENT: {
        const payload = fromBinary(WardleyElementPayloadSchema, bytes);
        next.elements.set(id, {
          id,
          x,
          y,
          name: payload.name,
          kind: payload.kind,
          visibility: payload.visibility,
          maturity: payload.maturity,
          evolutionStage: payload.evolutionStage,
          decorators: [...payload.decorators],
          inertia: payload.inertia,
          labelOffset: payload.labelOffset
            ? { x: payload.labelOffset.x, y: payload.labelOffset.y }
            : undefined,
          urlAddress: payload.urlAddress,
          pipelineParentId: payload.pipelineParentId,
          evolve: payload.evolve
            ? {
                maturity: payload.evolve.maturity,
                evolutionStage: payload.evolve.evolutionStage,
                overrideName: payload.evolve.overrideName,
              }
            : undefined,
        });
        break;
      }
      case LINK: {
        const payload = fromBinary(WardleyLinkPayloadSchema, bytes);
        next.links.set(id, {
          id,
          sourceId: payload.sourceId,
          targetId: payload.targetId,
          isFlow: payload.isFlow,
          context: payload.context,
          sourceName: payload.sourceName,
          targetName: payload.targetName,
        });
        break;
      }
      case NOTE: {
        const payload = fromBinary(WardleyNotePayloadSchema, bytes);
        next.notes.set(id, { id, x, y, text: payload.text });
        break;
      }
      case ANNOTATION: {
        const payload = fromBinary(WardleyAnnotationPayloadSchema, bytes);
        next.annotations.set(id, {
          id,
          x,
          y,
          number: payload.number,
          text: payload.text,
          occurrences: payload.occurrences.map((point) => ({ x: point.x, y: point.y })),
        });
        break;
      }
      case ACCELERATOR: {
        const payload = fromBinary(WardleyAcceleratorPayloadSchema, bytes);
        next.accelerators.set(id, {
          id,
          x,
          y,
          name: payload.name,
          isDeaccelerator: payload.isDeaccelerator,
        });
        break;
      }
      case ATTITUDE: {
        const payload = fromBinary(WardleyAttitudePayloadSchema, bytes);
        next.attitudes.set(id, {
          id,
          x,
          y,
          kind: payload.kind,
          opposite: { x: payload.opposite?.x ?? 0, y: payload.opposite?.y ?? 0 },
        });
        break;
      }
      case EVOLUTION_AXIS: {
        const payload = fromBinary(WardleyEvolutionAxisPayloadSchema, bytes);
        next.axis = {
          stages: payload.stages.map((stage) => ({
            label: stage.label,
            start: stage.start,
            end: stage.end,
          })),
          title: payload.title,
          width: payload.width,
          height: payload.height,
        };
        break;
      }
      default:
        // An element type this build does not know is ignored rather than fatal: a newer
        // backend must not break an older canvas.
        break;
    }
  }

  return next;
}

function removed(model: WardleyModel, ids: readonly { value: string }[]): WardleyModel {
  const next: WardleyModel = {
    elements: new Map(model.elements),
    links: new Map(model.links),
    notes: new Map(model.notes),
    annotations: new Map(model.annotations),
    accelerators: new Map(model.accelerators),
    attitudes: new Map(model.attitudes),
    axis: model.axis,
  };

  for (const id of ids) {
    // One id may name any kind of element, and only one map will hold it.
    next.elements.delete(id.value);
    next.links.delete(id.value);
    next.notes.delete(id.value);
    next.annotations.delete(id.value);
    next.accelerators.delete(id.value);
    next.attitudes.delete(id.value);
  }

  return next;
}

/** Every element a pipeline holds, by its parent's id. */
export function pipelineChildrenOf(model: WardleyModel, parentId: string): WardleyElement[] {
  return [...model.elements.values()].filter((element) => element.pipelineParentId === parentId);
}

/** A placeholder axis for a map whose baseline has not arrived, so the canvas can draw something. */
export function createEmptyAxis(): WardleyAxis {
  return create(WardleyEvolutionAxisPayloadSchema, {}) as unknown as WardleyAxis;
}
