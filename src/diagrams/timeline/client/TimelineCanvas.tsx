import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { create } from "@bufbuild/protobuf";
import { EmptySchema } from "@bufbuild/protobuf/wkt";
import { horizontalBezierPath, sideAnchorOf, type ConnectorBox } from "@client/canvas/connectors";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { TOOLBOX_DRAG_TYPE } from "@client/shell/panels/DiagramToolboxContext";
import { useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useRegisterDiagramView } from "@client/shell/panels/DiagramViewContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import {
  ContextSelectionAction,
  ContextSelectionSchema,
  ContextSourceSchema,
  type ContextSelection,
  type ContextShortcut,
} from "@client/generated/context_pb";
import { TimelineRuler } from "./TimelineRuler";
import { TimelineScrollbars } from "./TimelineScrollbars";
import { useTimelineStream } from "./useTimelineStream";
import type { TimelineElement } from "./timelineModel";

/**
 * The vertical distance between adjacent rows, in the module's own y units.
 *
 * Mirrors `TimelineRows.Height` in the backend, and must stay equal to it: the y this canvas
 * sends with a move is `row × this`, and the backend divides the same constant back out. A
 * mismatch would land every vertical drag on the wrong row.
 */
const ROW_HEIGHT = 60;

/** How tall an element's box is drawn, leaving a gutter between rows. */
const ELEMENT_HEIGHT = 36;

/** A moment's marker radius. */
const MOMENT_RADIUS = 9;

const DAY = 86400;

/** Zoom limits, in seconds per pixel: from about a minute across the view to about a century. */
const MIN_SECONDS_PER_PIXEL = 0.05;
const MAX_SECONDS_PER_PIXEL = 4_000_000;
const ZOOM_STEP = 1.25;

interface TimelineView {
  /** The time at the view's left edge, seconds since the epoch. */
  startSeconds: number;
  /** How many seconds one pixel covers - the zoom, which never reaches the backend. */
  secondsPerPixel: number;
  /** Vertical scroll, in the module's y units. */
  panY: number;
}

interface DragState {
  id: string;
  /** Where the pointer went down. */
  clientX: number;
  clientY: number;
  /** The element's own placement when the drag began. */
  beginSeconds: number;
  y: number;
  moved: boolean;
}

interface DragPreview {
  id: string;
  beginSeconds: number;
  row: number;
}

interface ResizeState {
  id: string;
  side: "left" | "right";
  clientX: number;
  /** The edge's time when the resize began. */
  edgeSeconds: number;
  /** The opposite edge, which the moving one must not cross (Requirement 7.4). */
  limitSeconds: number;
  dateOnly: boolean;
}

interface ConnectDrag {
  fromId: string;
  /** Where the pending curve currently ends, in module coordinates. */
  x: number;
  y: number;
  /** The element under the pointer, when there is one. */
  overId?: string;
}

/**
 * The timeline: periods and moments on rows along a time axis, with a view-fixed ruler and
 * every gesture ending in one command (Requirements 4-9).
 *
 * Zoom and pan live entirely here as a seconds-to-pixels transform; what crosses the wire is
 * always a point in the module's own coordinate space - seconds and row-height units - which is
 * what keeps the backend ignorant of the viewport (Requirement 5).
 */
