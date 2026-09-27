import { useLayoutEffect, useMemo, useRef, useState } from "react";
import { forwardBezierPath, horizontalBezierPath } from "@client/canvas/connectors";
import { elementSourceOf } from "@client/canvas/selection";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import type { DiagramCanvasProps as ShellCanvasProps } from "@client/shell/panels/diagramCanvas";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import type { DiagramDefinition, LabelDeclaration } from "@client/canvas/library/definition/diagramDefinition";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import type { DiagramEventHandlers, DiagramViewport } from "@client/canvas/library/api/diagramEvents";
import { TimelineRuler } from "./TimelineRuler";
import { useTimelineStream } from "./useTimelineStream";
import type { TimelineElement, TimelineModel } from "./timelineModel";
import { placementId, relationId } from "@client/canvas/gestureIds";

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

/** One canvas unit as a fraction of a row, for the hint's declared row arithmetic. */
const ROWS_PER_UNIT = 1 / ROW_HEIGHT;

/** How far above the box the drag hint sits, matching where the shared span drew it. */
const HINT_ABOVE = ELEMENT_HEIGHT / 2 + 8;

/**
 * The nearest row for a module-space y, matching TimelineRows.ToNearestRow's away-from-zero midpoint.
 *
 * Kept here rather than imported: the library's `snapToStep` is library-internal (module-client-api-readme's
 * design lists it so). A dragged element's position already arrives snapped by the declaration,
 * so for a move this only turns an exact row back into its index.
 */
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
  return placementId(seconds, row);
}

/**
 * The drag hint both element types show, declared once.
 *
 * <b>The one row in the tree whose text is arithmetic rather than a field</b>: the time under
 * the element's left edge, and the row its top would land on. Both read the LIVE bounds, which
 * is why `bounds` is a binding root at all - the model still holds the PRE-drag position while
 * this is on screen, so no payload could carry either number.
 */
const DRAG_HINT: LabelDeclaration = {
  // THE DRAG HINT, and the one row in the whole tree whose text is arithmetic rather
  // than a field: the time under the element's left edge, and the row its top would
  // land on. Both read the LIVE bounds, which is why `bounds` is a root at all - the
  // model still holds the PRE-drag position while this is on screen, so a payload
  // could not carry either of them.
  text: {
    parts: [
      {
        path: "bounds.left",
        number: { times: "payload.secondsPerUnit", plus: "payload.originSeconds", format: "yyyy-MM-ddTHH:mm:ss" },
      },
      {
        // `row N`: a literal word beside a computed number, which is why parts nest.
        parts: [
          { template: "row" },
          { path: "bounds.top", number: { times: ROWS_PER_UNIT, plus: "payload.originRows", round: "nearest" } },
        ],
        join: " ",
      },
    ],
    join: " · ",
  },
  when: { path: "state.dragging", is: "true" },
  offset: { x: 0, y: -HINT_ABOVE },
  className: "timeline-hint canvas-hint",
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
      // The box the shared span drew, chosen by TYPE rather than by a payload flag: the
      // definition already had two types and the renderer decided between them again at draw
      // time, which is the duplication this migration removes.
      shape: "span",
      classNames: [{ className: "timeline-period canvas-node", on: "shape" }],
      labels: [
        {
          text: { template: "{element.label}" },
          placement: "inside",
          truncate: true,
          editable: true,
          className: "timeline-label canvas-node-label",
        },
        DRAG_HINT,
      ],
      anchors: {
        kind: "sides",
        fractions: [
          { side: "left", at: 0.5, name: "begin" },
          { side: "right", at: 0.5, name: "end" },
        ],
        edgeSides: "horizontal",
      },
      sizing: "user",
    },
    {
      id: "moment",
      // A diamond rather than a box - the built-in that exists because this row needed it.
      shape: "moment",
      classNames: [{ className: "timeline-moment", on: "shape" }],
      labels: [
        {
          text: { template: "{element.label}" },
          placement: "beside",
          editable: true,
          className: "timeline-label canvas-node-label",
        },
        DRAG_HINT,
      ],
      anchors: {
        kind: "sides",
        fractions: [
          { side: "left", at: 0.5, name: "begin" },
          { side: "right", at: 0.5, name: "end" },
        ],
        edgeSides: "horizontal",
      },
      sizing: "model",
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
  // WHAT THIS TYPE OFFERS, AND WHAT INVOKES IT. The key list was hand-written in this canvas
  // and the delete was a keystroke it built to describe a gesture the library had already
  // handed it. Declared, the library derives the key set and dispatches an action id.
  actions: [
    { id: "rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
    { id: "insert", backendKey: "Insert", invokedBy: [{ kind: "shortcut", key: "Insert" }], appliesTo: [{ kind: "element" }] },
    { id: "add-right", backendKey: "Tab", invokedBy: [{ kind: "shortcut", key: "Tab" }], appliesTo: [{ kind: "element" }] },
    { id: "add-below", backendKey: "Enter", invokedBy: [{ kind: "shortcut", key: "Enter" }], appliesTo: [{ kind: "element" }] },
    { id: "delete", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }, { kind: "connection" }] },
  ],
  // Where a dragged element comes to rest, said once so the drag shows what the drop sends: an
  // element's top on a row, and a date-only element's begin on the start of a day - where
  // TimelineScale.ToTime lands a date-only begin. The day lattice depends on the frozen scale,
  // so it rides each element's payload; an element with a time of day carries none and moves
  // freely in x, as its backend keeps its seconds.
  snap: {
    y: { step: ROW_HEIGHT },
    x: { step: { path: "payload.dayUnits" }, origin: { path: "payload.dayOriginUnits" } },
  },
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

  const runAction = (actionId: string, sourceId?: string) => {
    // A refusal needs nothing here: the call reports it to the library's refusal line.
    void executeAction(actionId, sourceId ? elementSourceOf(sourceId) : undefined);
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

