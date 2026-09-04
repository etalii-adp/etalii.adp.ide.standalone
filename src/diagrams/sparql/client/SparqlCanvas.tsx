import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { FrameElement } from "@client/canvas/elements/frame/FrameElement";
import { StraightConnection } from "@client/canvas/connections/straight/StraightConnection";
import { CanvasScrollbars } from "@client/canvas/scroll/CanvasScrollbars";
import { scrollExtentOf } from "@client/canvas/scroll/scrollGeometry";
import type { ConnectorBox } from "@client/canvas/connectors";
import { elementIdOfKey, elementSelectionOf } from "@client/canvas/selection";
import { useElementContextMenu } from "@client/canvas/useElementContextMenu";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { useRegisterDiagramView } from "@client/shell/panels/DiagramViewContext";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { useViewReport } from "@client/diagrams/useViewReport";
import type { Viewport } from "@client/diagrams/viewReport";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { useSparqlStream } from "./useSparqlStream";
import type { SparqlDiagramEdge, SparqlModel } from "./sparqlModel";

/** A node's drawn size, in the module's own canvas units - matching the backend layout's spacing. */
export const NODE_WIDTH = 170;
export const NODE_HEIGHT = 64;

/** The id of the arrowhead marker this module defines and its edge stylesheet points at. */
const ARROWHEAD_ID = "sparql-arrowhead";

/** Zoom limits, in pixels per canvas unit. */
const MIN_PIXELS_PER_UNIT = 0.05;
const MAX_PIXELS_PER_UNIT = 8;
const ZOOM_STEP = 1.25;

/** What the viewport spans when the surface has not been laid out yet - jsdom, and the first frame. */
const FALLBACK_WIDTH_PX = 1200;
const FALLBACK_HEIGHT_PX = 400;

interface SparqlView {
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

/**
 * The query as the joins it is made of: one node per variable however many patterns mention it,
 * concrete terms and literals told apart from what is being asked for, group constructs as
 * labeled containment frames, FILTER/BIND/VALUES as badges showing their text as written, and
 * the form and modifiers as a header band - drawn through the central canvas library
 * (Requirements 3 and 4).
 *
 * <b>Nothing here can edit the query.</b> The backend offers no mutating action and no toolbox,
 * so this canvas registers no toolbox, no drop target and no connect gesture - there is exactly
 * one gesture, a drag, and it lands in the registration's `layout:` block through
 * `moveElementTo`. Refusals come back from the backend with their own sentences (Requirement 5.4).
 */
export function SparqlCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useSparqlStream(projectId, path);
  const { select } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const surfaceRef = useRef<HTMLDivElement | null>(null);

  const [view, setView] = useState<SparqlView>(() => ({ startX: -40, startY: -40, pixelsPerUnit: 1 }));
  const fittedRef = useRef(false);

  const panRef = useRef<{ clientX: number; clientY: number; view: SparqlView; moved: boolean } | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const [drag, setDrag] = useState<DragPreview | null>(null);
  const [rejection, setRejection] = useState("");