export function TimelineCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, moveElementTo } = useTimelineStream(projectId, path);
  const { select, executeAction, executeShortcut, setProperty } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const surfaceRef = useRef<HTMLDivElement | null>(null);

  // A right-click's menu, opened once the pushed selection for that element arrives with its
  // actions: the menu shows the backend's answer, never a guess - the C4 canvas's discipline.
  const [menuPosition, setMenuPosition] = useState<{ x: number; y: number } | null>(null);
  const pendingMenuRef = useRef<{ id: string; position: { x: number; y: number } } | null>(null);

  useRegisterDiagramToolbox(useToolboxItems(projectId, path));

  const [view, setView] = useState<TimelineView>(() => ({
    startSeconds: Date.now() / 1000 - 15 * DAY,
    secondsPerPixel: (60 * DAY) / 1200,
    panY: -ROW_HEIGHT,
  }));
  const fittedRef = useRef(false);

  const panRef = useRef<{ clientX: number; clientY: number; view: TimelineView; moved: boolean } | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const [drag, setDrag] = useState<DragPreview | null>(null);
  const resizeRef = useRef<ResizeState | null>(null);
  const [resize, setResize] = useState<{ id: string; side: "left" | "right"; edgeSeconds: number } | null>(null);
  const [connect, setConnect] = useState<ConnectDrag | null>(null);
  const connectRef = useRef<ConnectDrag | null>(null);
  const [rejection, setRejection] = useState("");

  const selectedId = elementIdOf(innermostKey(selection) ?? null);
  const selectionKey = innermostKey(selection);

  // The right-click's menu opens when the selection it asked for arrives, actions and all.
  useEffect(() => {
    const pending = pendingMenuRef.current;
    if (pending && selectionKey === `element:${pending.id}`) {
      pendingMenuRef.current = null;
      setMenuPosition(pending.position);
    }
  }, [selectionKey]);

  /** Fits the whole timeline into view, with a margin. */
  const fitToView = useCallback(() => {
    const surface = surfaceRef.current;
    const elements = [...model.elements.values()];
    if (!surface || elements.length === 0) {
      return;
    }

    const begins = elements.map((element) => element.x);
    const ends = elements.map((element) => endSecondsOf(element));
    const min = Math.min(...begins);
    const max = Math.max(...ends);
    const span = Math.max(max - min, DAY);
    const width = surface.getBoundingClientRect().width || 1200;

    setView((current) => ({
      ...current,
      startSeconds: min - span * 0.1,
      secondsPerPixel: clampZoom((span * 1.2) / width),
      panY: Math.min(...elements.map((element) => element.y)) - ROW_HEIGHT,
    }));
  }, [model]);

  // Fitted once, when the first delta lands - and never again, because refitting under the
  // user's feet on every edit would fight their own pan.
  useEffect(() => {
    if (!loading && !fittedRef.current && model.elements.size > 0) {
      fittedRef.current = true;
      fitToView();
    }
  }, [loading, model, fitToView]);

  const zoomBy = useCallback((factor: number) => {
    setView((current) => {
      const surface = surfaceRef.current;
      const width = surface?.getBoundingClientRect().width ?? 1200;
      const next = clampZoom(current.secondsPerPixel * factor);
      // About the centre, so the thing being looked at stays where it is.
      const centre = current.startSeconds + (width / 2) * current.secondsPerPixel;
      return { ...current, secondsPerPixel: next, startSeconds: centre - (width / 2) * next };
    });
  }, []);

  useRegisterDiagramView(
    useMemo(
      () => ({
        zoomIn: () => zoomBy(1 / ZOOM_STEP),
        zoomOut: () => zoomBy(ZOOM_STEP),
        fitToView,
      }),
      [zoomBy, fitToView],
    ),
  );

  // Non-passive by hand: React's synthetic wheel listener cannot preventDefault, and without
  // it every zoom also scrolls the page.
  useEffect(() => {
    const surface = surfaceRef.current;
    if (!surface) {
      return;
    }

    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      zoomBy(event.deltaY > 0 ? ZOOM_STEP : 1 / ZOOM_STEP);
    };

    surface.addEventListener("wheel", onWheel, { passive: false });
    return () => surface.removeEventListener("wheel", onWheel);
  }, [zoomBy]);

  // Escape abandons whichever gesture is in flight, dispatching nothing (Requirement 6.6).
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") {
        return;
      }

      dragRef.current = null;
      resizeRef.current = null;
      connectRef.current = null;
      setDrag(null);
      setResize(null);
      setConnect(null);
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  const toSeconds = (clientX: number): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    return view.startSeconds + (clientX - (rect?.left ?? 0)) * view.secondsPerPixel;
  };

  const toModuleY = (clientY: number): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    return view.panY + (clientY - (rect?.top ?? 0));
  };

  const secondsToPx = (seconds: number): number => (seconds - view.startSeconds) / view.secondsPerPixel;
  const yToPx = (y: number): number => y - view.panY;

  const onSurfacePointerDown = (event: React.MouseEvent) => {
    // "Empty space" is the surface div OR the bare svg that fills it. A real click never lands
    // on the div itself - the svg covers it - so a target===currentTarget check silently
    // disabled panning for every real mouse, while the synthetic events that verified it
    // dispatched straight at the div and passed. Element shapes stopPropagation, so anything
    // arriving here from inside the svg is background.
    const target = event.target as Element;
    if (target !== event.currentTarget && !target.classList?.contains("timeline-content")) {
      return;
    }

    setMenuPosition(null);
    if (event.button === 0) {
      select(null);
    }

    // Both buttons pan on empty space: the left is the convention every canvas here follows,
    // and the right frees the left hand for selection-heavy work. The `moved` flag is what
    // keeps a motionless right-click from being eaten as a zero-length pan.
    panRef.current = { clientX: event.clientX, clientY: event.clientY, view, moved: false };
  };

  const onElementPointerDown = (event: React.MouseEvent, element: TimelineElement) => {
    event.stopPropagation();
    setRejection("");
    dragRef.current = {
      id: element.id,
      clientX: event.clientX,
      clientY: event.clientY,
      beginSeconds: element.x,
      y: element.y,
      moved: false,
    };
  };

  const onAnchorPointerDown = (event: React.MouseEvent, element: TimelineElement) => {
    // Starting a connection: drag from a side anchor to another element (Requirement 8.4).
    event.stopPropagation();
    const start: ConnectDrag = { fromId: element.id, x: toSeconds(event.clientX), y: toModuleY(event.clientY) };
    connectRef.current = start;
    setConnect(start);
  };

  const onResizePointerDown = (event: React.MouseEvent, element: TimelineElement, side: "left" | "right") => {
    event.stopPropagation();
    setRejection("");
    resizeRef.current = {
      id: element.id,
      side,
      clientX: event.clientX,
      edgeSeconds: side === "left" ? element.x : endSecondsOf(element),
      limitSeconds: side === "left" ? endSecondsOf(element) : element.x,
      dateOnly: element.dateOnly,
    };
  };

  const onPointerMove = (event: React.MouseEvent) => {
    const dragging = dragRef.current;
    if (dragging) {
      const deltaSeconds = (event.clientX - dragging.clientX) * view.secondsPerPixel;
      const y = dragging.y + (event.clientY - dragging.clientY);
      dragging.moved ||= Math.abs(event.clientX - dragging.clientX) + Math.abs(event.clientY - dragging.clientY) > 3;
      // The row snaps as the pointer crosses the midpoint, so the user sees where it will land
      // before releasing (Requirement 6.2). Rounded away from zero to match TimelineRows.
      setDrag({
        id: dragging.id,
        beginSeconds: dragging.beginSeconds + deltaSeconds,
        row: nearestRow(y),
      });
      return;
    }

    const resizing = resizeRef.current;
    if (resizing) {
      const deltaSeconds = (event.clientX - resizing.clientX) * view.secondsPerPixel;
      let edge = resizing.edgeSeconds + deltaSeconds;
      // The edge stops at the other rather than crossing it (Requirement 7.4). The handler
      // clamps again server-side; this copy is what the user feels.
      edge = resizing.side === "left" ? Math.min(edge, resizing.limitSeconds) : Math.max(edge, resizing.limitSeconds);
      setResize({ id: resizing.id, side: resizing.side, edgeSeconds: edge });
      return;
    }

    const connecting = connectRef.current;
    if (connecting) {
      const next = { ...connecting, x: toSeconds(event.clientX), y: toModuleY(event.clientY) };
      connectRef.current = next;
      setConnect(next);
      return;
    }

    const pan = panRef.current;
    if (pan) {
      pan.moved ||= Math.abs(event.clientX - pan.clientX) + Math.abs(event.clientY - pan.clientY) > 3;
      setView({
        ...pan.view,
        startSeconds: pan.view.startSeconds - (event.clientX - pan.clientX) * pan.view.secondsPerPixel,
        panY: pan.view.panY - (event.clientY - pan.clientY),
      });
    }
  };

  /** The canvas owns the right button: a drag pans, and the browser's own menu never appears. */
  const onSurfaceContextMenu = (event: React.MouseEvent) => {
    event.preventDefault();
  };

  const onPointerUp = (event: React.MouseEvent) => {
    panRef.current = null;

    const dragging = dragRef.current;
    const landed = drag;
    dragRef.current = null;
    setDrag(null);
    if (dragging && landed && dragging.moved) {
      void (async () => {
        const error = await moveElementTo(landed.id, landed.beginSeconds, landed.row * ROW_HEIGHT);
        if (error) {
          setRejection(error);
        }
      })();
      return;
    }

    if (dragging && !dragging.moved) {
      // A press with no movement is a click: a selection, never an edit.
      select(elementSelection(entryId, path, dragging.id));
      return;
    }

    const resizing = resizeRef.current;
    const resized = resize;
    resizeRef.current = null;
    setResize(null);
    if (resizing && resized) {
      // A resize edits begin or end through the property channel of the selection the adorner
      // belongs to - one command, one undo (Requirement 7.6).
      const property = resizing.side === "left" ? "timeline.begin" : "timeline.end";
      void (async () => {
        const outcome = await setProperty(property, formatSeconds(resized.edgeSeconds, resizing.dateOnly));
        if (!outcome.accepted) {
          setRejection(outcome.error);
        }
      })();
      return;
    }

    const connecting = connectRef.current;
    connectRef.current = null;
    setConnect(null);
    if (connecting) {
      // The element under the release, read from the event itself as well as from the tracked
      // hover: a fast drag can land its mouseup before any mouseenter fired, and the gesture
      // must not depend on the hover having kept up.
      const under = (event.target as Element | null)?.closest?.("[data-element-id]");
      const targetId = connecting.overId ?? under?.getAttribute("data-element-id") ?? null;
      if (targetId === connecting.fromId) {
        // Back onto its own source: a relation to itself is refused anyway, so this is a
        // "never mind" and nothing is sent.
        return;
      }

      // The whole gesture in one call - source and landing together in a rel: id. Deliberately
      // stateless: the two-call protocol this replaces kept an armed source in the backend
      // between calls, and a stale arm related the wrong pair.
      const landing = targetId ?? newPlacementId(connecting.x, nearestRow(connecting.y));
      void (async () => {
        const outcome = await executeAction("timeline.connect", elementSource(`rel:${connecting.fromId}->${landing}`));
        if (!outcome.accepted) {
          setRejection(outcome.error);
        }
      })();
    }
  };

  const onElementPointerEnter = (element: TimelineElement) => {
    const connecting = connectRef.current;
    if (connecting && element.id !== connecting.fromId) {
      const next = { ...connecting, overId: element.id };
      connectRef.current = next;
      setConnect(next);
    }
  };

  const onElementPointerLeave = () => {
    const connecting = connectRef.current;
    if (connecting?.overId) {
      const next = { ...connecting, overId: undefined };
      connectRef.current = next;
      setConnect(next);
    }
  };

  const onElementContextMenu = (event: React.MouseEvent, element: TimelineElement) => {
    event.preventDefault();
    event.stopPropagation();
    if (panRef.current?.moved) {
      // The right button was panning; releasing it must not also open a menu.
      return;
    }

    // The menu opens once the pushed selection for this element arrives with its actions, so
    // what it shows is the backend's answer rather than a guess.
    pendingMenuRef.current = { id: element.id, position: { x: event.clientX, y: event.clientY } };
    select(elementSelection(entryId, path, element.id, ContextSelectionAction.CONTEXT_MENU));
  };

  /**
   * A toolbox entry dropped anywhere on the canvas: the drop names a placement - the time and
   * row under the pointer - and the element appears there with nothing asked. One handler on
   * the surface; drops over elements bubble here and land at the pointer all the same.
   */
  const onSurfaceDrop = (event: React.DragEvent) => {
    const actionId = event.dataTransfer.getData(TOOLBOX_DRAG_TYPE);
    if (!actionId) {
      return;
    }

    event.preventDefault();
    void (async () => {
      const outcome = await executeAction(
        actionId,
        elementSource(newPlacementId(toSeconds(event.clientX), nearestRow(toModuleY(event.clientY)))));
      if (!outcome.accepted) {
        setRejection(outcome.error);
      }
    })();
  };

  const onDragOver = (event: React.DragEvent) => {
    if (event.dataTransfer.types.includes(TOOLBOX_DRAG_TYPE)) {
      event.preventDefault();
      event.dataTransfer.dropEffect = "copy";
    }
  };

  /**
   * Structural keys travel to the backend as data - the backend holds the key-to-action table,
   * this canvas only forwards the keystroke against the selected element. Tab and Enter are
   * prevented from their browser defaults (focus traversal, activation) when an element is
   * selected, because here they mean "add after" and "add below".
   */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!selectedId || isTextTarget(event.target)) {
      return;
    }

    const shortcut = shortcutFor(event);
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    void (async () => {
      const outcome = await executeShortcut(shortcut, elementSource(selectedId));
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  if (failed) {
    return (
      <div className="timeline-canvas timeline-canvas-message">
        <p>This timeline could not be opened.</p>
      </div>
    );
  }

  // Where each element currently sits, with the in-flight gesture's preview overriding the
  // model - which is what makes the connections follow the drag (Requirements 6.5, 8.6): the
  // curves are recomputed from these boxes on every pointer move.
  const boxes = new Map<string, ConnectorBox>();
  for (const element of model.elements.values()) {
    boxes.set(element.id, boxFor(element, drag, resize, secondsToPx, yToPx, view.secondsPerPixel));
  }

  const width = surfaceRef.current?.getBoundingClientRect().width ?? 1200;

  return (
    <div className="timeline-canvas">
      <div
        ref={surfaceRef}
        className="timeline-surface"
        role="application"
        aria-label="Timeline"
        tabIndex={0}
        onMouseDown={onSurfacePointerDown}
        onMouseMove={onPointerMove}
        onMouseUp={onPointerUp}
        onMouseLeave={onPointerUp}
        onContextMenu={onSurfaceContextMenu}
        onKeyDown={onKeyDown}
        onDragOver={onDragOver}
        onDrop={onSurfaceDrop}
      >
        <svg className="timeline-content">
          {[...model.connections.values()].map((connection) => {
            const from = boxes.get(connection.fromElementId);
            const to = boxes.get(connection.toElementId);
            if (!from || !to) {
              // A dangling connection is the validator's to report; there is nothing to draw.
              return null;
            }

            // Side anchors and a horizontal bezier, from the shared geometry and nothing else
            // (Requirements 8.1-8.3). Which sides face each other follows from the boxes.
            const [a, b] = facingAnchors(from, to);
            return (
              <g key={connection.id} className="timeline-connection">
                <path d={horizontalBezierPath(a, b)} />
                {connection.label ? (
                  <text x={(a.x + b.x) / 2} y={(a.y + b.y) / 2 - 6}>
                    {connection.label}
                  </text>
                ) : null}
              </g>
            );
          })}

          {connect ? <PendingConnection boxes={boxes} connect={connect} secondsToPx={secondsToPx} yToPx={yToPx} /> : null}

          {[...model.elements.values()].map((element) => (
            <TimelineElementShape
              key={element.id}
              element={element}
              box={boxes.get(element.id)!}
              selected={element.id === selectedId}
              connectTarget={connect?.overId === element.id}
              dragPreview={drag?.id === element.id ? drag : null}
              resizePreview={resize?.id === element.id ? resize : null}
              onElementDown={onElementPointerDown}
              onAnchorDown={onAnchorPointerDown}
              onResizeDown={onResizePointerDown}
              onElementEnter={onElementPointerEnter}
              onElementLeave={onElementPointerLeave}
              onElementContextMenu={onElementContextMenu}
            />
          ))}
        </svg>
        <TimelineRuler startSeconds={view.startSeconds} secondsPerPixel={view.secondsPerPixel} widthPx={width} />
        <TimelineScrollbars model={model} view={view} widthPx={width} onPan={(startSeconds, panY) => setView((current) => ({ ...current, startSeconds, panY }))} />
      </div>
      <ContextMenu
        open={menuPosition !== null}
        groups={toMenuGroups(actions, (action) => {
          setMenuPosition(null);
          void executeAction(action.id, selectedId ? elementSource(selectedId) : undefined);
        })}
        position={menuPosition ?? { x: 0, y: 0 }}
        onClose={() => setMenuPosition(null)}
      />
      {loading ? <p className="timeline-status">Opening…</p> : null}
      {rejection ? <p className="timeline-rejection">{rejection}</p> : null}
    </div>
  );
}

/** The placement id a gesture carries when it lands on empty canvas: `new:{seconds},{row}`. */
function newPlacementId(seconds: number, row: number): string {
  return `new:${seconds},${row}`;
}

/** A structural key as data. The backend holds the key-to-action table, not this canvas. */
function shortcutFor(event: React.KeyboardEvent): ContextShortcut | null {
  const structural = ["F2", "Delete", "Insert", "Tab", "Enter"];
  if (!structural.includes(event.key)) {
    return null;
  }

  return { key: event.key, ctrl: event.ctrlKey, shift: event.shiftKey, alt: event.altKey, meta: event.metaKey } as ContextShortcut;
}

function isTextTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }

  const tag = target.tagName.toLowerCase();
  return tag === "input" || tag === "textarea" || target.isContentEditable;
}

