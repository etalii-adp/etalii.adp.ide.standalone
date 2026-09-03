import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { FrameElement } from "@client/canvas/elements/frame/FrameElement";
import { FixedBezierConnection } from "@client/canvas/connections/fixed-bezier/FixedBezierConnection";
import { StraightConnection } from "@client/canvas/connections/straight/StraightConnection";
import { CanvasScrollbars } from "@client/canvas/scroll/CanvasScrollbars";
import { scrollExtentOf } from "@client/canvas/scroll/scrollGeometry";
import type { ConnectorBox, Point } from "@client/canvas/connectors";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { useElementContextMenu } from "@client/canvas/useElementContextMenu";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { TOOLBOX_DRAG_TYPE, useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useRegisterDiagramView } from "@client/shell/panels/DiagramViewContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { useDatabricksStream } from "./useDatabricksStream";
import { useSimulatedRun } from "./useSimulatedRun";
import type { DatabricksEdge, DatabricksModel } from "./databricksModel";

/** A node's drawn size, in the module's own canvas units - matching the backend layouts' spacing. */
export const NODE_WIDTH = 200;
export const NODE_HEIGHT = 56;

/** A target frame's drawn size; the frame is an enclosure, so it is larger than a node. */
const FRAME_WIDTH = 220;
const FRAME_HEIGHT = 120;

/** The id of the arrowhead marker this family defines and its edge stylesheet points at. */
const ARROWHEAD_ID = "databricks-arrowhead";

/** Zoom limits, in pixels per canvas unit. */
const MIN_PIXELS_PER_UNIT = 0.05;
const MAX_PIXELS_PER_UNIT = 8;
const ZOOM_STEP = 1.25;

interface DatabricksView {
  /** The canvas coordinate at the view's top-left corner. */
  startX: number;
  startY: number;
  /** How many pixels one canvas unit covers - the zoom, which never reaches the backend. */
  pixelsPerUnit: number;
}

interface DragState {
  id: string;
  clientX: number;
  clientY: number;
  x: number;
  y: number;
  moved: boolean;
}

interface DragPreview {
  id: string;
  x: number;
  y: number;
}

interface ConnectDrag {
  fromId: string;
  x: number;
  y: number;
  overId?: string;
}

export interface DatabricksCanvasConfig {
  /** The aria label naming which of the family's readings this canvas draws. */
  ariaLabel: string;
  /** Whether side anchors offer the dependency gesture - the job canvas's interaction. */
  connectable: boolean;
  /**
   * Intercepts an action id before it reaches the backend; answering true means the canvas
   * handled it locally. The simulation engine's seam (Requirement 8.6): simulated ids play
   * client-side, everything else still travels.
   */
  interceptAction?: (actionId: string) => boolean;
}

/**
 * The family's shared canvas: boxes, frames and directed edges at the positions the backend
 * computed and the `.adp` authored, drawn through the central canvas library. The three diagram
 * types differ in what arrives on the stream and whether the dependency gesture is offered -
 * everything else (pan, zoom, drag-to-reposition through the layout path, selection, the shared
 * context menu, toolbox drops via placement ids) is one implementation.
 *
 * A drag never writes the body file: `moveElementTo` lands in the registration's `layout:`
 * block as one undoable command (Requirement 7).
 */
