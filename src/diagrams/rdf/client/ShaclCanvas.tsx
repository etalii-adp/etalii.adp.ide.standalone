import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { StraightConnection } from "@client/canvas/connections/straight/StraightConnection";
import { CanvasScrollbars } from "@client/canvas/scroll/CanvasScrollbars";
import { scrollExtentOf } from "@client/canvas/scroll/scrollGeometry";
import type { ConnectorBox } from "@client/canvas/connectors";
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
import { useShaclStream } from "./useShaclStream";
import { shapeHeight, targetWords, type ShaclEdge, type ShaclModel } from "./shaclModel";

/** A card's drawn width, in the module's own canvas units - matching the layout's column pitch. */
export const CARD_WIDTH = 260;

const HEADER_HEIGHT = 30;
const LINE_HEIGHT = 22;


/** The id of the arrowhead marker this reading defines. */
const ARROWHEAD_ID = "shacl-arrowhead";

/** Zoom limits, in pixels per canvas unit. */
const MIN_PIXELS_PER_UNIT = 0.05;
const MAX_PIXELS_PER_UNIT = 8;
const ZOOM_STEP = 1.25;

interface ShaclView {
  startX: number;
  startY: number;
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
 * The shapes graph: node shapes as cards, their targets as chips and their property shapes as
 * constraint rows, references between shapes as labeled edges - drawn through the central canvas
 * library (shacl-diagram Requirements 1, 3).
 *
 * Two things this canvas deliberately does NOT draw. A target's data end: the data a shapes
 * graph aims at lives in another file, so a chip states what a shape targets and no edge leaves
 * the card - a chip whose term is absent from the file renders exactly like one whose term is
 * present, because absence is this medium's normal case. And a validation result: nothing here
 * runs a shape against anything (Requirement 4).
 *
 * There is also no connect gesture. A reference between shapes is stated in the file as
 * `sh:node` or a logical combinator, so it is drawn rather than drawn-on; the menu's own verbs
 * are how one is added.
 */
export function ShaclCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, moveElementTo } = useShaclStream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const surfaceRef = useRef<HTMLDivElement | null>(null);

  useRegisterDiagramToolbox(useToolboxItems(projectId, path));

  const [view, setView] = useState<ShaclView>(() => ({ startX: -60, startY: -60, pixelsPerUnit: 1 }));
  const fittedRef = useRef(false);

  const panRef = useRef<{ clientX: number; clientY: number; view: ShaclView; moved: boolean } | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const [drag, setDrag] = useState<DragPreview | null>(null);
  const [rejection, setRejection] = useState("");

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  const { menuPosition, openMenuAt, closeMenu } = useElementContextMenu(selectionKey, actions.length > 0, (id) =>
    select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
  );

  /** Fits everything into view, with a margin. */
  const fitToView = useCallback(() => {
    const surface = surfaceRef.current;
    const shapes = [...model.shapes.values()];
    if (!surface || shapes.length === 0) {
      return;
    }

    const minX = Math.min(...shapes.map((shape) => shape.x));
    const maxX = Math.max(...shapes.map((shape) => shape.x + CARD_WIDTH));
    const span = Math.max(maxX - minX, CARD_WIDTH);
    const width = surface.getBoundingClientRect().width || 1200;

    setView({
      startX: minX - span * 0.1,
      startY: Math.min(...shapes.map((shape) => shape.y)) - HEADER_HEIGHT,
      pixelsPerUnit: clampZoom(width / (span * 1.2)),
    });
  }, [model]);

  // Fitted once, when the first delta lands - refitting on every edit would fight the user's pan.
  useEffect(() => {
    if (!loading && !fittedRef.current && model.shapes.size > 0) {
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

  // Escape abandons a drag in flight, dispatching nothing.
  useEffect(() => {
    const onKeyDownWindow = (event: KeyboardEvent) => {
      if (event.key !== "Escape") {
        return;
      }

      dragRef.current = null;
      setDrag(null);
    };

    window.addEventListener("keydown", onKeyDownWindow);
    return () => window.removeEventListener("keydown", onKeyDownWindow);
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
    if (target !== event.currentTarget && !target.classList?.contains("shacl-content")) {
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
      // The authored position, raw. An anonymous shape's refusal comes back from the backend
      // with its sentence - the canvas does not decide it, it reports it (Requirement 3.2).
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
    }
  };

  const openTargetMenuAt = (event: React.MouseEvent, id: string) => {
    if (panRef.current?.moved) {
      return;
    }

    openMenuAt(event, id);
  };

  /** A toolbox entry dropped anywhere on the canvas names a placement - `new:{x},{y}`. */
  const onSurfaceDrop = (event: React.DragEvent) => {
    const actionId = event.dataTransfer.getData(TOOLBOX_DRAG_TYPE);
    if (!actionId) {
      return;
    }

    event.preventDefault();
    const under = (event.target as Element | null)?.closest?.("[data-element-id]");
    const overId = under?.getAttribute("data-element-id");

    // A row entry dropped on a card acts on that card; anything else lands as a placement.
    runAction(actionId, overId ? overId : `new:${toUnitsX(event.clientX)},${toUnitsY(event.clientY)}`);
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
      <div className="shacl-canvas canvas-host shacl-canvas-message canvas-host-message">
        <p>This diagram could not be opened.</p>
      </div>
    );
  }

  const boxes = new Map<string, ConnectorBox>();
  for (const shape of model.shapes.values()) {
    boxes.set(shape.id, boxFor(at(shape, drag), CARD_WIDTH, shapeHeight(shape), xToPx, yToPx, view.pixelsPerUnit));
  }

  return (
    <div className="shacl-canvas canvas-host">
      <div
        ref={surfaceRef}
        className="shacl-surface canvas-viewport"
        role="application"
        aria-label="SHACL shapes"
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
        <svg className="shacl-content canvas-drawing">
          <defs>
            <marker
              id={ARROWHEAD_ID}
              className="shacl-arrowhead canvas-arrowhead"
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

          {[...model.edges.values()].map((edge) => renderEdge(edge, boxes, selectedId))}

          {[...model.shapes.values()].map((shape) => {
            const box = boxes.get(shape.id)!;
            const classes = ["shacl-shape canvas-element"];
            if (shape.blank) {
              classes.push("shacl-shape-blank");
            }

            if (shape.deactivated) {
              classes.push("shacl-shape-deactivated");
            }

            if (shape.id === selectedId) {
              classes.push("shacl-selected canvas-selected");
            }

            const badges = [
              shape.deactivated ? "deactivated" : "",
              shape.closed ? "closed" : "",
              shape.severity,
            ].filter((badge) => badge.length > 0);

            return (
              <BoxElement
                key={shape.id}
                className={classes.join(" ")}
                data-element-id={shape.id}
                x={box.x - box.width / 2}
                y={box.y - box.height / 2}
                width={box.width}
                height={box.height}
                label={shape.name.length > 0 ? `${shape.display} — ${shape.name}` : shape.display}
                boxClassName="shacl-shape-box canvas-node"
                labelClassName="shacl-label canvas-node-label"
                labelY={(HEADER_HEIGHT / 2 + 5) * view.pixelsPerUnit}
                onMouseDown={(event) => onBoxPointerDown(event, shape.id, at(shape, drag).x, at(shape, drag).y)}
                onContextMenu={(event) => openTargetMenuAt(event, shape.id)}
                onDragOver={(event) => event.preventDefault()}
              >
                {badges.length > 0 ? (
                  <text
                    className="shacl-badges canvas-hint"
                    x={(CARD_WIDTH - 8) * view.pixelsPerUnit}
                    y={(HEADER_HEIGHT / 2 + 5) * view.pixelsPerUnit}
                    textAnchor="end"
                  >
                    {badges.join(" · ")}
                  </text>
                ) : null}

                {shape.targets.map((target, index) => (
                  <text
                    key={`target-${index}`}
                    className="shacl-target"
                    x={8 * view.pixelsPerUnit}
                    y={(HEADER_HEIGHT + (index + 1) * LINE_HEIGHT - 6) * view.pixelsPerUnit}
                  >
                    {targetWords(target)}
                  </text>
                ))}

                {shape.rows.map((row, index) => {
                  const y = (HEADER_HEIGHT + (shape.targets.length + index + 1) * LINE_HEIGHT - 6) * view.pixelsPerUnit;
                  return (
                    <g key={`row-${index}`} className={row.sparql ? "shacl-row shacl-row-sparql" : "shacl-row"}>
                      <text className="shacl-row-path" x={8 * view.pixelsPerUnit} y={y}>
                        {row.sparql ? "SPARQL constraint" : row.path}
                      </text>
                      <text
                        className="shacl-row-cardinality canvas-hint"
                        x={(CARD_WIDTH - 8) * view.pixelsPerUnit}
                        y={y}
                        textAnchor="end"
                      >
                        {row.cardinality}
                      </text>
                      {row.summary.length > 0 ? (
                        <text className="shacl-row-summary canvas-hint" x={110 * view.pixelsPerUnit} y={y}>
                          {row.summary}
                        </text>
                      ) : null}
                    </g>
                  );
                })}
              </BoxElement>
            );
          })}
        </svg>

        <CanvasScrollbars
          {...scrollAxesOf(model, view, surfaceRef.current?.getBoundingClientRect() ?? null)}
          className="shacl-scrollbars"
          onPan={(startX, startY) => setView((current) => ({ ...current, startX, startY }))}
        />
      </div>

      {model.truncation ? (
        <p className="shacl-banner canvas-banner" role="status">
          {`Showing ${model.truncation.shown} of ${model.truncation.total} shapes. Edits are withheld while the view is partial.`}
        </p>
      ) : null}

      {rejection ? (
        <p className="shacl-rejection canvas-rejection" role="alert">
          {rejection}
        </p>
      ) : null}

      {loading && model.shapes.size === 0 ? <p className="shacl-loading canvas-hint">Loading…</p> : null}

      <ContextMenu
        open={menuPosition !== null}
        groups={toMenuGroups(actions, (action) => {
          closeMenu();
          runAction(action.id, selectedId ?? undefined);
        })}
        position={menuPosition ?? { x: 0, y: 0 }}
        onClose={closeMenu}
      />
    </div>
  );
}

/**
 * The two scroll axes as the shared bars want them: the view's window, and the content's
 * bounds, padded so a drag can go a little past the content. Everything in canvas units.
 */
function scrollAxesOf(model: ShaclModel, view: ShaclView, surface: DOMRect | null) {
  const widthPx = surface?.width || 1200;
  const heightPx = surface?.height || 600;
  const shapes = [...model.shapes.values()];

  const horizontalSpan = widthPx / view.pixelsPerUnit;
  const verticalSpan = heightPx / view.pixelsPerUnit;
  const minX = shapes.length > 0 ? Math.min(...shapes.map((shape) => shape.x)) : view.startX;
  const maxX = shapes.length > 0 ? Math.max(...shapes.map((shape) => shape.x + CARD_WIDTH)) : view.startX + horizontalSpan;
  const minY = shapes.length > 0 ? Math.min(...shapes.map((shape) => shape.y)) : view.startY;
  const maxY = shapes.length > 0
    ? Math.max(...shapes.map((shape) => shape.y + shapeHeight(shape)))
    : view.startY + verticalSpan;

  return {
    horizontal: {
      viewStart: view.startX,
      viewSpan: horizontalSpan,
      ...scrollExtentOf(minX, maxX, { factor: 0.5, minimumSpan: CARD_WIDTH }),
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
 * One reference between shapes: a straight labeled line with the shared arrowhead. The label is
 * the operator - `node`, `and`, `or`, `xone`, `not` - or, where the reference came from a
 * property row, that row's path. An edge whose end is missing draws nothing.
 */
function renderEdge(edge: ShaclEdge, boxes: Map<string, ConnectorBox>, selectedId: string | null) {
  const from = boxes.get(edge.fromElementId);
  const to = boxes.get(edge.toElementId);
  if (!from || !to) {
    return null;
  }

  return (
    <g key={edge.id} data-element-id={edge.id}>
      <StraightConnection
        from={from}
        to={to}
        className={`shacl-edge shacl-edge-${edge.kind}${edge.id === selectedId ? " shacl-selected canvas-selected" : ""}`}
        pathClassName="shacl-edge-line canvas-connection-line"
        markerEnd={`url(#${ARROWHEAD_ID})`}
      />
      <text className="shacl-edge-label canvas-hint" x={(from.x + to.x) / 2} y={(from.y + to.y) / 2 - 6}>
        {edge.label}
      </text>
    </g>
  );
}

function clampZoom(pixelsPerUnit: number): number {
  return Math.min(MAX_PIXELS_PER_UNIT, Math.max(MIN_PIXELS_PER_UNIT, pixelsPerUnit));
}

/** A card's height follows its content - the same function the layout uses. */
export { shapeHeight };
