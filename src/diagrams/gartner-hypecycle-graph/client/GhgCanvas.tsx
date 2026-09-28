import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, EdgeAttachment, ElementTypeDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { RulerRung } from "@client/canvas/library/definition/chrome";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import type { GhgAttachment } from "@client/generated/gartner-hypecycle-graph_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { endPropertyPreview, settlePropertyPreview, showPropertyPreview } from "@client/shell/panels/propertyPreview";
import { viewReportOf } from "@client/diagrams/viewReport";
import { useViewReport } from "@client/diagrams/useViewReport";
import {
  GHG_ACTION_IDS,
  GHG_PHASES,
  GHG_PHASE_TITLES,
  GHG_PHASE_TOOLTIPS,
  GhgActions,
  GhgElementTypes,
  GhgProperties,
  GhgRelationTypes,
  GhgScale,
  GhgShortcuts,
  GhgTimeUnits,
  formatMonth,
  monthAt,
  timeUnitOf,
  type GhgTimeUnit,
} from "./ghgIds";
import { applyDelta, emptyModel } from "./ghgModel";
import { placementId, relationId } from "@client/canvas/gestureIds";

/**
 * The ruler's rungs, finest first, each with the months it spans. A diagram drawn in a coarser unit
 * keeps only the rungs at least one of its steps wide: a diagram of years never labels a month it
 * cannot snap to. A century and a millennium are ten and a hundred decades, which start on years
 * ending in 00 and 000.
 */
const RULER_RUNGS: readonly { months: number; rung: RulerRung }[] = [
  { months: 1, rung: { every: { calendar: "month" }, label: "MMM yyyy" } },
  { months: 3, rung: { every: { calendar: "quarter" }, label: "MMM yyyy" } },
  { months: 12, rung: { every: { calendar: "year" }, label: "yyyy" } },
  { months: 120, rung: { every: { calendar: "decade" }, label: "yyyy" } },
  { months: 1200, rung: { every: { calendar: "decade", count: 10 }, label: "yyyy" } },
  { months: 12000, rung: { every: { calendar: "decade", count: 100 }, label: "yyyy" } },
];

/**
 * A compact trend's width when it shows all four phases: twice that of a new trend dropped in
 * true-time, twenty-four steps of any unit, so its name and phases read at a glance (Peter,
 * 2026-09-27). A trend showing fewer phases takes that share of it - see {@link compactWidthOf}.
 */
export const COMPACT_WIDTH = 24 * GhgScale.unitsPerMonth;

/** A trend's compact width: {@link COMPACT_WIDTH} shared by the phases it shows, a quarter per phase. */
export function compactWidthOf(phases: number): number {
  return (COMPACT_WIDTH * Math.min(Math.max(phases, 1), GHG_PHASES.length)) / GHG_PHASES.length;
}

/**
 * A trigger: a moment in time, drawn as a circle half a trend's height across. Its name and its date
 * are written left of it as a trend's name is, and only the name is edited in place. It offers three
 * handles to start an influence from, and the line leaves its outline facing the target wherever the
 * gesture began, so the document stores nothing for that end. Never resized: every trigger is one size.
 */
const TRIGGER_TYPE: ElementTypeDefinition = {
  id: GhgElementTypes.trigger,
  shape: "ellipse",
  classNames: [
    { className: "canvas-element ghg-trigger", on: "element" },
    { className: "canvas-node ghg-trigger-circle", on: "shape" },
  ],
  labels: [{ text: { template: "{payload.name} · {payload.when}" }, placement: "before", editable: true, className: "canvas-node-label ghg-label" }],
  tooltip: { template: "Trigger: {payload.name}, {payload.whenLong}" },
  // No dots are drawn: the handles still start an influence, but a small circle ringed with dots
  // reads as a different shape (Peter, 2026-09-27).
  anchors: { kind: "compass", positions: ["n", "e", "s"], attachDrawnBy: "edge", visible: false },
  sizing: "model",
};

/**
 * A note: the author's own text in a box, word-wrapped and edited in place across several lines, with
 * no anchors, so no influence starts or ends at one. A note dropped from the toolbox opens its editor.
 */