export function DatabricksCanvas({
  projectId,
  entryId,
  path,
  ariaLabel,
  connectable,
  interceptAction,
}: DiagramCanvasProps & DatabricksCanvasConfig) {
  const { model, loading, failed, moveElementTo } = useDatabricksStream(projectId, path);
  const simulation = useSimulatedRun(model);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const surfaceRef = useRef<HTMLDivElement | null>(null);

  useRegisterDiagramToolbox(useToolboxItems(projectId, path));

  const [view, setView] = useState<DatabricksView>(() => ({ startX: -60, startY: -60, pixelsPerUnit: 1 }));
  const fittedRef = useRef(false);

  const panRef = useRef<{ clientX: number; clientY: number; view: DatabricksView; moved: boolean } | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const [drag, setDrag] = useState<DragPreview | null>(null);
  const [connect, setConnect] = useState<ConnectDrag | null>(null);
  const connectRef = useRef<ConnectDrag | null>(null);
  const [rejection, setRejection] = useState("");

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  const { menuPosition, openMenuAt, closeMenu } = useElementContextMenu(selectionKey, actions.length > 0, (id) =>
    select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
  );

  /** Fits everything into view, with a margin. */
  const fitToView = useCallback(() => {
    const surface = surfaceRef.current;
    const boxes = [...model.nodes.values(), ...model.frames.values()];
    if (!surface || boxes.length === 0) {
      return;
    }

    const minX = Math.min(...boxes.map((box) => box.x));
    const maxX = Math.max(...boxes.map((box) => box.x + FRAME_WIDTH));
    const span = Math.max(maxX - minX, NODE_WIDTH);
    const width = surface.getBoundingClientRect().width || 1200;

    setView({
      startX: minX - span * 0.1,
      startY: Math.min(...boxes.map((box) => box.y)) - NODE_HEIGHT,
      pixelsPerUnit: clampZoom(width / (span * 1.2)),
    });
  }, [model]);

  // Fitted once, when the first delta lands - refitting on every edit would fight the user's pan.
  useEffect(() => {
    if (!loading && !fittedRef.current && model.nodes.size > 0) {
      fittedRef.current = true;
      fitToView();
    }
  }, [loading, model, fitToView]);

  const zoomBy = useCallback((factor: number) => {
    setView((current) => {
      const rect = surfaceRef.current?.getBoundingClientRect();
      const width = rect?.width || 1200;
      const height = rect?.height || 600;
      const next = clampZoom(current.pixelsPerUnit * factor);
      // About the centre, so the thing being looked at stays where it is.
      const centreX = current.startX + (width / 2) / current.pixelsPerUnit;
      const centreY = current.startY + (height / 2) / current.pixelsPerUnit;
      return {
        startX: centreX - (width / 2) / next,
        startY: centreY - (height / 2) / next,
        pixelsPerUnit: next,
      };
    });
  }, []);

  useRegisterDiagramView(
    useMemo(
      () => ({
        zoomIn: () => zoomBy(ZOOM_STEP),
        zoomOut: () => zoomBy(1 / ZOOM_STEP),
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
      zoomBy(event.deltaY > 0 ? 1 / ZOOM_STEP : ZOOM_STEP);
    };

    surface.addEventListener("wheel", onWheel, { passive: false });
    return () => surface.removeEventListener("wheel", onWheel);
  }, [zoomBy]);

  // Escape abandons whichever gesture is in flight, dispatching nothing.
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") {
        return;
      }

      dragRef.current = null;
      connectRef.current = null;
      setDrag(null);
      setConnect(null);
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  const toUnitsX = (clientX: number): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    return view.startX + (clientX - (rect?.left ?? 0)) / view.pixelsPerUnit;
  };

  const toUnitsY = (clientY: number): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    return view.startY + (clientY - (rect?.top ?? 0)) / view.pixelsPerUnit;
  };

  const xToPx = (x: number): number => (x - view.startX) * view.pixelsPerUnit;
  const yToPx = (y: number): number => (y - view.startY) * view.pixelsPerUnit;

  /**
   * Runs an action, letting the interception seam play it locally first (Requirement 8.6): a
   * simulated id starts the client-side show and never reaches executeAction, the history or
   * a file (Requirement 11.6); everything else travels as ever.
   */
  const runAction = (actionId: string, sourceId?: string) => {
    if ((interceptAction ?? simulation.intercept)(actionId)) {
      return;
    }

    void (async () => {
      const outcome = await executeAction(actionId, sourceId ? elementSourceOf(sourceId) : undefined);
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const onSurfacePointerDown = (event: React.MouseEvent) => {
    const target = event.target as Element;
    if (target !== event.currentTarget && !target.classList?.contains("databricks-content")) {
      return;
    }

    closeMenu();
    if (event.button === 0) {
      select(null);
    }

    panRef.current = { clientX: event.clientX, clientY: event.clientY, view, moved: false };
  };

  const onBoxPointerDown = (event: React.MouseEvent, id: string, x: number, y: number) => {
    event.stopPropagation();
    setRejection("");
    dragRef.current = { id, clientX: event.clientX, clientY: event.clientY, x, y, moved: false };
  };

  const onAnchorPointerDown = (event: React.MouseEvent, id: string) => {
    event.stopPropagation();
    const start: ConnectDrag = { fromId: id, x: toUnitsX(event.clientX), y: toUnitsY(event.clientY) };
    connectRef.current = start;
    setConnect(start);
  };

  const onPointerMove = (event: React.MouseEvent) => {
    const dragging = dragRef.current;
    if (dragging) {
      dragging.moved ||= Math.abs(event.clientX - dragging.clientX) + Math.abs(event.clientY - dragging.clientY) > 3;
      setDrag({
        id: dragging.id,
        x: dragging.x + (event.clientX - dragging.clientX) / view.pixelsPerUnit,
        y: dragging.y + (event.clientY - dragging.clientY) / view.pixelsPerUnit,
      });
      return;
    }

    const connecting = connectRef.current;
    if (connecting) {
      const next = { ...connecting, x: toUnitsX(event.clientX), y: toUnitsY(event.clientY) };
      connectRef.current = next;
      setConnect(next);
      return;
    }

    const pan = panRef.current;
    if (pan) {
      pan.moved ||= Math.abs(event.clientX - pan.clientX) + Math.abs(event.clientY - pan.clientY) > 3;
      setView({
        ...pan.view,
        startX: pan.view.startX - (event.clientX - pan.clientX) / pan.view.pixelsPerUnit,
        startY: pan.view.startY - (event.clientY - pan.clientY) / pan.view.pixelsPerUnit,
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
      // The authored position, raw: the layout block stores what the author placed, and
      // rounding it here would quietly turn the canvas into a grid.
      void (async () => {
        const error = await moveElementTo(landed.id, landed.x, landed.y);
        if (error) {
          setRejection(error);
        }
      })();
      return;
    }

    if (dragging && !dragging.moved) {
      // A press with no movement is a click: a selection, never an edit.
      select(elementSelectionOf(entryId, path, dragging.id));
      return;
    }

    const connecting = connectRef.current;
    connectRef.current = null;
    setConnect(null);
    if (connecting) {
      const under = (event.target as Element | null)?.closest?.("[data-element-id]");
      const targetId = connecting.overId ?? under?.getAttribute("data-element-id") ?? null;
      if (!targetId || targetId === connecting.fromId) {
        // Released on nothing, or back onto its own source: a "never mind". This family
        // creates tasks by drop, not by relation-to-empty-space, so no placement is fabricated.
        return;
      }

      // The whole gesture in one stateless rel: call - the dragged task becomes the dependency
      // the landing task waits for.
      runAction("databricks.connect", `rel:${connecting.fromId}->${targetId}`);
    }
  };

  const onBoxPointerEnter = (id: string) => {
    const connecting = connectRef.current;
    if (connecting && id !== connecting.fromId) {
      const next = { ...connecting, overId: id };
      connectRef.current = next;
      setConnect(next);
    }
  };

  const onBoxPointerLeave = () => {
    const connecting = connectRef.current;
    if (connecting?.overId) {
      const next = { ...connecting, overId: undefined };
      connectRef.current = next;
      setConnect(next);
    }
  };

  const openTargetMenuAt = (event: React.MouseEvent, id: string) => {
    if (panRef.current?.moved) {
      return;
    }

    openMenuAt(event, id);
  };

  /**
   * A toolbox entry dropped anywhere on the canvas: the drop names a placement - `new:{x},{y}`
   * under the pointer - and the element appears there with nothing asked (Requirement 9).
   */
  const onSurfaceDrop = (event: React.DragEvent) => {
    const actionId = event.dataTransfer.getData(TOOLBOX_DRAG_TYPE);
    if (!actionId) {
      return;
    }

    event.preventDefault();
    runAction(actionId, `new:${toUnitsX(event.clientX)},${toUnitsY(event.clientY)}`);
  };

  const onDragOver = (event: React.DragEvent) => {
    if (event.dataTransfer.types.includes(TOOLBOX_DRAG_TYPE)) {
      event.preventDefault();
      event.dataTransfer.dropEffect = "copy";
    }
  };

  /** Structural keys travel to the backend as data - the backend owns the key-to-action table. */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!selectedId || isTextTarget(event.target)) {
      return;
    }

    const shortcut = structuralShortcutFor(event, ["F2", "Delete"]);
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    void (async () => {
      const outcome = await executeShortcut(shortcut, elementSourceOf(selectedId));
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  if (failed) {
    return (
      <div className="databricks-canvas canvas-host databricks-canvas-message canvas-host-message">
        <p>This diagram could not be opened.</p>
      </div>
    );
  }

  const boxes = new Map<string, ConnectorBox>();
  for (const node of model.nodes.values()) {
    boxes.set(node.id, boxFor(at(node, drag), NODE_WIDTH, NODE_HEIGHT, xToPx, yToPx, view.pixelsPerUnit));
  }

  for (const frame of model.frames.values()) {
    boxes.set(frame.id, boxFor(at(frame, drag), FRAME_WIDTH, FRAME_HEIGHT, xToPx, yToPx, view.pixelsPerUnit));
  }

  return (
    <div className="databricks-canvas canvas-host">
      <div
        ref={surfaceRef}
        className="databricks-surface canvas-viewport"
        role="application"
        aria-label={ariaLabel}
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
        <svg className="databricks-content canvas-drawing">
          <defs>
            <marker
              id={ARROWHEAD_ID}
              className="databricks-arrowhead canvas-arrowhead"
              viewBox="0 0 10 10"
              refX="9"
              refY="5"
              markerWidth="7"
              markerHeight="7"
              orient="auto-start-reverse"
            >
              <path d="M 0 0 L 10 5 L 0 10 z" />
            </marker>
          </defs>

          {/* Frames first, so everything they enclose paints on top. */}
          {[...model.frames.values()].map((frame) => {
            const box = boxes.get(frame.id)!;
            const badges = [frame.mode, frame.isDefault ? "default" : "", overrides(frame.overrideCount)]
              .filter((badge) => badge.length > 0)
              .join(" · ");
            return (
              <FrameElement
                key={frame.id}
                className={`databricks-frame${frame.id === selectedId ? " databricks-selected" : ""}`}
                data-element-id={frame.id}
                x={box.x}
                y={box.y}
                width={box.width}
                height={box.height}
                label={frame.label}
                labelClassName="databricks-frame-label"
                onMouseDown={(event) => onBoxPointerDown(event, frame.id, at(frame, drag).x, at(frame, drag).y)}
                onContextMenu={(event) => openTargetMenuAt(event, frame.id)}
              >
                {badges ? (
                  <text className="databricks-badges" x={-box.width / 2 + 12} y={-box.height / 2 + 18}>
                    {badges}
                  </text>
                ) : null}
              </FrameElement>
            );
          })}

          {[...model.edges.values()].map((edge) => renderEdge(edge, boxes, selectedId))}

          {connect ? <PendingEdge boxes={boxes} connect={connect} xToPx={xToPx} yToPx={yToPx} /> : null}

          {[...model.nodes.values()].map((node) => {
            const box = boxes.get(node.id)!;
            const classes = ["databricks-node canvas-element", `databricks-node-${node.kind}`];
            if (node.unresolved) {
              classes.push("databricks-node-missing");
            }

            const simulated = simulation.states.get(node.id);
            if (simulated) {
              classes.push(`databricks-sim-${simulated}`);
            }

            if (node.id === selectedId) {
              classes.push("databricks-selected canvas-selected");
            }

            if (connect?.overId === node.id) {
              classes.push("databricks-connect-target canvas-connect-target");
            }

            return (
              <BoxElement
                key={node.id}
                className={classes.join(" ")}
                data-element-id={node.id}
                x={box.x - box.width / 2}
                y={box.y - box.height / 2}
                width={box.width}
                height={box.height}
                label={node.label}
                boxClassName="databricks-node-box canvas-node"
                labelClassName="databricks-label canvas-node-label"
                labelY={box.height / 2 - 4}
                onMouseDown={(event) => onBoxPointerDown(event, node.id, at(node, drag).x, at(node, drag).y)}
                onMouseEnter={() => onBoxPointerEnter(node.id)}
                onMouseLeave={onBoxPointerLeave}
                onContextMenu={(event) => openTargetMenuAt(event, node.id)}
                onDragOver={(event) => event.preventDefault()}
              >
                {node.badges.length > 0 ? (
                  <text className="databricks-badges" x={8} y={box.height - 8}>
                    {node.badges.join(" · ")}
                  </text>
                ) : null}
                {connectable && node.id === selectedId && movable(node.id) ? (
                  <>
                    {/* A visible dot with an invisible fat grab twin - the shared anchor pair. */}
                    <circle className="databricks-anchor canvas-anchor" cx={0} cy={box.height / 2} r={4} />
                    <circle className="databricks-anchor canvas-anchor" cx={box.width} cy={box.height / 2} r={4} />
                    <circle
                      className="databricks-anchor-hit canvas-anchor-hit"
                      cx={0}
                      cy={box.height / 2}
                      r={10}
                      onMouseDown={(event) => onAnchorPointerDown(event, node.id)}
                    />
                    <circle
                      className="databricks-anchor-hit canvas-anchor-hit"
                      cx={box.width}
                      cy={box.height / 2}
                      r={10}
                      onMouseDown={(event) => onAnchorPointerDown(event, node.id)}
                    />
                  </>
                ) : null}
              </BoxElement>
            );
          })}
        </svg>
        <CanvasScrollbars
          {...scrollAxesOf(model, view, surfaceRef.current?.getBoundingClientRect() ?? null)}
          className="databricks-scrollbars"
          onPan={(startX, startY) => setView((current) => ({ ...current, startX, startY }))}
        />
      </div>
      <ContextMenu
        open={menuPosition !== null}
        groups={toMenuGroups(actions, (action) => {
          closeMenu();
          runAction(action.id, selectedId ?? undefined);
        })}
        position={menuPosition ?? { x: 0, y: 0 }}
        onClose={closeMenu}
      />
      {simulation.marker ? (
        <button type="button" className="databricks-simulation-banner" onClick={simulation.dismiss}>
          {simulation.marker} · dismiss
        </button>
      ) : null}
      {loading ? <p className="databricks-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="databricks-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}

/** Whether an id is a box a gesture may start from - the dependency gesture is tasks-only. */
function movable(id: string): boolean {
  return id.startsWith("task:");
}

/**
 * The two scroll axes as the shared bars want them: the view's window, and the content's
 * bounds - nodes and frames alike - padded so a drag can go a little past the content, or the
 * plane stops feeling unbounded. Everything in the module's own canvas units.
 */
function scrollAxesOf(model: DatabricksModel, view: DatabricksView, surface: DOMRect | null) {
  const widthPx = surface?.width || 1200;
  const heightPx = surface?.height || 600;
  const boxes = [
    ...[...model.nodes.values()].map((node) => ({ x: node.x, y: node.y, width: NODE_WIDTH, height: NODE_HEIGHT })),
    ...[...model.frames.values()].map((frame) => ({ x: frame.x, y: frame.y, width: FRAME_WIDTH, height: FRAME_HEIGHT })),
  ];

  const horizontalSpan = widthPx / view.pixelsPerUnit;
  const verticalSpan = heightPx / view.pixelsPerUnit;
  const minX = boxes.length > 0 ? Math.min(...boxes.map((box) => box.x)) : view.startX;
  const maxX = boxes.length > 0 ? Math.max(...boxes.map((box) => box.x + box.width)) : view.startX + horizontalSpan;
  const minY = boxes.length > 0 ? Math.min(...boxes.map((box) => box.y)) : view.startY;
  const maxY = boxes.length > 0 ? Math.max(...boxes.map((box) => box.y + box.height)) : view.startY + verticalSpan;

  return {
    horizontal: {
      viewStart: view.startX,
      viewSpan: horizontalSpan,
      ...scrollExtentOf(minX, maxX, { factor: 0.5, minimumSpan: NODE_WIDTH }),
    },
    vertical: {
      viewStart: view.startY,
      viewSpan: verticalSpan,
      ...scrollExtentOf(minY, maxY, { factor: 0, minimum: 2 * NODE_HEIGHT }),
    },
  };
}

function overrides(count: number): string {
  return count === 0 ? "" : count === 1 ? "1 override" : `${count} overrides`;
}

function at(placed: { id: string; x: number; y: number }, drag: DragPreview | null): { x: number; y: number } {
  return drag && drag.id === placed.id ? { x: drag.x, y: drag.y } : { x: placed.x, y: placed.y };
}

/** A box in pixels, centre-based as the shared geometry expects. */
function boxFor(
  position: { x: number; y: number },
  width: number,
  height: number,
  xToPx: (x: number) => number,
  yToPx: (y: number) => number,
  pixelsPerUnit: number,
): ConnectorBox {
  const w = Math.max(width * pixelsPerUnit, 2);
  const h = Math.max(height * pixelsPerUnit, 2);
  return {
    x: xToPx(position.x) + w / 2,
    y: yToPx(position.y) + h / 2,
    width: w,
    height: h,
  };
}

/**
 * One edge, drawn per its kind: dependencies and flow lines leave the source's right edge and
 * curve into the target's left (the layered-layout case), overrides run straight from a target
 * frame to the resource it overrides. An edge whose end is missing draws nothing - a dangling
 * reference is the validator's to report.
 */
function renderEdge(edge: DatabricksEdge, boxes: Map<string, ConnectorBox>, selectedId: string | null) {
  const from = boxes.get(edge.fromElementId);
  const to = boxes.get(edge.toElementId);
  if (!from || !to) {
    return null;
  }

  if (edge.kind === "override") {
    return (
      <StraightConnection
        key={edge.id}
        from={from}
        to={to}
        className={`databricks-override${edge.id === selectedId ? " databricks-selected canvas-selected" : ""}`}
        pathClassName="databricks-override-line canvas-connection-line"
        markerEnd={`url(#${ARROWHEAD_ID})`}
      />
    );
  }

  const start: Point = { x: from.x + from.width / 2, y: from.y };
  const end: Point = { x: to.x - to.width / 2, y: to.y };
  const classes = [`databricks-edge canvas-connection-line databricks-edge-${edge.kind}`];
  if (edge.outcome === "true") {
    classes.push("databricks-edge-outcome-true");
  } else if (edge.outcome === "false") {
    classes.push("databricks-edge-outcome-false");
  }

  if (edge.id === selectedId) {
    classes.push("databricks-selected canvas-selected");
  }

  return (
    <g key={edge.id} data-element-id={edge.id}>
      <FixedBezierConnection
        from={start}
        to={end}
        className={classes.join(" ")}
        markerEnd={`url(#${ARROWHEAD_ID})`}
      />
      {edge.outcome ? (
        <text className="databricks-outcome-label canvas-hint" x={(start.x + end.x) / 2} y={(start.y + end.y) / 2 - 6}>
          {edge.outcome}
        </text>
      ) : null}
    </g>
  );
}

function PendingEdge({
  boxes,
  connect,
  xToPx,
  yToPx,
}: {
  boxes: Map<string, ConnectorBox>;
  connect: ConnectDrag;
  xToPx: (x: number) => number;
  yToPx: (y: number) => number;
}) {
  const from = boxes.get(connect.fromId);
  if (!from) {
    return null;
  }

  const end = connect.overId && boxes.has(connect.overId)
    ? { x: boxes.get(connect.overId)!.x - boxes.get(connect.overId)!.width / 2, y: boxes.get(connect.overId)!.y }
    : { x: xToPx(connect.x), y: yToPx(connect.y) };
  const start: Point = { x: from.x + from.width / 2, y: from.y };

  return (
    <path
      className="databricks-pending-edge canvas-pending-connection"
      d={`M ${start.x} ${start.y} C ${start.x + 30} ${start.y}, ${end.x - 30} ${end.y}, ${end.x} ${end.y}`}
    />
  );
}

function clampZoom(pixelsPerUnit: number): number {
  return Math.min(MAX_PIXELS_PER_UNIT, Math.max(MIN_PIXELS_PER_UNIT, pixelsPerUnit));
}