/** An element's box in pixels, with any in-flight preview applied. Centre-based, as the shared geometry expects. */
function boxFor(
  element: TimelineElement,
  drag: DragPreview | null,
  resize: { id: string; side: "left" | "right"; edgeSeconds: number } | null,
  secondsToPx: (seconds: number) => number,
  yToPx: (y: number) => number,
  secondsPerPixel: number,
): ConnectorBox {
  let beginSeconds = element.x;
  let endSeconds = endSecondsOf(element);
  let y = element.y;

  if (drag && drag.id === element.id) {
    const duration = endSeconds - beginSeconds;
    beginSeconds = drag.beginSeconds;
    endSeconds = beginSeconds + duration;
    y = drag.row * ROW_HEIGHT;
  }

  if (resize && resize.id === element.id) {
    if (resize.side === "left") {
      beginSeconds = resize.edgeSeconds;
    } else {
      endSeconds = resize.edgeSeconds;
    }
  }

  const width = element.isPeriod
    ? Math.max((endSeconds - beginSeconds) / secondsPerPixel, 2)
    : MOMENT_RADIUS * 2;
  const left = secondsToPx(beginSeconds);

  return {
    x: element.isPeriod ? left + width / 2 : left,
    y: yToPx(y) + ELEMENT_HEIGHT / 2,
    width,
    height: ELEMENT_HEIGHT,
  };
}

