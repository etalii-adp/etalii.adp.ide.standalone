import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
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
import { useRdfStream } from "./useRdfStream";
import { useViewReport } from "@client/diagrams/useViewReport";
import { viewportOf } from "./rdfViewport";
import type { RdfDiagramEdge, RdfModel, RdfNode } from "./rdfModel";

/** A card's drawn width, in the module's own canvas units - matching the backend layout's spacing. */
export const NODE_WIDTH = 220;

/** The card's parts, stacked: title, the type badges, then one line per literal row. */
const HEADER_HEIGHT = 30;
const BADGES_HEIGHT = 16;
const ROW_HEIGHT = 16;
const FOOTER_PADDING = 10;

/** The id of the arrowhead marker this module defines and its edge stylesheet points at. */
const ARROWHEAD_ID = "rdf-arrowhead";

/** Zoom limits, in pixels per canvas unit. */
const MIN_PIXELS_PER_UNIT = 0.05;
const MAX_PIXELS_PER_UNIT = 8;
const ZOOM_STEP = 1.25;

interface RdfView {
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

/** A card's height follows its content: title, badges when worn, one line per row. */
export function nodeHeightOf(node: RdfNode): number {
  return HEADER_HEIGHT
    + (node.typeBadges.length > 0 ? BADGES_HEIGHT : 0)
    + node.rows.length * ROW_HEIGHT
    + FOOTER_PADDING;
}

/**
 * The data graph: resource cards with literal rows and type badges, straight labeled edges, and
 * the full interaction set - drag to reposition through the layout path (blank nodes refused
 * backend-side), anchor drags for new statements, toolbox drops via placement ids, the shared
 * context menu - drawn through the central canvas library (rdf-diagram Requirements 3, 4, 6).
 *
 * A drag never writes the RDF file: `moveElementTo` lands in the registration's `layout:` block
 * as one undoable command. When the drawn-element budget cut the graph, the showing-N-of-M
 * banner says so (Requirement 8.2).
 */
export function RdfCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, reportView, moveElementTo } = useRdfStream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const surfaceRef = useRef<HTMLDivElement | null>(null);

  useRegisterDiagramToolbox(useToolboxItems(projectId, path));

  const [view, setView] = useState<RdfView>(() => ({ startX: -60, startY: -60, pixelsPerUnit: 1 }));

  const viewRef = useRef(view);
  viewRef.current = view;

  // What the reader can see, reported once it settles and on every later change. The report
  // observes the view rather than being wired to any one gesture, so a pan, a scrollbar thumb,
  // a zoom and a programmatic reveal all reach the backend by the same path.
  useViewReport({
    view: { x: view.startX, y: view.startY, w: view.pixelsPerUnit, h: view.pixelsPerUnit },
    report: reportView,
    convert: () => viewportOf(viewRef.current, surfaceRef.current?.getBoundingClientRect() ?? null),
    ready: !loading && !failed,
  });
  const fittedRef = useRef(false);

  const panRef = useRef<{ clientX: number; clientY: number; view: RdfView; moved: boolean } | null>(null);
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
    const nodes = [...model.nodes.values()];
    if (!surface || nodes.length === 0) {
      return;
    }

    const minX = Math.min(...nodes.map((node) => node.x));
    const maxX = Math.max(...nodes.map((node) => node.x + NODE_WIDTH));
    const span = Math.max(maxX - minX, NODE_WIDTH);
    const width = surface.getBoundingClientRect().width || 1200;

    setView({
      startX: minX - span * 0.1,
      startY: Math.min(...nodes.map((node) => node.y)) - HEADER_HEIGHT,
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

  const runAction = (actionId: string, sourceId?: string) => {
    void (async () => {
      const outcome = await executeAction(actionId, sourceId ? elementSourceOf(sourceId) : undefined);
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const onSurfacePointerDown = (event: React.MouseEvent) => {
    const target = event.target as Element;
    if (target !== event.currentTarget && !target.classList?.contains("rdf-content")) {
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
      // rounding it here would quietly turn the canvas into a grid. A blank node's refusal
      // comes back from the backend with its sentence (Requirement 4.5).
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
        // Released on nothing, or back onto its own source: a "never mind".
        return;
      }

      // The whole gesture in one stateless rel: call; the predicate is asked in a dialog.
      runAction("rdf.connect", `rel:${connecting.fromId}->${targetId}`);
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
   * under the pointer (Requirement 6).
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
      <div className="rdf-canvas canvas-host rdf-canvas-message canvas-host-message">
        <p>This diagram could not be opened.</p>
      </div>
    );
  }

  const boxes = new Map<string, ConnectorBox>();
  for (const node of model.nodes.values()) {
    boxes.set(node.id, boxFor(at(node, drag), NODE_WIDTH, nodeHeightOf(node), xToPx, yToPx, view.pixelsPerUnit));
  }

  return (
    <div className="rdf-canvas canvas-host">
      <div
        ref={surfaceRef}
        className="rdf-surface canvas-viewport"
        role="application"
        aria-label="RDF graph"
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
        <svg className="rdf-content canvas-drawing">
          <defs>
            <marker
              id={ARROWHEAD_ID}
              className="rdf-arrowhead canvas-arrowhead"
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

          {[...model.edges.values()].map((edge) =>
            renderEdge(edge, boxes, selectedId, {
              onMouseDown: (event) => {
                // A click on an edge is a selection, never a drag: an edge has no position of
                // its own, so there is nothing to move - but its menu carries the removal.
                event.stopPropagation();
                setRejection("");
                select(elementSelectionOf(entryId, path, edge.id));
              },
              onContextMenu: (event) => openTargetMenuAt(event, edge.id),
            }),
          )}

          {connect ? <PendingEdge boxes={boxes} connect={connect} xToPx={xToPx} yToPx={yToPx} /> : null}

          {[...model.nodes.values()].map((node) => {
            const box = boxes.get(node.id)!;
            const classes = ["rdf-node canvas-element"];
            if (node.blank) {
              classes.push("rdf-node-blank");
            }

            if (node.id === selectedId) {
              classes.push("rdf-selected canvas-selected");
            }

            if (connect?.overId === node.id) {
              classes.push("rdf-connect-target canvas-connect-target");
            }

            const badgesY = HEADER_HEIGHT - 6 + BADGES_HEIGHT;
            const rowsStart = HEADER_HEIGHT + (node.typeBadges.length > 0 ? BADGES_HEIGHT : 0);
            return (
              <BoxElement
                key={node.id}
                className={classes.join(" ")}
                data-element-id={node.id}
                x={box.x - box.width / 2}
                y={box.y - box.height / 2}
                width={box.width}
                height={box.height}
                label={node.display}
                boxClassName="rdf-node-box canvas-node"
                labelClassName="rdf-label canvas-node-label"
                labelY={(HEADER_HEIGHT / 2 + 5) * view.pixelsPerUnit}
                onMouseDown={(event) => onBoxPointerDown(event, node.id, at(node, drag).x, at(node, drag).y)}
                onMouseEnter={() => onBoxPointerEnter(node.id)}
                onMouseLeave={onBoxPointerLeave}
                onContextMenu={(event) => openTargetMenuAt(event, node.id)}
                onDragOver={(event) => event.preventDefault()}
              >
                {node.typeBadges.length > 0 ? (
                  <text className="rdf-badges" x={8 * view.pixelsPerUnit} y={badgesY * view.pixelsPerUnit}>
                    {node.typeBadges.join(" · ")}
                  </text>
                ) : null}
                {node.rows.map((row, index) => (
                  <text
                    key={`${row.predicate}-${index}`}
                    className="rdf-row"
                    x={8 * view.pixelsPerUnit}
                    y={(rowsStart + (index + 1) * ROW_HEIGHT - 4) * view.pixelsPerUnit}
                  >
                    {`${row.predicate}: ${row.value}${row.annotation ? ` ${row.annotation}` : ""}`}
                  </text>
                ))}
                {!node.blank && node.id === selectedId ? (
                  <>
                    {/* A visible dot with an invisible fat grab twin - the shared anchor pair. */}
                    <circle className="rdf-anchor canvas-anchor" cx={0} cy={box.height / 2} r={4} />
                    <circle className="rdf-anchor canvas-anchor" cx={box.width} cy={box.height / 2} r={4} />
                    <circle
                      className="rdf-anchor-hit canvas-anchor-hit"
                      cx={0}
                      cy={box.height / 2}
                      r={10}
                      onMouseDown={(event) => onAnchorPointerDown(event, node.id)}
                    />
                    <circle
                      className="rdf-anchor-hit canvas-anchor-hit"
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
          className="rdf-scrollbars"
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
      {model.truncation ? (
        <p className="rdf-truncation-banner">
          {`Showing ${model.truncation.shown} of ${model.truncation.total} resources — edits are withheld on this truncated view`}
        </p>
      ) : null}
      {loading ? <p className="rdf-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="rdf-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}

/**
 * The two scroll axes as the shared bars want them: the view's window, and the content's
 * bounds, padded so a drag can go a little past the content. Everything in canvas units.
 */
function scrollAxesOf(model: RdfModel, view: RdfView, surface: DOMRect | null) {
  const widthPx = surface?.width || 1200;
  const heightPx = surface?.height || 600;
  const nodes = [...model.nodes.values()];

  const horizontalSpan = widthPx / view.pixelsPerUnit;
  const verticalSpan = heightPx / view.pixelsPerUnit;
  const minX = nodes.length > 0 ? Math.min(...nodes.map((node) => node.x)) : view.startX;
  const maxX = nodes.length > 0 ? Math.max(...nodes.map((node) => node.x + NODE_WIDTH)) : view.startX + horizontalSpan;
  const minY = nodes.length > 0 ? Math.min(...nodes.map((node) => node.y)) : view.startY;
  const maxY = nodes.length > 0 ? Math.max(...nodes.map((node) => node.y + nodeHeightOf(node))) : view.startY + verticalSpan;

  return {
    horizontal: {
      viewStart: view.startX,
      viewSpan: horizontalSpan,
      ...scrollExtentOf(minX, maxX, { factor: 0.5, minimumSpan: NODE_WIDTH }),
    },
    vertical: {
      viewStart: view.startY,
      viewSpan: verticalSpan,
      ...scrollExtentOf(minY, maxY, { factor: 0, minimum: 2 * HEADER_HEIGHT }),
    },
  };
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
 * One edge: a straight labeled line with the shared arrowhead - the any-direction graph case
 * the straight connection exists for. An edge whose end is missing draws nothing. The handlers
 * make the edge selectable: found in the first manual pass, where the removal Requirement 6
 * promises was unreachable because nothing on the canvas could select an edge.
 */
function renderEdge(
  edge: RdfDiagramEdge,
  boxes: Map<string, ConnectorBox>,
  selectedId: string | null,
  handlers: {
    onMouseDown: (event: React.MouseEvent) => void;
    onContextMenu: (event: React.MouseEvent) => void;
  },
) {
  const from = boxes.get(edge.fromElementId);
  const to = boxes.get(edge.toElementId);
  if (!from || !to) {
    return null;
  }

  return (
    <g
      key={edge.id}
      data-element-id={edge.id}
      onMouseDown={handlers.onMouseDown}
      onContextMenu={handlers.onContextMenu}
    >
      {/* The invisible fat grab twin the shared class defines - a thin stroke is no target. */}
      <path className="rdf-edge-hit canvas-connection-hit" d={`M ${from.x} ${from.y} L ${to.x} ${to.y}`} />
      <StraightConnection
        from={from}
        to={to}
        className={`rdf-edge${edge.id === selectedId ? " rdf-selected canvas-selected" : ""}`}
        pathClassName="rdf-edge-line canvas-connection-line"
        markerEnd={`url(#${ARROWHEAD_ID})`}
      />
      <text className="rdf-edge-label canvas-hint" x={(from.x + to.x) / 2} y={(from.y + to.y) / 2 - 6}>
        {edge.predicate}
      </text>
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
      className="rdf-pending-edge canvas-pending-connection"
      d={`M ${start.x} ${start.y} C ${start.x + 30} ${start.y}, ${end.x - 30} ${end.y}, ${end.x} ${end.y}`}
    />
  );
}

function clampZoom(pixelsPerUnit: number): number {
  return Math.min(MAX_PIXELS_PER_UNIT, Math.max(MIN_PIXELS_PER_UNIT, pixelsPerUnit));
}