  /**
   * The rectangle this canvas is showing, in the module's own units. A pixels-per-unit canvas
   * converts at its own call site - the shared code converts nothing (Requirement 3.4).
   */
  const viewportOf = useCallback((): Viewport => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    const widthPx = rect?.width || FALLBACK_WIDTH_PX;
    const heightPx = rect?.height || FALLBACK_HEIGHT_PX;
    return {
      minX: view.startX,
      minY: view.startY,
      maxX: view.startX + widthPx / view.pixelsPerUnit,
      maxY: view.startY + heightPx / view.pixelsPerUnit,
    };
  }, [view]);

  // Reported once the view settles. The rectangle is what is keyed on, so a pan and a zoom
  // that happen to show the same span report once, and a resize that changes nothing else is
  // still reported because the measured surface is part of the rectangle.
  const reported = viewportOf();
  useViewReport({
    view: {
      x: reported.minX,
      y: reported.minY,
      w: reported.maxX - reported.minX,
      h: reported.maxY - reported.minY,
    },
    report: reportView,
    convert: viewportOf,
    ready: !loading && !failed,
  });

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  const { menuPosition, openMenuAt, closeMenu } = useElementContextMenu(selectionKey, actions.length > 0, (id) =>
    select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
  );

  /** Fits everything into view, with a margin. */
  const fitToView = useCallback(() => {
    const surface = surfaceRef.current;
    const bounds = boundsOf(model);
    if (!surface || !bounds) {
      return;
    }

    const span = Math.max(bounds.maxX - bounds.minX, NODE_WIDTH);
    const width = surface.getBoundingClientRect().width || 1200;

    setView({
      startX: bounds.minX - span * 0.1,
      startY: bounds.minY - NODE_HEIGHT,
      pixelsPerUnit: clampZoom(width / (span * 1.2)),
    });
  }, [model]);

  // Fitted once, when the first delta lands - refitting on every change would fight the user's pan.
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

  // Escape abandons the drag in flight, dispatching nothing.
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") {
        return;
      }

      dragRef.current = null;
      setDrag(null);
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  const xToPx = (x: number): number => (x - view.startX) * view.pixelsPerUnit;
  const yToPx = (y: number): number => (y - view.startY) * view.pixelsPerUnit;

  const onSurfacePointerDown = (event: React.MouseEvent) => {
    const target = event.target as Element;
    if (target !== event.currentTarget && !target.classList?.contains("sparql-content")) {
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

  const onPointerUp = () => {
    panRef.current = null;

    const dragging = dragRef.current;
    const landed = drag;
    dragRef.current = null;
    setDrag(null);

    if (dragging && landed && dragging.moved) {
      // The authored position, raw - and the only thing this canvas can send. An anonymous
      // variable's refusal comes back from the backend with its sentence (Requirement 5.4).
      void (async () => {
        const error = await moveElementTo(landed.id, landed.x, landed.y);
        if (error) {
          setRejection(error);
        }
      })();
      return;
    }

    if (dragging && !dragging.moved) {
      // A press with no movement is a click: a selection, and this diagram has nothing else.
      select(elementSelectionOf(entryId, path, dragging.id));
    }
  };

  const openTargetMenuAt = (event: React.MouseEvent, id: string) => {
    if (panRef.current?.moved) {
      return;
    }

    openMenuAt(event, id);
  };

  if (failed) {
    return (
      <div className="sparql-canvas canvas-host sparql-canvas-message canvas-host-message">
        <p>This query could not be opened.</p>
      </div>
    );
  }

  const boxes = new Map<string, ConnectorBox>();
  for (const node of model.nodes.values()) {
    boxes.set(node.id, boxFor(at(node, drag), NODE_WIDTH, NODE_HEIGHT, xToPx, yToPx, view.pixelsPerUnit));
  }

  return (
    <div className="sparql-canvas canvas-host">
      {model.header ? (
        <header className="sparql-header-band">
          <span className="sparql-header-form">{model.header.form}</span>
          {model.header.modifierRows.map((row) => (
            <span key={row} className="sparql-header-modifier">
              {row}
            </span>
          ))}
        </header>
      ) : null}
      <div
        ref={surfaceRef}
        className="sparql-surface canvas-viewport"
        role="application"
        aria-label="SPARQL query"
        tabIndex={0}
        onMouseDown={onSurfacePointerDown}
        onMouseMove={onPointerMove}
        onMouseUp={onPointerUp}
        onMouseLeave={onPointerUp}
        onContextMenu={onSurfaceContextMenu}
      >
        <svg className="sparql-content canvas-drawing">
          <defs>
            <marker
              id={ARROWHEAD_ID}
              className="sparql-arrowhead canvas-arrowhead"
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

          {/* Regions first, so every frame paints behind the patterns it contains. */}
          {[...model.regions.values()].map((region) => (
            <FrameElement
              key={region.id}
              className={`sparql-region sparql-region-${region.kind} canvas-element${
                region.id === selectedId ? " sparql-selected canvas-selected" : ""
              }`}
              data-element-id={region.id}
              x={xToPx(region.x) + (region.width * view.pixelsPerUnit) / 2}
              y={yToPx(region.y) + (region.height * view.pixelsPerUnit) / 2}
              width={region.width * view.pixelsPerUnit}
              height={region.height * view.pixelsPerUnit}
              label={region.label}
              labelClassName="sparql-region-label canvas-hint"
              onMouseDown={(event) => onBoxPointerDown(event, region.id, region.x, region.y)}
              onContextMenu={(event) => openTargetMenuAt(event, region.id)}
            />
          ))}

          {[...model.edges.values()].map((edge) =>
            renderEdge(edge, boxes, selectedId, {
              onMouseDown: (event) => {
                // An edge has no position of its own, so a click on one is a selection.
                event.stopPropagation();
                setRejection("");
                select(elementSelectionOf(entryId, path, edge.id));
              },
              onContextMenu: (event) => openTargetMenuAt(event, edge.id),
            }),
          )}

          {[...model.nodes.values()].map((node) => {
            const box = boxes.get(node.id)!;
            const classes = ["sparql-node canvas-element", `sparql-node-${node.kind}`];
            if (node.projected) {
              classes.push("sparql-node-projected");
            }

            if (node.id === selectedId) {
              classes.push("sparql-selected canvas-selected");
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
                label={node.display}
                boxClassName="sparql-node-box canvas-node"
                labelClassName="sparql-label canvas-node-label"
                labelY={(NODE_HEIGHT / 2 + 2) * view.pixelsPerUnit}
                onMouseDown={(event) => onBoxPointerDown(event, node.id, at(node, drag).x, at(node, drag).y)}
                onContextMenu={(event) => openTargetMenuAt(event, node.id)}
              >
                {node.projected ? (
                  // The mark that says "this leaves the query", without consulting the header.
                  <text className="sparql-projection-mark" x={8 * view.pixelsPerUnit} y={16 * view.pixelsPerUnit}>
                    →
                  </text>
                ) : null}
                {node.annotation ? (
                  <text
                    className="sparql-node-annotation"
                    x={8 * view.pixelsPerUnit}
                    y={(NODE_HEIGHT - 10) * view.pixelsPerUnit}
                  >
                    {node.annotation}
                  </text>
                ) : null}
              </BoxElement>
            );
          })}

          {/* Badges last, so an annotation is never painted over by what it annotates. */}
          {[...model.annotations.values()].map((annotation) => {
            const anchor = anchorFor(annotation.attachedTo, boxes, model, xToPx, yToPx, view.pixelsPerUnit);
            if (!anchor) {
              return null;
            }

            return (
              <text
                key={annotation.id}
                className={`sparql-annotation sparql-annotation-${annotation.kind} canvas-hint`}
                data-element-id={annotation.id}
                x={anchor.x}
                y={anchor.y}
                onMouseDown={(event) => {
                  event.stopPropagation();
                  setRejection("");
                  select(elementSelectionOf(entryId, path, annotation.id));
                }}
                onContextMenu={(event) => openTargetMenuAt(event, annotation.id)}
              >
                {annotation.text}
              </text>
            );
          })}
        </svg>
        <CanvasScrollbars
          {...scrollAxesOf(model, view, surfaceRef.current?.getBoundingClientRect() ?? null)}
          className="sparql-scrollbars"
          onPan={(startX, startY) => setView((current) => ({ ...current, startX, startY }))}
        />
      </div>
      <ContextMenu
        open={menuPosition !== null}
        groups={toMenuGroups(actions, () => closeMenu())}
        position={menuPosition ?? { x: 0, y: 0 }}
        onClose={closeMenu}
      />
      {model.truncation ? (
        <p className="sparql-truncation-banner">
          {`Showing ${model.truncation.shown} of ${model.truncation.total} elements — this query is larger than the diagram draws`}
        </p>
      ) : null}
      {loading ? <p className="sparql-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="sparql-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}

/** Where a badge sits: on its anchor element, or floating at the canvas origin when it has none. */
function anchorFor(
  attachedTo: string,
  boxes: Map<string, ConnectorBox>,
  model: SparqlModel,
  xToPx: (x: number) => number,
  yToPx: (y: number) => number,
  pixelsPerUnit: number,
): { x: number; y: number } | null {
  const box = boxes.get(attachedTo);
  if (box) {
    return { x: box.x - box.width / 2, y: box.y - box.height / 2 - 6 };
  }

  const region = model.regions.get(attachedTo);
  if (region) {
    return { x: xToPx(region.x) + 12 * pixelsPerUnit, y: yToPx(region.y) + 18 * pixelsPerUnit };
  }

  // A root-scope filter constrains the whole query and floats above its patterns.
  return { x: xToPx(0), y: yToPx(0) - 10 * pixelsPerUnit };
}

/** Everything drawn, in canvas units - nodes and region frames alike. */
function boundsOf(model: SparqlModel): { minX: number; minY: number; maxX: number; maxY: number } | null {
  const xs: number[] = [];
  const ys: number[] = [];
  const rights: number[] = [];
  const bottoms: number[] = [];

  for (const node of model.nodes.values()) {
    xs.push(node.x);
    ys.push(node.y);
    rights.push(node.x + NODE_WIDTH);
    bottoms.push(node.y + NODE_HEIGHT);
  }

  for (const region of model.regions.values()) {
    xs.push(region.x);
    ys.push(region.y);
    rights.push(region.x + region.width);
    bottoms.push(region.y + region.height);
  }

  if (xs.length === 0) {
    return null;
  }

  return { minX: Math.min(...xs), minY: Math.min(...ys), maxX: Math.max(...rights), maxY: Math.max(...bottoms) };
}

/**
 * The two scroll axes as the shared bars want them: the view's window, and the content's
 * bounds, padded so a drag can go a little past the content. Everything in canvas units.
 */
function scrollAxesOf(model: SparqlModel, view: SparqlView, surface: DOMRect | null) {
  const widthPx = surface?.width || 1200;
  const heightPx = surface?.height || 600;
  const horizontalSpan = widthPx / view.pixelsPerUnit;
  const verticalSpan = heightPx / view.pixelsPerUnit;
  const bounds = boundsOf(model);

  const minX = bounds?.minX ?? view.startX;
  const maxX = bounds?.maxX ?? view.startX + horizontalSpan;
  const minY = bounds?.minY ?? view.startY;
  const maxY = bounds?.maxY ?? view.startY + verticalSpan;

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
 * One triple pattern: a straight labeled line with the shared arrowhead. The label is the
 * predicate or the whole property path as written, and a path is marked so a reader can tell a
 * multi-step ask from a single predicate (Requirement 3.1).
 */
function renderEdge(
  edge: SparqlDiagramEdge,
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
      <path className="sparql-edge-hit canvas-connection-hit" d={`M ${from.x} ${from.y} L ${to.x} ${to.y}`} />
      <StraightConnection
        from={from}
        to={to}
        className={`sparql-edge${edge.isPath ? " sparql-edge-path" : ""}${
          edge.id === selectedId ? " sparql-selected canvas-selected" : ""
        }`}
        pathClassName="sparql-edge-line canvas-connection-line"
        markerEnd={`url(#${ARROWHEAD_ID})`}
      />
      <text className="sparql-edge-label canvas-hint" x={(from.x + to.x) / 2} y={(from.y + to.y) / 2 - 6}>
        {edge.label}
      </text>
    </g>
  );
}

function clampZoom(pixelsPerUnit: number): number {
  return Math.min(MAX_PIXELS_PER_UNIT, Math.max(MIN_PIXELS_PER_UNIT, pixelsPerUnit));
}