const NOTE_TYPE: ElementTypeDefinition = {
  id: GhgElementTypes.note,
  shape: "box",
  classNames: [
    { className: "canvas-element ghg-note", on: "element" },
    { className: "canvas-node ghg-note-box", on: "shape" },
  ],
  labels: [{ text: { path: "payload.text" }, wrap: true, editable: true, className: "ghg-note-text" }],
  anchors: { kind: "edge", enabled: false, visible: false },
  sizing: "user",
  resize: "both",
  editOnDrop: true,
};

/**
 * What a hype cycle graph is, stated once for each time unit a document may name. Every piece of it
 * is a library declaration: the phased banner, the circle and the note, the attachments anywhere
 * along a phase's edge, one influence per direction, the ruler and the tag filter. The unit changes only the ruler's scale
 * and rungs; a snap is always one step of four units, whatever a step is. The backend states the
 * same rules in `GhgRuleSet` and refuses what the canvas refuses anyway, because a request is never
 * trusted to have come from this canvas.
 */
function definitionFor(unit: GhgTimeUnit): DiagramDefinition {
  const months = GhgTimeUnits[unit];
  const trendType: ElementTypeDefinition = {
    id: GhgElementTypes.trend,
    shape: "arrow-banner",
    classNames: [{ className: "canvas-element ghg-trend", on: "element" }],
    segments: {
      count: { path: "payload.phases" },
      max: GHG_PHASES.length,
      boundaries: "payload.boundaries",
      classNames: GHG_PHASES.map((phase) => `ghg-${phase}`),
      tooltips: GHG_PHASE_TOOLTIPS,
      divider: "chevron",
      draggableBoundaries: true,
    },
    labels: [{ text: { path: "payload.name" }, placement: "before", editable: true, className: "canvas-node-label ghg-label" }],
    // An influence attaches anywhere along a phase's top or bottom edge; no dot is drawn, because
    // the whole edge is the handle.
    anchors: { kind: "along", edges: ["top", "bottom"], regions: "segments", visible: false },
    sizing: "user",
  };
  // Compact: every trend one width, its phases even, and nothing that would change a date offered -
  // no move, no resize, no boundary drag - while influences, renaming and the toolbox still work.
  const compactTrendType: ElementTypeDefinition = {
    ...trendType,
    sizing: "model",
    draggable: false,
    segments: { ...trendType.segments!, boundaries: undefined, draggableBoundaries: false },
  };
  return assertValidDiagramDefinition({
    elementTypes: [trendType, TRIGGER_TYPE, NOTE_TYPE],
    relationTypes: [
      {
        id: GhgRelationTypes.influence,
        route: "cubic-bezier",
        style: { endMarker: "arrow" },
        className: "ghg-influence",
        // A selected influence shows a handle on each end, slid along its trend's edge to move it.
        movableEnds: true,
        hideWhenAttachmentHidden: true,
        // A trigger sets trends off and is never set off itself: it is a source, never a target.
        endpoints: {
          source: { elementTypes: [GhgElementTypes.trend, GhgElementTypes.trigger] },
          target: { elementTypes: [GhgElementTypes.trend] },
          allowSelf: false,
          cardinality: { perPair: "ordered" },
        },
      },
    ],
    actions: [
      {
        id: GhgActions.rename,
        invokedBy: [{ kind: "shortcut", key: GhgShortcuts.rename }, { kind: "gesture", gesture: "activate" }],
        appliesTo: [{ kind: "element" }],
      },
      { id: GhgActions.remove, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
      { id: GhgActions.disconnect, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "connection" }] },
    ],
    // A step of the unit is four units wide, so a snap of four lands every edge on the start of a
    // month, a year, a decade or a century; a row is 56, and the snap rests the trend's top on it,
    // which puts its middle on the row. The origin, 1900-01, starts all four. Each element carries
    // its own origins: 0 for a trend and a note, and for a trigger the offsets that put its CENTRE
    // on a step line and a row's middle.
    snap: {
      x: { step: GhgScale.unitsPerMonth, origin: { path: "payload.snapX" } },
      y: { step: GhgScale.rowStep, origin: { path: "payload.snapY" } },
    },
    chrome: {
      rulers: [
        {
          orientation: "horizontal",
          edge: "bottom",
          scale: { unit: "month", unitsPerStep: GhgScale.unitsPerMonth / months, origin: GhgScale.origin },
          ladder: RULER_RUNGS.filter((entry) => entry.months >= months).map((entry) => entry.rung),
          minSpacingPx: 64,
        },
      ],
    },
    filter: {
      field: "payload.tags",
      label: "Filter by tags",
      // Notes carry no tags and stay on the canvas under every filter.
      elementTypes: [GhgElementTypes.trend, GhgElementTypes.trigger],
      // The key to the phase colours, each swatch painted by the rule that paints its phase.
      legend: GHG_PHASES.map((phase, index) => ({ caption: GHG_PHASE_TITLES[index], swatchClass: `ghg-${phase}` })),
    },
    // True-time by default; the Compact toggle under the legend packs the trends along their rows
    // at one width, in the order they start, and takes away the time axis with the gestures.
    layout: {
      modes: ["manual", "row-packed"],
      toggle: { caption: "Compact", on: "row-packed" },
      // Only a trend takes the compact width: a trigger keeps its circle and a note its box, and a
      // note two rows tall keeps both rows clear, because an element covers every row line it spans.
      // Each trend's width is its share of the compact width, and an influence's target starts after
      // the middle of its source, so causes read to the left of their effects.
      rowPacked: {
        width: { path: "payload.compactWidth" },
        gap: GhgScale.unitsPerMonth,
        types: [GhgElementTypes.trend],
        rowStep: GhgScale.rowStep,
        followConnections: true,
      },
      modeOverrides: {
        "row-packed": {
          // Nothing that would change a date is offered: no trigger is dragged, and a note is
          // neither dragged nor resized, while influences from a trigger are still drawn.
          elementTypes: [compactTrendType, { ...TRIGGER_TYPE, draggable: false }, { ...NOTE_TYPE, sizing: "model", draggable: false }],
          dragging: "disabled",
          chrome: { rulers: [] },
        },
      },
    },
    dragging: "enabled",
  });
}