function endSecondsOf(element: TimelineElement): number {
  if (!element.isPeriod) {
    return element.x;
  }

  const parsed = Date.parse(element.end.includes("T") ? `${element.end}Z` : `${element.end}T00:00:00Z`) / 1000;
  return Number.isFinite(parsed) ? parsed : element.x;
}

/** The anchors on the sides that face each other, per Requirement 8.1. */
function facingAnchors(from: ConnectorBox, to: ConnectorBox): [ReturnType<typeof sideAnchorOf>, ReturnType<typeof sideAnchorOf>] {
  const toIsRight = to.x >= from.x;
  return [sideAnchorOf(from, toIsRight ? "right" : "left"), sideAnchorOf(to, toIsRight ? "left" : "right")];
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

function elementIdOf(key: string | null): string | null {
  return key?.startsWith("element:") ? key.slice("element:".length) : null;
}

function elementSource(elementId: string) {
  return create(ContextSourceSchema, { source: { case: "elementId", value: { value: elementId } } });
}

function elementSelection(
  entryId: Uint8Array,
  path: readonly string[],
  elementId: string,
  gesture?: ContextSelectionAction,
): ContextSelection {
  const child = create(ContextSelectionSchema, {
    source: 2, // DIAGRAM_CANVAS
    id: { source: { case: "elementId", value: { value: elementId } } },
    // Empty asks the backend to fill in the full path; sending a partial one is refused.
    path: { segments: [] },
    detail: gesture === undefined
      ? { case: "none" as const, value: create(EmptySchema) }
      : { case: "action" as const, value: gesture },
  });

  return create(ContextSelectionSchema, {
    source: 1,
    id: { source: { case: "entryId", value: { value: entryId } } },
    path: { segments: [...path] },
    detail: { case: "child", value: child },
  });
}

interface TimelineElementShapeProps {
  element: TimelineElement;
  box: ConnectorBox;
  selected: boolean;
  connectTarget: boolean;
  dragPreview: DragPreview | null;
  resizePreview: { id: string; side: "left" | "right"; edgeSeconds: number } | null;
  onElementDown: (event: React.MouseEvent, element: TimelineElement) => void;
  onAnchorDown: (event: React.MouseEvent, element: TimelineElement) => void;
  onResizeDown: (event: React.MouseEvent, element: TimelineElement, side: "left" | "right") => void;
  onElementEnter: (element: TimelineElement) => void;
  onElementLeave: () => void;
  onElementContextMenu: (event: React.MouseEvent, element: TimelineElement) => void;
}

function TimelineElementShape({
  element,
  box,
  selected,
  connectTarget,
  dragPreview,
  resizePreview,
  onElementDown,
  onAnchorDown,
  onResizeDown,
  onElementEnter,
  onElementLeave,
  onElementContextMenu,
}: TimelineElementShapeProps) {
  const left = box.x - box.width / 2;
  const top = box.y - box.height / 2;
  const classes = ["timeline-element"];
  if (selected) {
    classes.push("timeline-selected");
  }

  if (connectTarget) {
    classes.push("timeline-connect-target");
  }

  // What the gesture would land on, visible before the user commits (Requirements 6.5, 7.7).
  const hint = dragPreview
    ? `${formatSeconds(dragPreview.beginSeconds, element.dateOnly)} · row ${dragPreview.row}`
    : resizePreview
      ? formatSeconds(resizePreview.edgeSeconds, element.dateOnly)
      : null;

  return (
    <g
      className={classes.join(" ")}
      data-element-id={element.id}
      onMouseDown={(event) => onElementDown(event, element)}
      onMouseEnter={() => onElementEnter(element)}
      onMouseLeave={onElementLeave}
      onContextMenu={(event) => onElementContextMenu(event, element)}
      onDragOver={(event) => event.preventDefault()}
    >
      {element.isPeriod ? (
        <rect className="timeline-period" x={left} y={top} width={box.width} height={box.height} rx={6} />
      ) : (
        <path
          className="timeline-moment"
          d={diamond(box.x, box.y)}
        />
      )}
      <text
        className="timeline-label"
        // A label wider than its box sits beside it instead of overflowing invisibly under the
        // neighbours - the same placement a moment's label always has. Approximated at 7px per
        // character, which errs on the side of moving outside a little early.
        {...(element.isPeriod && box.width >= (element.label || element.id).length * 7 + 8
          ? { x: box.x, textAnchor: undefined }
          : { x: element.isPeriod ? box.x + box.width / 2 + 6 : box.x + MOMENT_RADIUS + 6, textAnchor: "start" as const })}
        y={box.y + 4}
      >
        {element.label || element.id}
      </text>
      {hint ? (
        <text className="timeline-hint" x={box.x} y={top - 8}>
          {hint}
        </text>
      ) : null}
      {selected ? (
        <>
          {/* Adorners first, anchors after: SVG paints in order, and the full-height adorner
              strip used to cover the anchor down to a one-pixel sliver - which is why starting
              a relation felt unreliable, and why a grab meant as a relate became a resize. Now
              the dot and its generous hit circle sit on top; the strip stays grabbable above
              and below the dot. The left edge edits begin; the right edits end; a moment has no
              right edge to offer, and the menu's "Give it an end" is the path instead
              (Requirement 7.5). */}
          <rect
            className="timeline-adorner"
            x={left - 4}
            y={top}
            width={8}
            height={box.height}
            onMouseDown={(event) => onResizeDown(event, element, "left")}
          />
          {element.isPeriod ? (
            <rect
              className="timeline-adorner"
              x={left + box.width - 4}
              y={top}
              width={8}
              height={box.height}
              onMouseDown={(event) => onResizeDown(event, element, "right")}
            />
          ) : null}
          <circle className="timeline-anchor" cx={left} cy={box.y} r={5} />
          <circle
            className="timeline-anchor-hit"
            cx={left}
            cy={box.y}
            r={14}
            onMouseDown={(event) => onAnchorDown(event, element)}
          />
          <circle className="timeline-anchor" cx={left + box.width} cy={box.y} r={5} />
          <circle
            className="timeline-anchor-hit"
            cx={left + box.width}
            cy={box.y}
            r={14}
            onMouseDown={(event) => onAnchorDown(event, element)}
          />
        </>
      ) : null}
    </g>
  );
}

function diamond(cx: number, cy: number): string {
  const r = MOMENT_RADIUS;
  return `M ${cx - r} ${cy} L ${cx} ${cy - r} L ${cx + r} ${cy} L ${cx} ${cy + r} Z`;
}

function PendingConnection({
  boxes,
  connect,
  secondsToPx,
  yToPx,
}: {
  boxes: Map<string, ConnectorBox>;
  connect: ConnectDrag;
  secondsToPx: (seconds: number) => number;
  yToPx: (y: number) => number;
}) {
  const from = boxes.get(connect.fromId);
  if (!from) {
    return null;
  }

  const end = connect.overId && boxes.has(connect.overId)
    ? facingAnchors(from, boxes.get(connect.overId)!)[1]
    : { x: secondsToPx(connect.x), y: yToPx(connect.y) };
  const start = sideAnchorOf(from, end.x >= from.x ? "right" : "left");

  return <path className="timeline-pending-connection" d={horizontalBezierPath(start, end)} />;
}

function clampZoom(secondsPerPixel: number): number {
  return Math.min(MAX_SECONDS_PER_PIXEL, Math.max(MIN_SECONDS_PER_PIXEL, secondsPerPixel));
}
