import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { facingAnchorsBetween, horizontalBezierPath, sideAnchorOf, type ConnectorBox } from "@client/canvas/connectors";
import { InteractiveBezierConnection } from "@client/canvas/connections/interactive-bezier/InteractiveBezierConnection";
import { SpanElement, type SpanElementClasses } from "@client/canvas/elements/span/SpanElement";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { useElementContextMenu } from "@client/canvas/useElementContextMenu";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { TOOLBOX_DRAG_TYPE } from "@client/shell/panels/DiagramToolboxContext";
import { useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useRegisterDiagramView } from "@client/shell/panels/DiagramViewContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { useViewReport } from "@client/diagrams/useViewReport";
import type { Viewport } from "@client/diagrams/viewReport";
import { CanvasScrollbars } from "@client/canvas/scroll/CanvasScrollbars";
import { scrollExtentOf } from "@client/canvas/scroll/scrollGeometry";
import { useDependencyGraphStream } from "./useDependencyGraphStream";
import type { DependencyGraphElement, DependencyGraphModel } from "./dependencyGraphModel";

/**
 * The vertical distance between adjacent rows, in the module's own y units.
 *
 * Mirrors `DependencyGraphRows.Height` in the backend, and must stay equal to it: the y this
 * canvas sends with a move is `row × this`, and the backend divides the same constant back out.
 * A mismatch would land every vertical drag on the wrong row.
 */
const ROW_HEIGHT = 60;

/** How tall a node's box is drawn, leaving a gutter between rows. */
const NODE_HEIGHT = 36;

/**
 * How wide a node's box is, in canvas units.
 *
 * The timeline derived a box's width from its duration; a node has none, so the width is the
 * module's own rendering constant - in canvas units rather than pixels, so it grows and shrinks
 * with the zoom exactly as a period's did.
 */
const NODE_WIDTH = 160;

/** The id of the arrowhead marker this canvas defines and its edge stylesheet points at. */
const ARROWHEAD_ID = "dependency-graph-arrowhead";

/** Zoom limits, in pixels per canvas unit. */
const MIN_PIXELS_PER_UNIT = 0.05;
const MAX_PIXELS_PER_UNIT = 8;
const ZOOM_STEP = 1.25;

/**
 * Vertical zoom limits, in pixels per module y unit. Far tighter than the horizontal axis's:
 * rows are discrete, so past a few steps either way more vertical zoom only wastes screen.
 */
const MIN_VERTICAL_SCALE = 0.25;
const MAX_VERTICAL_SCALE = 4;

/**
 * What a surface is assumed to span before it has been measured - in jsdom, and for the render
 * that happens before the first layout. Only the reported rectangle uses these: the drawing
 * itself always measures. A guess is better than a zero here, because a zero-sized rectangle
 * would report a viewport that admits nothing and the backend would cull the whole graph.
 */
const DEFAULT_SURFACE_WIDTH = 1200;
const DEFAULT_SURFACE_HEIGHT = 600;

interface DependencyGraphView {
  /** The canvas coordinate at the view's left edge. */
  startX: number;
  /** How many pixels one canvas unit covers - the zoom, which never reaches the backend. */
  pixelsPerUnit: number;
  /** Vertical scroll, in the module's y units. */
  panY: number;
  /**
   * How many pixels one module y unit covers - the same zoom applied vertically, so zooming
   * spreads and squeezes the rows along with the horizontal axis instead of leaving them fixed.
   */
  verticalScale: number;
}

interface DragState {
  id: string;
  /** Where the pointer went down. */
  clientX: number;
  clientY: number;
  /** The node's own placement when the drag began. */
  x: number;
  y: number;
  moved: boolean;
}

interface DragPreview {
  id: string;
  x: number;
  row: number;
}

interface ConnectDrag {
  fromId: string;
  /**
   * Which anchor the drag lifted from. From the right, the dependency reads dragged-node depends
   * on landing; from the left it arrives reversed - landing depends on dragged-node - because
   * what depends on a node arrives at it.
   */
  fromSide: "left" | "right";
  /** Where the pending curve currently ends, in module coordinates. */
  x: number;
  y: number;
  /** The node under the pointer, when there is one. */
  overId?: string;
}