/** One definition per unit, built once, so a canvas's definition keeps its identity across renders. */
const DEFINITIONS: Readonly<Record<GhgTimeUnit, DiagramDefinition>> = {
  month: definitionFor("month"),
  year: definitionFor("year"),
  decade: definitionFor("decade"),
  century: definitionFor("century"),
};

/** The definition for a diagram drawn in `unit`. */
export function ghgDefinitionFor(unit: GhgTimeUnit): DiagramDefinition {
  return DEFINITIONS[unit];
}

/** The definition for a diagram drawn in months - every document that names no unit. */
export const GHG_DEFINITION: DiagramDefinition = DEFINITIONS.month;

/** A view of the whole canvas, far beyond any date a document can state: what compact reports. */
const EVERYTHING: ShapeBounds = { x: -1e9, y: -1e9, width: 2e9, height: 2e9 };

/** The declared actions this module forwards; anything else the library raises is not ours. */
const FORWARDED_ACTIONS: ReadonlySet<string> = new Set(GHG_ACTION_IDS);

/**
 * What a toolbox drop adds: the backend's toolbox drops its add action, and a toolbox derived from
 * the definition drops the element type, so both name the same add.
 */
const DROPPED_ACTIONS: ReadonlyMap<string, string> = new Map([
  [GhgActions.addTrend, GhgActions.addTrend],
  [GhgElementTypes.trend, GhgActions.addTrend],
  [GhgActions.addTrigger, GhgActions.addTrigger],
  [GhgElementTypes.trigger, GhgActions.addTrigger],
  [GhgActions.addNote, GhgActions.addNote],
  [GhgElementTypes.note, GhgActions.addNote],
]);

/** A size as the backend reads one: whole units, or two decimals at most. */
function round(value: number): string {
  return String(Math.round(value * 100) / 100);
}

/** The wire attachment as the library reads one, or nothing for an end the document could not state. */
function attachmentOf(attachment: GhgAttachment | undefined): EdgeAttachment | undefined {
  return attachment === undefined || (attachment.edge !== "top" && attachment.edge !== "bottom")
    ? undefined
    : { edge: attachment.edge, region: attachment.region, at: attachment.at };
}

/** An attachment as the document writes one: `phase/edge/at`, such as `plateau/bottom/0.3`. */
function endText(attachment: EdgeAttachment): string {
  const phase = GHG_PHASES[attachment.region ?? 0] ?? GHG_PHASES[0];
  return `${phase}/${attachment.edge}/${Math.round(attachment.at * 100) / 100}`;
}

/** One end of a connect gesture: the trend id, and where on it the influence attaches. */
function gestureEnd(elementId: string, attachment: EdgeAttachment | undefined): string {
  return attachment === undefined ? elementId : `${elementId}@${endText(attachment)}`;
}


