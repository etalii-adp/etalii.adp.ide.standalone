import { useLayoutEffect, useMemo, useRef, useState } from "react";
import { elementSourceOf } from "@client/canvas/selection";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import type { ToolContentProps as ShellCanvasProps } from "@client/shell/panels/toolPanelRegistration";
import { DiagramCanvas, snapToStep } from "@client/canvas/library/DiagramCanvas";
import type { DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import type { DiagramEventHandlers, DiagramViewport } from "@client/canvas/library/api/diagramEvents";
import { TimelineRuler } from "./TimelineRuler";
import { useTimelineStream } from "./useTimelineStream";
import type { TimelineElement, TimelineModel } from "./timelineModel";
import { placementId, relationId } from "@client/canvas/gestureIds";
import disText from "../definition/timeline.dis?raw";
import { ELEMENT_HEIGHT, ROW_HEIGHT, TIMELINE_BINDINGS } from "./timelineBindings";

export { ELEMENT_HEIGHT, ROW_HEIGHT };

/** A moment's marker radius. */
export const MOMENT_RADIUS = 9;

const DAY = 86400;

/** What the surface is assumed to be before it is measured - jsdom, or the first frame. */
const FALLBACK_WIDTH_PX = 1200;

/**
 * The surface's width in real pixels, measured.
 *
 * The ruler converts the view's canvas units into pixel offsets for its labels, so it needs
 * the width the surface actually has - not the width the frozen scale was normalized against.
 * Passing the constant meant the ladder was laid out for a 1200px ruler whatever the pane's
 * real size, and the labels then disagreed with the elements they date: on a 469px surface a
 * milestone at 2026-01-06 drew 130px to the left of where the ruler put "2026".
 *
 * Measured on mount and kept current by a ResizeObserver where there is one; jsdom has neither
 * a layout nor an observer, so a zero measurement leaves {@link FALLBACK_WIDTH_PX} standing and
 * the existing tests keep saying exactly what they said.
 */
function useMeasuredWidth(ref: React.RefObject<HTMLElement | null>): number {
  const [widthPx, setWidthPx] = useState(FALLBACK_WIDTH_PX);

  useLayoutEffect(() => {
    const host = ref.current;
    if (host === null) {
      return;
    }

    const measure = () => {
      const measured = host.getBoundingClientRect().width;
      setWidthPx((current) => (measured > 0 ? measured : current));
    };

    measure();
    if (typeof ResizeObserver === "undefined") {
      return;
    }

    const observer = new ResizeObserver(measure);
    observer.observe(host);
    return () => observer.disconnect();
  }, [ref]);

  return widthPx;
}

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

/**
 * The nearest row for a module-space y, by the library's one rounding rule - halves away from
 * zero, never negative zero - which TimelineRows.ToNearestRow shares on the backend. A dragged
 * element's position already arrives snapped by the declaration, so for a move this only turns
 * an exact row back into its index.
 */
export function nearestRow(y: number): number {
  return snapToStep(y, ROW_HEIGHT) / ROW_HEIGHT;
}

function formatSeconds(seconds: number, dateOnly: boolean): string {
  const date = new Date(Math.round(seconds) * 1000);
  const pad = (value: number) => String(value).padStart(2, "0");
  const day = `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
  return dateOnly ? day : `${day}T${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:${pad(date.getUTCSeconds())}`;
}

/** The placement id a gesture carries when it lands on empty canvas: `new:{seconds},{row}`. */
function newPlacementId(seconds: number, row: number): string {
  return placementId(seconds, row);
}

/**
 * What a timeline allows, compiled from the bundled DISL specification (`definition/timeline.dis`) with
 * what the library cannot read from it in `timelineBindings.ts`: periods that drag and resize, moments
 * that drag, either connecting to either from its begin or end anchor, with one bezier relation whose
 * empty release is itself a gesture - the create-and-relate the notation offers - and the background
 * menu, whose placement this canvas converts into seconds and a row (`config` below).
 */
export const TIMELINE_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(parseDisl(disText), TIMELINE_BINDINGS));

/** The relation type the compiled definition draws a connection as. */
const CONNECTION = TIMELINE_DEFINITION.relationTypes[0]!.id;

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
  const { executeAction, setProperty } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<DiagramViewport | null>(null);
  const hostRef = useRef<HTMLDivElement | null>(null);
  const surfaceWidthPx = useMeasuredWidth(hostRef);

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
    const elements = [...model.elements.values()].map((element): DiagramModelElement => {
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
        // `label || id`, as the span drew it, and the scale the drag hint's arithmetic reads.
        label: element.label || element.id,
        payload: {
          secondsPerUnit: scale.secondsPerUnit,
          originSeconds: scale.originSeconds,
          // In ROWS rather than units: the hint scales the y first and then adds this, because
          // `times` runs before `plus` and a scale constant is the module's to convert.
          originRows: scale.originY / ROW_HEIGHT,
          // A date-only element's day lattice in canvas units: one day wide, with a line where
          // a day starts. Absent for an element with a time of day, which does not snap in x.
          ...(element.dateOnly ? { dayUnits: DAY / scale.secondsPerUnit, dayOriginUnits: -scale.originSeconds / scale.secondsPerUnit } : {}),
        },
      };
    });
    const connections = [...model.connections.values()].map((connection) => ({
      id: connection.id,
      type: CONNECTION,
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

  // A background right-click names its placement in the backend's terms - seconds and a row - as
  // a drop does, so "Add element here" lands where the reader clicked.
  const config = useMemo(
    () => ({ definitionOverrides: { backgroundPlacement: (point: { x: number; y: number }) => ({ x: toSeconds(point.x), y: nearestRow(toModuleY(point.y)) }) } }),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the scale is frozen; it is all the conversion reads.
    [scale],
  );

  const runAction = (actionId: string, sourceId?: string) => {
    // A refusal needs nothing here: the call reports it to the library's refusal line.
    void executeAction(actionId, sourceId ? elementSourceOf(sourceId, entryId) : undefined);
  };

  /** A drag from the begin anchor arrives reversed: what precedes an element points into it. */
  const relationOf = (sourceId: string, sourceAnchor: string | undefined, landing: string) =>
    sourceAnchor === "begin" ? relationId(landing, sourceId) : relationId(sourceId, landing);

  const events: DiagramEventHandlers = {
    // Selection is the library's (centralized-selection), and so is the refusal line: every call
    // here, and every menu action the library runs, reports its own refusal there
    // (client-centralization Requirement 2).
    onElementMoved: ({ elementId, position }) => {
      const element = model.elements.get(elementId);
      if (element === undefined) {
        return;
      }
      const width = element.isPeriod ? Math.max((endSecondsOf(element) - element.x) / scale.secondsPerUnit, 2) : MOMENT_RADIUS * 2;
      const beginSeconds = toSeconds(position.x - width / 2);
      const row = nearestRow(toModuleY(position.y - ELEMENT_HEIGHT / 2));
      void moveElementTo(elementId, beginSeconds, row * ROW_HEIGHT);
    },
    onElementResized: ({ elementId, side, bounds }) => {
      const element = model.elements.get(elementId);
      if (element === undefined || !element.isPeriod) {
        return;
      }
      // The moving edge stops at the other rather than crossing it; the handler clamps again
      // server-side, this copy is what the user feels.
      let edgeSeconds = toSeconds(side === "left" ? bounds.x : bounds.x + bounds.width);
      const limit = side === "left" ? endSecondsOf(element) : element.x;
      edgeSeconds = side === "left" ? Math.min(edgeSeconds, limit) : Math.max(edgeSeconds, limit);
      const property = side === "left" ? "timeline.begin" : "timeline.end";
      // A refusal needs nothing here: the call reports it to the library's refusal line.
      void setProperty(property, formatSeconds(edgeSeconds, element.dateOnly));
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

  // The view-fixed ruler, derived from the same view the report observes - and from the width
  // the surface really has, so a label sits over the elements it dates rather than over the
  // ones a 1200px-wide surface would have put there.
  const rulerStartSeconds = toSeconds(viewport?.x ?? 0);
  const rulerSecondsPerPixel = ((viewport?.width ?? surfaceWidthPx) * scale.secondsPerUnit) / surfaceWidthPx;

  /** The canvas owns the empty surface's right button; an item's right-click is the menu's. */
  const onContextMenu = (event: React.MouseEvent) => {
    if ((event.target as Element).closest("[data-element-id],[data-connection-id]") === null) {
      event.preventDefault();
    }
  };

  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas.
  return (
    <div ref={hostRef} className="timeline-canvas canvas-host" role="application" aria-label="Timeline" onContextMenu={onContextMenu}>
      <DiagramCanvas
        definition={TIMELINE_DEFINITION}
        config={config}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Timeline"
        className="timeline-surface canvas-viewport"
        scrollbarsClassName="timeline-scrollbars"
      />
      <TimelineRuler startSeconds={rulerStartSeconds} secondsPerPixel={rulerSecondsPerPixel} widthPx={surfaceWidthPx} />
    </div>
  );
}

