import { useMemo, useRef, useState } from "react";
import { forwardBezierPath, horizontalBezierPath } from "@client/canvas/connectors";
import { SpanElement, type SpanElementClasses } from "@client/canvas/elements/span/SpanElement";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { innermostKey, useContextConnection, useContextPrompt, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import type { DiagramCanvasProps as ShellCanvasProps } from "@client/shell/panels/diagramCanvas";
import { ContextSelectionAction, type ContextShortcut } from "@client/generated/context_pb";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import type { CustomShapeRef, DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import type { DiagramEventHandlers, DiagramSelection, DiagramViewport } from "@client/canvas/library/api/diagramEvents";
import { TimelineRuler } from "./TimelineRuler";
import { useTimelineStream } from "./useTimelineStream";
import type { TimelineElement, TimelineModel } from "./timelineModel";

/**
 * The vertical distance between adjacent rows, in the module's own y units. Mirrors
 * `TimelineRows.Height` in the backend and must stay equal to it.
 */
const ROW_HEIGHT = 60;

/** How tall an element's box is drawn, leaving a gutter between rows. */
const ELEMENT_HEIGHT = 36;

/** A moment's marker radius. */
const MOMENT_RADIUS = 9;

const DAY = 86400;

/** What the surface is assumed to be before it is measured - jsdom, or the first frame. */
const FALLBACK_WIDTH_PX = 1200;

/**
 * The seconds-to-canvas-units mapping, frozen when the first non-empty model lands.
 *
 * The time axis cannot live in the viewBox itself: seconds are ~10^9 while rows are ~10^2,
 * and a uniform svg would draw one of them invisibly. So the module normalizes once - the
 * initial fit's span across ~1200 units, exactly the pixels the old canvas would have used -
 * and the library's uniform zoom and pan take over from there. Frozen deliberately: deriving
 * it from the current model would re-normalize on every edit and shift the whole drawing
 * under the user. This is the axis mechanism the schema's background/extent could not carry,
 * recorded in the tasks document per Requirement 9.4.
 */
export interface TimelineScale {
  originSeconds: number;
  secondsPerUnit: number;
  /** The module y (row × height) drawn at canvas y 0. */
  originY: number;
}

/** The scale the first fit would choose for this model - exported so tests can compute positions. */
export function timelineScaleOf(model: TimelineModel): TimelineScale {
  const elements = [...model.elements.values()];
  if (elements.length === 0) {
    return { originSeconds: 0, secondsPerUnit: DAY / 20, originY: -ROW_HEIGHT };
  }

  const begins = elements.map((element) => element.x);
  const ends = elements.map((element) => endSecondsOf(element));
  const min = Math.min(...begins);
  const span = Math.max(Math.max(...ends) - min, DAY);
  return {
    originSeconds: min - span * 0.1,
    secondsPerUnit: (span * 1.2) / FALLBACK_WIDTH_PX,
    originY: Math.min(...elements.map((element) => element.y)) - ROW_HEIGHT,
  };
}

export function endSecondsOf(element: TimelineElement): number {
  if (!element.isPeriod) {
    return element.x;
  }

  const parsed = Date.parse(element.end.includes("T") ? `${element.end}Z` : `${element.end}T00:00:00Z`) / 1000;
  return Number.isFinite(parsed) ? parsed : element.x;
}

/** The nearest row for a module-space y, matching TimelineRows.ToNearestRow's away-from-zero midpoint. */
function nearestRow(y: number): number {
  const exact = y / ROW_HEIGHT;
  return exact >= 0 ? Math.floor(exact + 0.5) : -Math.floor(-exact + 0.5);
}

function formatSeconds(seconds: number, dateOnly: boolean): string {
  const date = new Date(Math.round(seconds) * 1000);
  const pad = (value: number) => String(value).padStart(2, "0");
  const day = `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
  return dateOnly ? day : `${day}T${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:${pad(date.getUTCSeconds())}`;
}

/** The placement id a gesture carries when it lands on empty canvas: `new:{seconds},{row}`. */
function newPlacementId(seconds: number, row: number): string {
  return `new:${seconds},${row}`;
}

/** The class names the shared span element hangs the timeline's styling on. */
const SPAN_CLASSES: SpanElementClasses = {
  span: "timeline-period canvas-node",
  moment: "timeline-moment",
  label: "timeline-label canvas-node-label",
  hint: "timeline-hint canvas-hint",
  adorner: "timeline-adorner",
  anchor: "timeline-anchor canvas-anchor",
  anchorHit: "timeline-anchor-hit canvas-anchor-hit",
};

/** An element as the library carries it here: the model element plus what draws it. */
type SpanModelElement = DiagramModelElement & { source: TimelineElement; scale: TimelineScale };

/**
 * The period-or-moment as a custom shape: the shared SpanElement draws the box or diamond
 * and the label that trims itself, while the library owns selection, anchors, resizing and
 * every gesture - so the span's own selection furniture stays off (`selected={false}`), and
 * the drag hint is computed from the position the library has already carried it to.
 */
const spanShape: CustomShapeRef = {
  customShape: "timeline-span",
  render: (raw, state) => {
    const element = raw as SpanModelElement;
    const { source, scale } = element;
    const width = element.width ?? 1;
    const hint = state?.dragging === true
      ? `${formatSeconds(scale.originSeconds + (element.x - width / 2) * scale.secondsPerUnit, source.dateOnly)} · row ${nearestRow(element.y - ELEMENT_HEIGHT / 2 + scale.originY)}`
      : null;

    return (
      <SpanElement
        box={{ x: element.x, y: element.y, width, height: element.height ?? ELEMENT_HEIGHT }}
        moment={!source.isPeriod}
        label={source.label || source.id}
        hint={hint}
        selected={false}
        pointRadius={MOMENT_RADIUS}
        classes={SPAN_CLASSES}
      />
    );
  },
  edgePoint: (bounds, towards) => ({
    x: towards.x >= bounds.x + bounds.width / 2 ? bounds.x + bounds.width : bounds.x,
    y: bounds.y + bounds.height / 2,
  }),
};

/**
 * What a timeline allows, stated once: periods that drag and resize, moments that drag,
 * either connecting to either from its begin or end anchor, with one bezier relation whose
 * empty release is itself a gesture - the create-and-relate the notation offers.
 */
const TIMELINE_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "period",
      shape: spanShape,
      anchors: {
        kind: "sides",
        fractions: [
          { side: "left", at: 0.5, name: "begin" },
          { side: "right", at: 0.5, name: "end" },
        ],
      },
      sizing: "user",
      label: { placement: "inside", editable: true },
    },
    {
      id: "moment",
      shape: spanShape,
      anchors: {
        kind: "sides",
        fractions: [
          { side: "left", at: 0.5, name: "begin" },
          { side: "right", at: 0.5, name: "end" },
        ],
      },
      sizing: "model",
      label: { placement: "beside", editable: true },
    },
  ],
  relationTypes: [
    {
      id: "gates",
      // The loop is decided by geometry: a target beginning before the source ends gets the
      // forward-and-back curve, exactly as the interactive bezier drew it.
      route: {
        customRoute: "timeline-bezier",
        path: (from, to) => (to.x < from.x ? forwardBezierPath(from, to) : horizontalBezierPath(from, to)),
      },
      label: { placement: "midpoint", offset: -6, editable: true },
      className: "timeline-connection",
      lineClassName: "timeline-connection-line",
      hitClassName: "timeline-connection-hit",
      endpoints: {
        source: { elementTypes: ["period", "moment"] },
        target: { elementTypes: ["period", "moment"], anchors: "edge" },
        allowSelf: false,
      },
      emptyRelease: "complete",
    },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

/**
 * The timeline, drawn through the diagram library: the axis-shaped reference canvas
 * (diagram-library Requirement 9.3). The time axis lives in the module's frozen
 * seconds-to-units scale; the ruler renders beside the canvas from the library's one
 * view-changed signal; and every gesture - drag, resize, connect from either anchor,
 * create-and-relate on an empty release, toolbox drops as placements - answers through the
 * module's own transports, one command each.
 */
export function TimelineCanvas({ projectId, entryId, path }: ShellCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useTimelineStream(projectId, path);
  const { select, executeAction, executeShortcut, setProperty } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState("");
  const [viewport, setViewport] = useState<DiagramViewport | null>(null);

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  // Frozen when the first non-empty model lands, exactly as the old canvas fitted once.
  const scaleRef = useRef<TimelineScale | null>(null);
  if (scaleRef.current === null && model.elements.size > 0) {
    scaleRef.current = timelineScaleOf(model);
  }
  const scale = scaleRef.current ?? timelineScaleOf(model);

  const toUnitsX = (seconds: number) => (seconds - scale.originSeconds) / scale.secondsPerUnit;
  const toSeconds = (units: number) => scale.originSeconds + units * scale.secondsPerUnit;
  const toUnitsY = (moduleY: number) => moduleY - scale.originY;
  const toModuleY = (units: number) => units + scale.originY;

  const diagramModel = useMemo<DiagramModel>(() => {
    const elements = [...model.elements.values()].map((element): SpanModelElement => {
      const width = element.isPeriod
        ? Math.max((endSecondsOf(element) - element.x) / scale.secondsPerUnit, 2)
        : MOMENT_RADIUS * 2;
      return {
        id: element.id,
        type: element.isPeriod ? "period" : "moment",
        x: toUnitsX(element.x) + width / 2,
        y: toUnitsY(element.y) + ELEMENT_HEIGHT / 2,
        width,
        height: ELEMENT_HEIGHT,
        label: element.label,
        source: element,
        scale,
      };
    });
    const connections = [...model.connections.values()].map((connection) => ({
      id: connection.id,
      type: "gates",
      sourceId: connection.fromElementId,
      targetId: connection.toElementId,
      // Fixed sides, deliberately: a timeline reads left to right, so a relation always
      // leaves its source's end and arrives at its target's begin - and when the target
      // starts earlier, those two points are what the loop-back curve loops between.
      sourceAnchor: "end",
      targetAnchor: "begin",
      label: connection.label,
    }));
    return { elements, connections };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the scale is frozen; only the model varies.
  }, [model, scale]);

  const librarySelection = useMemo<DiagramSelection>(() => {
    if (selectedId === null) {
      return [];
    }
    return [{ kind: model.connections.has(selectedId) ? "connection" : "element", id: selectedId }];
  }, [selectedId, model.connections]);

  const runAction = (actionId: string, sourceId?: string) => {
    void (async () => {
      const outcome = await executeAction(actionId, sourceId ? elementSourceOf(sourceId) : undefined);
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const runShortcut = (shortcut: ContextShortcut, sourceId: string) => {
    void (async () => {
      const outcome = await executeShortcut(shortcut, elementSourceOf(sourceId));
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  /** A drag from the begin anchor arrives reversed: what precedes an element points into it. */
  const relationOf = (sourceId: string, sourceAnchor: string | undefined, landing: string) =>
    sourceAnchor === "begin" ? `rel:${landing}->${sourceId}` : `rel:${sourceId}->${landing}`;

  const events: DiagramEventHandlers = {
    onSelectionChanged: ({ selection: next }) =>
      select(next.length > 0 ? elementSelectionOf(entryId, path, next[0].id) : null),
    onElementMoved: ({ elementId, position }) => {
      const element = model.elements.get(elementId);
      if (element === undefined) {
        return;
      }
      setRejection("");
      const width = element.isPeriod ? Math.max((endSecondsOf(element) - element.x) / scale.secondsPerUnit, 2) : MOMENT_RADIUS * 2;
      const beginSeconds = toSeconds(position.x - width / 2);
      const row = nearestRow(toModuleY(position.y - ELEMENT_HEIGHT / 2));
      void (async () => {
        const error = await moveElementTo(elementId, beginSeconds, row * ROW_HEIGHT);
        if (error) {
          setRejection(error);
        }
      })();
    },
    onElementResized: ({ elementId, side, bounds }) => {
      const element = model.elements.get(elementId);
      if (element === undefined || !element.isPeriod) {
        return;
      }
      setRejection("");
      // The moving edge stops at the other rather than crossing it; the handler clamps again
      // server-side, this copy is what the user feels.
      let edgeSeconds = toSeconds(side === "left" ? bounds.x : bounds.x + bounds.width);
      const limit = side === "left" ? endSecondsOf(element) : element.x;
      edgeSeconds = side === "left" ? Math.min(edgeSeconds, limit) : Math.max(edgeSeconds, limit);
      const property = side === "left" ? "timeline.begin" : "timeline.end";
      void (async () => {
        const outcome = await setProperty(property, formatSeconds(edgeSeconds, element.dateOnly));
        if (!outcome.accepted) {
          setRejection(outcome.error);
        }
      })();
    },
    // One stateless call carries the whole gesture - the payload the context channel cannot
    // (Requirement 7.3). Direction follows the anchor the drag lifted from.
    onConnectionDrawn: ({ sourceElementId, sourceAnchor, targetElementId }) =>
      runAction("timeline.connect", relationOf(sourceElementId, sourceAnchor, targetElementId)),
    onConnectionReleasedOnEmpty: ({ sourceElementId, sourceAnchor, position }) => {
      // Released on nothing: a create-and-relate placement at the dropped time and row.
      const landing = newPlacementId(toSeconds(position.x), nearestRow(toModuleY(position.y)));
      runAction("timeline.connect", relationOf(sourceElementId, sourceAnchor, landing));
    },
    onElementDropped: ({ elementType, position }) =>
      runAction(elementType, newPlacementId(toSeconds(position.x), nearestRow(toModuleY(position.y)))),
    onElementDeleted: ({ elementId }) => runShortcut(deleteShortcut(), elementId),
    onConnectionDeleted: ({ connectionId }) => runShortcut(deleteShortcut(), connectionId),
    onViewChanged: ({ viewport: next }) => setViewport(next),
  };

  // What the reader can see, in the module's own units - seconds across, row-derived y down -
  // reported once it settles, observing the library's one view-changed signal.
  useViewReport({
    view: { x: viewport?.x ?? 0, y: viewport?.y ?? 0, w: viewport?.width ?? 0, h: viewport?.height ?? 0 },
    report: reportView,
    convert: () => ({
      minX: toSeconds(viewport?.x ?? 0),
      minY: toModuleY(viewport?.y ?? 0),
      maxX: toSeconds((viewport?.x ?? 0) + (viewport?.width ?? 0)),
      maxY: toModuleY((viewport?.y ?? 0) + (viewport?.height ?? 0)),
    }),
    ready: !loading && !failed && viewport !== null,
  });

  const { prompt, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel } = useContextPrompt();
  const editingId = inlineLabelElementIdOf(prompt);

  /**
   * Structural keys travel to the backend as data - the backend holds the key-to-action
   * table. Delete arrives through the library's deletion events above; the rest bubble here.
   */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!selectedId || isTextTarget(event.target)) {
      return;
    }

    const shortcut = structuralShortcutFor(event, ["F2", "Insert", "Tab", "Enter"]);
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    runShortcut(shortcut, selectedId);
  };

  if (failed) {
    return (
      <div className="timeline-canvas canvas-host timeline-canvas-message canvas-host-message">
        <p>This timeline could not be opened.</p>
      </div>
    );
  }

  // The view-fixed ruler, derived from the same view the report observes.
  const rulerStartSeconds = toSeconds(viewport?.x ?? 0);
  const rulerSecondsPerPixel = ((viewport?.width ?? FALLBACK_WIDTH_PX) * scale.secondsPerUnit) / FALLBACK_WIDTH_PX;

  /** The canvas owns the empty surface's right button; an item's right-click is the menu's. */
  const onContextMenu = (event: React.MouseEvent) => {
    if ((event.target as Element).closest("[data-element-id],[data-connection-id]") === null) {
      event.preventDefault();
    }
  };

  return (
    <div className="timeline-canvas canvas-host" role="application" aria-label="Timeline" onKeyDown={onKeyDown} onContextMenu={onContextMenu}>
      <DiagramCanvas
        definition={TIMELINE_DEFINITION}
        model={diagramModel}
        events={events}
        selection={librarySelection}
        toolboxItems={toolboxItems}
        context={{
          selectionKey: selectionKey ?? undefined,
          actions,
          selectForMenu: (id) => select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
          executeAction: (actionId) => runAction(actionId, selectedId ?? undefined),
        }}
        editing={{ editingId, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel }}
        ariaLabel="Timeline"
        className="timeline-surface canvas-viewport"
        scrollbarsClassName="timeline-scrollbars"
      />
      <TimelineRuler startSeconds={rulerStartSeconds} secondsPerPixel={rulerSecondsPerPixel} widthPx={FALLBACK_WIDTH_PX} />
      {loading ? <p className="timeline-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="timeline-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}

/** The Delete key as the backend shortcut it has always travelled as. */
function deleteShortcut(): ContextShortcut {
  return { key: "Delete", ctrl: false, shift: false, alt: false, meta: false } as ContextShortcut;
}