/**
 * A Gartner hype cycle graph: trends on a time axis, each an arrow banner in the phases it has
 * reached, joined by influences. Every library event takes the one route that can carry it: a move
 * through the stream's `moveElementTo`, a resize or a boundary drag through `setProperty`, and
 * everything else through a context action on one target id.
 */
export function GhgCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { watchId, executeAction, setProperty } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);
  // Compact places every trend by where all the others start, so it needs the whole document: while
  // it is on, the view reported to the backend is everything, not the part of the canvas on screen.
  const [compact, setCompact] = useState(false);
  // Every trend carries the diagram's unit; an empty diagram is drawn in months until it has one.
  const unit = timeUnitOf(model.trends.values().next().value?.payload.unit);
  const dateAt = (x: number) => formatMonth(monthAt(x, unit));

  const diagramModel = useMemo<DiagramModel>(() => {
    const trends = [...model.trends.values()].map((trend): DiagramModelElement => ({
      id: trend.id,
      type: GhgElementTypes.trend,
      x: trend.x,
      y: trend.y,
      width: trend.payload.width,
      height: GhgScale.trendHeight,
      label: trend.payload.name,
      payload: {
        name: trend.payload.name,
        phases: trend.payload.phases,
        boundaries: [...trend.payload.boundaries],
        tags: [...trend.payload.tags],
        snapX: trend.payload.snapX,
        snapY: trend.payload.snapY,
        compactWidth: compactWidthOf(trend.payload.phases),
      },
    }));
    const triggers = [...model.triggers.values()].map((trigger): DiagramModelElement => ({
      id: trigger.id,
      type: GhgElementTypes.trigger,
      x: trigger.x,
      y: trigger.y,
      width: GhgScale.triggerSize,
      height: GhgScale.triggerSize,
      // The label is the name alone: what the inline editor opens with, never the date beside it.
      label: trigger.payload.name,
      payload: {
        name: trigger.payload.name,
        when: trigger.payload.when,
        whenLong: trigger.payload.whenLong,
        tags: [...trigger.payload.tags],
        snapX: trigger.payload.snapX,
        snapY: trigger.payload.snapY,
      },
    }));
    const notes = [...model.notes.values()].map((note): DiagramModelElement => ({
      id: note.id,
      type: GhgElementTypes.note,
      x: note.x,
      y: note.y,
      width: note.payload.width,
      height: note.payload.height,
      label: note.payload.text,
      payload: { text: note.payload.text, snapX: 0, snapY: 0 },
    }));
    const elements = [...trends, ...triggers, ...notes];
    const sources = (id: string) => model.trends.has(id) || model.triggers.has(id);
    const connections = [...model.influences.values()].flatMap((influence): DiagramModelConnection[] =>
      sources(influence.payload.fromElementId) && model.trends.has(influence.payload.toElementId)
        ? [{
            id: influence.id,
            type: GhgRelationTypes.influence,
            sourceId: influence.payload.fromElementId,
            targetId: influence.payload.toElementId,
            sourceAttachment: attachmentOf(influence.payload.sourceAttachment),
            targetAttachment: attachmentOf(influence.payload.targetAttachment),
          }]
        : [], // an end is not held - off screen; a line to nothing is worse than none
    );
    return { elements, connections };
  }, [model]);

  /** Half an element's drawn size, by what it is: the offset between its centre and its top-left. */
  const halfSizeOf = (elementId: string): { width: number; height: number } | undefined => {
    const trend = model.trends.get(elementId);
    if (trend !== undefined) {
      return { width: trend.payload.width / 2, height: GhgScale.trendHeight / 2 };
    }
    if (model.triggers.has(elementId)) {
      return { width: GhgScale.triggerSize / 2, height: GhgScale.triggerSize / 2 };
    }
    const note = model.notes.get(elementId);
    return note === undefined ? undefined : { width: note.payload.width / 2, height: note.payload.height / 2 };
  };

  // Every route below reports its own refusal to the one line the library draws around the canvas.
  const runAction = (actionId: string, targetId: string) => {
    void executeAction(actionId, elementSourceOf(targetId));
  };

  const runProperty = (propertyId: string, value: string, targetId: string) => {
    void setProperty(propertyId, value, elementSourceOf(targetId));
  };

  const events: DiagramEventHandlers = {
    // Rename, delete and disconnect, as the library dispatched them from their declarations.
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId !== undefined && FORWARDED_ACTIONS.has(actionId)) {
        runAction(actionId, targetId);
      }
    },
    // Where a trend is drawn mid-gesture, shown in the property grid as the dates the release would
    // write - Start, Stop and each phase's end - and nothing written until then.
    onElementPreviewed: ({ elementId, bounds, boundaries }) => {
      if (bounds === null) {
        endPropertyPreview();
        return;
      }
      const values: Record<string, string> = {
        [GhgProperties.start]: dateAt(bounds.x),
        [GhgProperties.stop]: dateAt(bounds.x + bounds.width),
      };
      (boundaries ?? []).forEach((x, index) => {
        const property = GhgProperties.boundaries[index];
        if (property !== undefined) {
          values[property] = dateAt(x);
        }
      });
      showPropertyPreview(elementId, values);
    },
    // The library reports the CENTRE it drew an element at; the backend places by the top-left, so
    // each element's own half-size comes off.
    onElementMoved: ({ elementId, position }) => {
      const half = halfSizeOf(elementId);
      if (half !== undefined) {
        settlePropertyPreview();
        void moveElementTo(elementId, position.x - half.width, position.y - half.height);
      }
    },
    // A trend's resize is a span: the dragged edge's month is the new start or stop. A note's is its
    // size, with the top-left it now has, which a drag of the left or top border moves.
    onElementResized: ({ elementId, side, bounds }) => {
      settlePropertyPreview();
      if (model.notes.has(elementId)) {
        runProperty(GhgProperties.size, `${round(bounds.width)} x ${round(bounds.height)} at ${dateAt(bounds.x)} row ${Math.round(bounds.y / GhgScale.rowStep)}`, elementId);
      } else if (side === "left") {
        runProperty(GhgProperties.start, dateAt(bounds.x), elementId);
      } else if (side === "right") {
        runProperty(GhgProperties.stop, dateAt(bounds.x + bounds.width), elementId);
      }
    },
    // A dragged boundary is the month its phase now ends at.
    onSegmentBoundaryMoved: ({ elementId, index, x }) => {
      const property = GhgProperties.boundaries[index];
      if (property !== undefined) {
        settlePropertyPreview();
        runProperty(property, dateAt(x), elementId);
      }
    },
    // A selected influence's end, slid along its trend's edge, possibly into another phase.
    onConnectionEndMoved: ({ connectionId, end, attachment }) => {
      runProperty(end === "source" ? GhgProperties.fromAttachment : GhgProperties.toAttachment, endText(attachment), connectionId);
    },
    // The whole gesture in one call: both ends, and where on each the influence attaches.
    onConnectionDrawn: ({ sourceElementId, targetElementId, sourceAttachment, targetAttachment }) => {
      runAction(
        GhgActions.connect,
        relationId(gestureEnd(sourceElementId, sourceAttachment), gestureEnd(targetElementId, targetAttachment)),
      );
    },
    // The backend's toolbox drops its add action; a toolbox derived from the definition drops the type.
    onElementDropped: ({ elementType, position }) => {
      const action = DROPPED_ACTIONS.get(elementType);
      if (action !== undefined) {
        runAction(action, placementId(position.x, position.y));
      }
    },
    onViewChanged: ({ viewport: next }) => setViewport(next),
    onLayoutModeChanged: ({ mode }) => setCompact(mode === "row-packed"),
  };

  const reported = compact ? EVERYTHING : viewport;

  useViewReport({
    view: { x: reported?.x ?? 0, y: reported?.y ?? 0, w: reported?.width ?? 0, h: reported?.height ?? 0 },
    report: viewReportOf(client, projectId, watchId, path),
    convert: () => ({
      minX: reported?.x ?? 0,
      minY: reported?.y ?? 0,
      maxX: (reported?.x ?? 0) + (reported?.width ?? 0),
      maxY: (reported?.y ?? 0) + (reported?.height ?? 0),
    }),
    ready: !loading && !failed && viewport !== null,
  });

  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas.
  return (
    <div className="ghg-canvas canvas-host" role="application" aria-label="Gartner hype cycle graph">
      <DiagramCanvas
        definition={ghgDefinitionFor(unit)}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Gartner hype cycle graph"
        className="ghg-surface"
      />
    </div>
  );
}