/**
 * The dependency graph: named nodes on rows, joined by directed depends-on edges, at the
 * coordinates their author gave them.
 *
 * This is the timeline canvas with the time taken out. There is no ruler - its whole job was
 * naming times, and there are none. Zoom and pan remain, as plain geometry: a units-to-pixels
 * transform that never reaches the backend, so what crosses the wire is always a point in the
 * module's own coordinate space. What this type adds rather than deletes is direction: every
 * edge carries an arrowhead at its dependency end.
 */
export function DependencyGraphCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useDependencyGraphStream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const surfaceRef = useRef<HTMLDivElement | null>(null);

  useRegisterDiagramToolbox(useToolboxItems(projectId, path));

  const [view, setView] = useState<DependencyGraphView>(() => ({
    startX: -NODE_WIDTH,
    pixelsPerUnit: 1,
    panY: -ROW_HEIGHT,
    verticalScale: 1,
  }));
  const fittedRef = useRef(false);

  // This canvas draws in pixels-per-unit rather than through a viewBox, so it converts at its
  // own call site: the shared library is given a rectangle already in this module's units and
  // converts nothing (view-delta-adoption Requirement 3.4). Kept in a ref because the report
  // fires when the debounce settles, by which time `view` may have moved on again.
  const viewRef = useRef(view);
  viewRef.current = view;

  const visibleRect = (current: DependencyGraphView): Viewport => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    // Before the first measure - and in jsdom, which lays nothing out - the view's own origin is
    // the only honest answer; a zero-sized rectangle would report a viewport admitting nothing.
    const width = rect && rect.width > 0 ? rect.width : DEFAULT_SURFACE_WIDTH;
    const height = rect && rect.height > 0 ? rect.height : DEFAULT_SURFACE_HEIGHT;
    return {
      minX: current.startX,
      minY: current.panY,
      maxX: current.startX + width / current.pixelsPerUnit,
      maxY: current.panY + height / current.verticalScale,
    };
  };

  // Keyed on the four numbers the rectangle is derived from, so a pan, a zoom, a scrollbar drag
  // and a programmatic fit all reach the report the same way - by having changed the view.
  const reportedBox = visibleRect(view);
  useViewReport({
    view: {
      x: reportedBox.minX,
      y: reportedBox.minY,
      w: reportedBox.maxX - reportedBox.minX,
      h: reportedBox.maxY - reportedBox.minY,
    },
    report: reportView,
    convert: () => visibleRect(viewRef.current),
    ready: !loading && !failed,
  });

  const panRef = useRef<{ clientX: number; clientY: number; view: DependencyGraphView; moved: boolean } | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const [drag, setDrag] = useState<DragPreview | null>(null);
  const [connect, setConnect] = useState<ConnectDrag | null>(null);
  const connectRef = useRef<ConnectDrag | null>(null);
  const [rejection, setRejection] = useState("");

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  // A right-click's menu, opened once the pushed selection for that target arrives with its
  // actions: the menu shows the backend's answer, never a guess - and the shared hook opens
  // at once when the target is already the selection, actions in hand.
  const { menuPosition, openMenuAt, closeMenu } = useElementContextMenu(selectionKey, actions.length > 0, (id) =>
    select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
  );

  /** Fits the whole graph into view, with a margin. */
  const fitToView = useCallback(() => {
    const surface = surfaceRef.current;
    const elements = [...model.elements.values()];
    if (!surface || elements.length === 0) {
      return;
    }

    const min = Math.min(...elements.map((element) => element.x));
    const max = Math.max(...elements.map((element) => element.x + NODE_WIDTH));
    const span = Math.max(max - min, NODE_WIDTH);
    const width = surface.getBoundingClientRect().width || 1200;

    setView((current) => ({
      ...current,
      startX: min - span * 0.1,
      pixelsPerUnit: clampZoom(width / (span * 1.2)),
      panY: Math.min(...elements.map((element) => element.y)) - ROW_HEIGHT,
      verticalScale: 1,
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

  /** Zooms by a factor: greater than one moves in, less than one moves out. */
  const zoomBy = useCallback((factor: number) => {
    setView((current) => {
      const surface = surfaceRef.current;
      const rect = surface?.getBoundingClientRect();
      const width = rect?.width || 1200;
      const height = rect?.height || 600;
      const next = clampZoom(current.pixelsPerUnit * factor);
      // The same step vertically: rows spread and squeeze along with the horizontal axis.
      const nextVertical = clampVerticalScale(current.verticalScale * factor);
      // About the centre on both axes, so the thing being looked at stays where it is.
      const centre = current.startX + (width / 2) / current.pixelsPerUnit;
      const centreY = current.panY + (height / 2) / current.verticalScale;
      return {
        startX: centre - (width / 2) / next,
        pixelsPerUnit: next,
        panY: centreY - (height / 2) / nextVertical,
        verticalScale: nextVertical,
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

  const toUnits = (clientX: number): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    return view.startX + (clientX - (rect?.left ?? 0)) / view.pixelsPerUnit;
  };

  const toModuleY = (clientY: number): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    return view.panY + (clientY - (rect?.top ?? 0)) / view.verticalScale;
  };

  const xToPx = (x: number): number => (x - view.startX) * view.pixelsPerUnit;
  const yToPx = (y: number): number => (y - view.panY) * view.verticalScale;

  const onSurfacePointerDown = (event: React.MouseEvent) => {
    // "Empty space" is the surface div OR the bare svg that fills it. A real click never lands
    // on the div itself - the svg covers it - so a target===currentTarget check silently
    // disabled panning for every real mouse, while the synthetic events that verified it
    // dispatched straight at the div and passed. Node shapes stopPropagation, so anything
    // arriving here from inside the svg is background.
    const target = event.target as Element;
    if (target !== event.currentTarget && !target.classList?.contains("dependency-graph-content")) {
      return;
    }

    closeMenu();
    if (event.button === 0) {
      select(null);
    }

    // Both buttons pan on empty space: the left is the convention every canvas here follows,
    // and the right frees the left hand for selection-heavy work. The `moved` flag is what
    // keeps a motionless right-click from being eaten as a zero-length pan.
    panRef.current = { clientX: event.clientX, clientY: event.clientY, view, moved: false };
  };

  const onElementPointerDown = (event: React.MouseEvent, element: DependencyGraphElement) => {
    event.stopPropagation();
    setRejection("");
    dragRef.current = {
      id: element.id,
      clientX: event.clientX,
      clientY: event.clientY,
      x: element.x,
      y: element.y,
      moved: false,
    };
  };

  const onAnchorPointerDown = (event: React.MouseEvent, element: DependencyGraphElement, side: "left" | "right") => {
    // Starting a dependency: drag from a side anchor to another node. Which side is remembered,
    // because it decides which end of the edge is the dependency on release.
    event.stopPropagation();
    const start: ConnectDrag = { fromId: element.id, fromSide: side, x: toUnits(event.clientX), y: toModuleY(event.clientY) };
    connectRef.current = start;
    setConnect(start);
  };

  const onPointerMove = (event: React.MouseEvent) => {
    const dragging = dragRef.current;
    if (dragging) {
      const deltaX = (event.clientX - dragging.clientX) / view.pixelsPerUnit;
      const y = dragging.y + (event.clientY - dragging.clientY) / view.verticalScale;
      dragging.moved ||= Math.abs(event.clientX - dragging.clientX) + Math.abs(event.clientY - dragging.clientY) > 3;
      // The row snaps as the pointer crosses the midpoint, so the user sees where it will land
      // before releasing. Rounded away from zero to match DependencyGraphRows. The horizontal
      // does not snap: x is free, and rounding it would quietly make the canvas a grid.
      setDrag({
        id: dragging.id,
        x: dragging.x + deltaX,
        row: nearestRow(y),
      });
      return;
    }

    const connecting = connectRef.current;
    if (connecting) {
      const next = { ...connecting, x: toUnits(event.clientX), y: toModuleY(event.clientY) };
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
        panY: pan.view.panY - (event.clientY - pan.clientY) / pan.view.verticalScale,
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
        const error = await moveElementTo(landed.id, landed.x, landed.row * ROW_HEIGHT);
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
      // The node under the release, read from the event itself as well as from the tracked
      // hover: a fast drag can land its mouseup before any mouseenter fired, and the gesture
      // must not depend on the hover having kept up.
      const under = (event.target as Element | null)?.closest?.("[data-element-id]");
      const targetId = connecting.overId ?? under?.getAttribute("data-element-id") ?? null;
      if (targetId === connecting.fromId) {
        // Back onto its own source: a node depending on itself is refused anyway, so this is a
        // "never mind" and nothing is sent.
        return;
      }

      // The whole gesture in one call - source and landing together in a rel: id. Deliberately
      // stateless: the two-call protocol this replaces kept an armed source in the backend
      // between calls, and a stale arm related the wrong pair. A drag from the left anchor
      // arrives reversed - what depends on a node arrives at it - so the landing becomes the
      // dependent and the dragged node the dependency.
      const landing = targetId ?? newPlacementId(connecting.x, nearestRow(connecting.y));
      const gesture = connecting.fromSide === "left"
        ? `rel:${landing}->${connecting.fromId}`
        : `rel:${connecting.fromId}->${landing}`;
      void (async () => {
        const outcome = await executeAction("dependencies.connect", elementSourceOf(gesture));
        if (!outcome.accepted) {
          setRejection(outcome.error);
        }
      })();
    }
  };

  const onElementPointerEnter = (element: DependencyGraphElement) => {
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

  /** One menu opening for nodes and dependencies alike - both select through the same channel. */
  const openTargetMenuAt = (event: React.MouseEvent, id: string) => {
    if (panRef.current?.moved) {
      // The right button was panning; releasing it must not also open a menu.
      return;
    }

    openMenuAt(event, id);
  };

  const onElementContextMenu = (event: React.MouseEvent, element: DependencyGraphElement) => {
    openTargetMenuAt(event, element.id);
  };

  const onRelationClick = (event: React.MouseEvent, relationId: string) => {
    // A left-click on a dependency selects it, exactly as it does a node (the resolver answers
    // for both). stopPropagation keeps the surface from reading it as background.
    event.stopPropagation();
    select(elementSelectionOf(entryId, path, relationId));
  };

  /**
   * A toolbox entry dropped anywhere on the canvas: the drop names a placement - the coordinate
   * and row under the pointer - and the node appears there with nothing asked. One handler on
   * the surface; drops over nodes bubble here and land at the pointer all the same.
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
        elementSourceOf(newPlacementId(toUnits(event.clientX), nearestRow(toModuleY(event.clientY)))));
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
   * this canvas only forwards the keystroke against the selected node. Tab and Enter are
   * prevented from their browser defaults (focus traversal, activation) when a node is
   * selected, because here they mean "add to the right" and "add below".
   */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!selectedId || isTextTarget(event.target)) {
      return;
    }

    const shortcut = structuralShortcutFor(event, ["F2", "Delete", "Insert", "Tab", "Enter"]);
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
      <div className="dependency-graph-canvas canvas-host dependency-graph-canvas-message canvas-host-message">
        <p>This dependency graph could not be opened.</p>
      </div>
    );
  }

  // Where each node currently sits, with the in-flight gesture's preview overriding the model -
  // which is what makes the edges follow the drag: the curves are recomputed from these boxes on
  // every pointer move.
  const boxes = new Map<string, ConnectorBox>();
  for (const element of model.elements.values()) {
    boxes.set(element.id, boxFor(element, drag, xToPx, yToPx, view.pixelsPerUnit, view.verticalScale));
  }

  const width = surfaceRef.current?.getBoundingClientRect().width ?? 1200;

  return (
    <div className="dependency-graph-canvas canvas-host">
      <div
        ref={surfaceRef}
        className="dependency-graph-surface canvas-viewport"
        role="application"
        aria-label="Dependency graph"
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
        <svg className="dependency-graph-content canvas-drawing">
          <defs>
            {/*
              The arrowhead, and the reason this type exists rather than the timeline. The shared
              connection component draws its path from the source anchor to the target anchor, so
              a `marker-end` - attached in this module's own stylesheet, which is why the shared
              component needed no change - always lands on the dependency end.
            */}
            <marker
              id={ARROWHEAD_ID}
              className="dependency-graph-arrowhead canvas-arrowhead"
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

          {[...model.relations.values()].map((relation) => {
            const from = boxes.get(relation.fromElementId);
            const to = boxes.get(relation.toElementId);
            const fromElement = model.elements.get(relation.fromElementId);
            const toElement = model.elements.get(relation.toElementId);
            if (!from || !to || !fromElement || !toElement) {
              // A dangling dependency is the validator's to report; there is nothing to draw.
              return null;
            }

            // The shared interactive connection. Whether the line must loop - the dependency
            // sitting behind the node that needs it - is decided here in the module's own
            // coordinates and handed over as a fact.
            return (
              <InteractiveBezierConnection
                key={relation.id}
                id={relation.id}
                from={from}
                to={to}
                loopsBack={toElement.x < fromElement.x + NODE_WIDTH}
                selected={relation.id === selectedId}
                label={relation.label || undefined}
                className="dependency-graph-relation canvas-connection"
                selectedClassName="dependency-graph-selected canvas-selected"
                hitClassName="dependency-graph-relation-hit canvas-connection-hit"
                lineClassName="dependency-graph-relation-line canvas-connection-line"
                onSelect={(event) => onRelationClick(event, relation.id)}
                onOpenMenu={(event) => openTargetMenuAt(event, relation.id)}
              />
            );
          })}

          {connect ? <PendingRelation boxes={boxes} connect={connect} xToPx={xToPx} yToPx={yToPx} /> : null}

          {[...model.elements.values()].map((element) => (
            <DependencyGraphNodeShape
              key={element.id}
              element={element}
              box={boxes.get(element.id)!}
              selected={element.id === selectedId}
              connectTarget={connect?.overId === element.id}
              dragPreview={drag?.id === element.id ? drag : null}
              onElementDown={onElementPointerDown}
              onAnchorDown={onAnchorPointerDown}
              onElementEnter={onElementPointerEnter}
              onElementLeave={onElementPointerLeave}
              onElementContextMenu={onElementContextMenu}
            />
          ))}
        </svg>
        <CanvasScrollbars
          {...scrollAxesOf(model, view, width)}
          className="dependency-graph-scrollbars"
          onPan={(startX, panY) => setView((current) => ({ ...current, startX, panY }))}
        />
      </div>
      <ContextMenu
        open={menuPosition !== null}
        groups={toMenuGroups(actions, (action) => {
          closeMenu();
          void executeAction(action.id, selectedId ? elementSourceOf(selectedId) : undefined);
        })}
        position={menuPosition ?? { x: 0, y: 0 }}
        onClose={closeMenu}
      />
      {loading ? <p className="dependency-graph-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="dependency-graph-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}

/** The placement id a gesture carries when it lands on empty canvas: `new:{x},{row}`. */
function newPlacementId(x: number, row: number): string {
  return `new:${x},${row}`;
}

/** A node's box in pixels, with any in-flight drag applied. Centre-based, as the shared geometry expects. */
function boxFor(
  element: DependencyGraphElement,
  drag: DragPreview | null,
  xToPx: (x: number) => number,
  yToPx: (y: number) => number,
  pixelsPerUnit: number,
  verticalScale: number,
): ConnectorBox {
  let x = element.x;
  let y = element.y;

  if (drag && drag.id === element.id) {
    x = drag.x;
    y = drag.row * ROW_HEIGHT;
  }

  const width = Math.max(NODE_WIDTH * pixelsPerUnit, 2);
  const left = xToPx(x);
  // The box scales with the vertical zoom, exactly as its width already scales with the
  // horizontal one - a row's height in pixels is a view concern, never a module one.
  const height = NODE_HEIGHT * verticalScale;

  return {
    x: left + width / 2,
    y: yToPx(y) + height / 2,
    width,
    height,
  };
}

/** The nearest row for a module-space y, matching DependencyGraphRows.ToNearestRow's away-from-zero midpoint. */
function nearestRow(y: number): number {
  const exact = y / ROW_HEIGHT;
  return exact >= 0 ? Math.floor(exact + 0.5) : -Math.floor(-exact + 0.5);
}

/**
 * What a drag hint says, in this type's own terms.
 *
 * The timeline's hint read `2026-01-31 · row 1`. There is no date to put here, so the position
 * itself is what the hint shows - rounded for reading, never for writing: the coordinate that
 * travels is the pointer's, unrounded.
 */
function hintFor(preview: DragPreview): string {
  return `${Math.round(preview.x)} · row ${preview.row}`;
}

interface DependencyGraphNodeShapeProps {
  element: DependencyGraphElement;
  box: ConnectorBox;
  selected: boolean;
  connectTarget: boolean;
  dragPreview: DragPreview | null;
  onElementDown: (event: React.MouseEvent, element: DependencyGraphElement) => void;
  onAnchorDown: (event: React.MouseEvent, element: DependencyGraphElement, side: "left" | "right") => void;
  onElementEnter: (element: DependencyGraphElement) => void;
  onElementLeave: () => void;
  onElementContextMenu: (event: React.MouseEvent, element: DependencyGraphElement) => void;
}

/**
 * The class names the shared span element hangs this type's styling on.
 *
 * `adorner` names a class the stylesheet hides. The shared component draws resize strips on a
 * selected element's edges, and a node has no extent to resize - the timeline resized a
 * duration. Hiding them here rather than adding a flag to the shared component keeps the
 * timeline untouched, which this spec's reliability requirement asks for outright.
 */
const SPAN_CLASSES: SpanElementClasses = {
  span: "dependency-graph-node canvas-node",
  moment: "dependency-graph-node-point",
  label: "dependency-graph-label canvas-node-label",
  hint: "dependency-graph-hint canvas-hint",
  adorner: "dependency-graph-adorner",
  anchor: "dependency-graph-anchor canvas-anchor",
  anchorHit: "dependency-graph-anchor-hit canvas-anchor-hit",
};

function DependencyGraphNodeShape({
  element,
  box,
  selected,
  connectTarget,
  dragPreview,
  onElementDown,
  onAnchorDown,
  onElementEnter,
  onElementLeave,
  onElementContextMenu,
}: DependencyGraphNodeShapeProps) {
  const classes = ["dependency-graph-element canvas-element"];
  if (selected) {
    classes.push("dependency-graph-selected canvas-selected");
  }

  if (connectTarget) {
    classes.push("dependency-graph-connect-target canvas-connect-target");
  }

  // The drawing - the box, the label that trims when the box is too narrow, and the connection
  // anchors - is the shared span element's; only what this type says through it is decided here.
  // No `onResizeStart` is passed: there is nothing to resize.
  return (
    <SpanElement
      className={classes.join(" ")}
      data-element-id={element.id}
      box={box}
      label={element.label || element.id}
      hint={dragPreview ? hintFor(dragPreview) : null}
      selected={selected}
      classes={SPAN_CLASSES}
      onAnchorStart={(event, side) => onAnchorDown(event, element, side)}
      onMouseDown={(event) => onElementDown(event, element)}
      onMouseEnter={() => onElementEnter(element)}
      onMouseLeave={onElementLeave}
      onContextMenu={(event) => onElementContextMenu(event, element)}
      onDragOver={(event) => event.preventDefault()}
    />
  );
}

function PendingRelation({
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
    ? facingAnchorsBetween(from, boxes.get(connect.overId)!)[1]
    : { x: xToPx(connect.x), y: yToPx(connect.y) };
  const start = sideAnchorOf(from, end.x >= from.x ? "right" : "left");

  return <path className="dependency-graph-pending-relation canvas-pending-connection" d={horizontalBezierPath(start, end)} />;
}

/**
 * The two scroll axes as the shared scroll view wants them - the fork's own extent
 * arithmetic, term for term: the x extent from the elements' range (each widened by
 * NODE_WIDTH) with a half-span margin floored at one node width, the y extent with a fixed
 * two-row margin. The 400px height, like the fork's, only shapes the thumb ratio; an empty
 * model falls back to the view's own window.
 */
function scrollAxesOf(model: DependencyGraphModel, view: DependencyGraphView, widthPx: number) {
  const elements = [...model.elements.values()];
  const heightPx = 400;
  const viewSpan = widthPx / view.pixelsPerUnit;
  const minX = elements.length > 0 ? Math.min(...elements.map((element) => element.x)) : view.startX;
  const maxX = elements.length > 0
    ? Math.max(...elements.map((element) => element.x + NODE_WIDTH))
    : view.startX + viewSpan;
  const horizontalExtent = scrollExtentOf(minX, maxX, { factor: 0.5, minimumSpan: NODE_WIDTH });
  const minY = elements.length > 0 ? Math.min(...elements.map((element) => element.y)) : view.panY;
  const maxY = elements.length > 0 ? Math.max(...elements.map((element) => element.y + ROW_HEIGHT)) : view.panY + heightPx;
  const verticalExtent = scrollExtentOf(minY, maxY, { factor: 0, minimum: 2 * ROW_HEIGHT });
  return {
    horizontal: { viewStart: view.startX, viewSpan, ...horizontalExtent },
    vertical: { viewStart: view.panY, viewSpan: heightPx / view.verticalScale, ...verticalExtent },
  };
}

function clampVerticalScale(scale: number): number {
  return Math.min(MAX_VERTICAL_SCALE, Math.max(MIN_VERTICAL_SCALE, scale));
}

function clampZoom(pixelsPerUnit: number): number {
  return Math.min(MAX_PIXELS_PER_UNIT, Math.max(MIN_PIXELS_PER_UNIT, pixelsPerUnit));
}
