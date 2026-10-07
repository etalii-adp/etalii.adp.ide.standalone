import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, EdgeAttachment, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import type { GhgAttachment } from "@client/generated/gartner-hypecycle-graph_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { endPropertyPreview, settlePropertyPreview, showPropertyPreview } from "@client/shell/panels/propertyPreview";
import { viewReportOf } from "@client/diagrams/viewReport";
import { useViewReport } from "@client/diagrams/useViewReport";
import {
  GHG_ACTION_IDS,
  GHG_PHASES,
  GhgActions,
  GhgElementTypes,
  GhgProperties,
  GhgRelationTypes,
  GhgScale,
  formatMonth,
  monthAt,
  timeUnitOf,
  type GhgTimeUnit,
} from "./ghgIds";
import { applyDelta, emptyModel } from "./ghgModel";
import { placementId, relationId } from "@client/canvas/gestureIds";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import disText from "../definition/gartner-hype-cycle-graph.dis?raw";
import { GHG_BINDINGS } from "./ghgBindings";

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

/** The bundled specification: the same bytes the backend loads, read by Vite as text. */
const SPEC = parseDisl(disText);

/**
 * What a hype cycle graph is, stated once for each time unit a document may name - compiled from the
 * bundled DISL specification (`definition/gartner-hype-cycle-graph.dis`), with the few things the
 * library cannot read from it in `ghgBindings.ts`. The unit is the diagram attribute the axis, the
 * snap and the ruler bind, so it is the one option the compilation takes. The backend states the
 * same rules and refuses what the canvas refuses anyway, because a request is never trusted to have
 * come from this canvas; `ghgCompiledDefinition.test.ts` holds this to the definition once written
 * here by hand.
 */
function definitionFor(unit: GhgTimeUnit): DiagramDefinition {
  return assertValidDiagramDefinition(compileNotation(SPEC, GHG_BINDINGS, { diagram: { unit } }));
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
export function GhgCanvas({ projectId, entryId, path }: ToolContentProps) {
  const { watchId, executeAction, setProperty } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);
  // Compact places every trend by where all the others start, so it needs the whole document: while
  // it is on, the view reported to the backend is everything, not the part of the canvas on screen.
  const [compact, setCompact] = useState(false);
  // Every trend, trigger and note carries the diagram's unit, so a diagram of triggers or notes alone
  // is drawn in it too; an empty diagram is drawn in months until it has one.
  const carrier = model.trends.values().next().value ?? model.triggers.values().next().value ?? model.notes.values().next().value;
  const unit = timeUnitOf(carrier?.payload.unit);
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
    void executeAction(actionId, elementSourceOf(targetId, entryId));
  };

  const runProperty = (propertyId: string, value: string, targetId: string) => {
    void setProperty(propertyId, value, elementSourceOf(targetId, entryId));
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
