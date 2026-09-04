import type { ConnectorBox } from "@client/canvas/connectors";
import { StraightConnection } from "@client/canvas/connections/straight/StraightConnection";
import { StyledBoxElement } from "@client/canvas/elements/styled-box/StyledBoxElement";
import { FrameElement } from "@client/canvas/elements/frame/FrameElement";
import { elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { useElementContextMenu } from "@client/canvas/useElementContextMenu";
import { CanvasScrollbars } from "@client/canvas/scroll/CanvasScrollbars";
import { scrollExtentOf } from "@client/canvas/scroll/scrollGeometry";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useViewReport } from "@client/diagrams/useViewReport";
import { shownRectOf } from "@client/diagrams/viewReport";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { TOOLBOX_DRAG_TYPE, useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useRegisterDiagramView, type DiagramViewControls } from "@client/shell/panels/DiagramViewContext";
import { boxesOf, type C4BoundaryBox, type C4Model, type C4Node, type C4Relationship } from "./c4Model";
import type { C4RelationshipPayload } from "@client/generated/c4_pb";
import { useC4Stream } from "./useC4Stream";

/** The visible rectangle, in canvas units - the svg viewBox as data. */
interface ViewBox {
  x: number;
  y: number;
  w: number;
  h: number;
}

const ZOOM_STEP = 1.25;
const MIN_VIEW_WIDTH = 40;
const MAX_VIEW_WIDTH = 100000;

export interface C4CanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/**
 * Renders one C4 view. Everything it draws was decided by the backend - the boxes at the sizes
 * it measured, the palette it resolved, the title and the key it composed - so the canvas is a
 * renderer rather than a second opinion about what a C4 diagram is
 * (c4-diagrams Requirement 4).
 */
export function C4Canvas({ projectId, entryId, path }: C4CanvasProps) {
  const { model, loading, failed, reportView, moveElementTo } = useC4Stream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();

  // The palette this view contributes, registered for the Toolbox panel while this canvas is
  // the mounted one - the same discipline the mindmap follows.
  useRegisterDiagramToolbox(useToolboxItems(projectId, path));

  // A right-click's menu, opened once the pushed selection for that element arrives with its
  // actions: the menu shows the backend's answer, never a guess. The shared hook also opens
  // at once when the element is already the selection, actions in hand.
  const selectionKey = innermostKey(selection);
  const { menuPosition, openMenuAt, closeMenu } = useElementContextMenu(selectionKey, actions.length > 0, (id) =>
    select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
  );

  // The element a toolbox drag is held over - highlighted so the drop's outcome is visible
  // before the button is released.
  const [dropTargetId, setDropTargetId] = useState<string | undefined>(undefined);

  // A drag of an element in flight: which one, where the pointer started, where the element
  // started, and whether it has moved far enough to be a drag rather than a wobbly click. A
  // ref for the parts nothing renders from; the live offset is state, because it is exactly
  // what renders differently while the button is down.
  const dragRef = useRef<{ id: string; clientX: number; clientY: number; x: number; y: number; moved: boolean; dx: number; dy: number } | null>(null);
  const [dragOffset, setDragOffset] = useState<{ id: string; dx: number; dy: number } | null>(null);
  const dragJustEndedRef = useRef(false);

  const [focusedId, setFocusedId] = useState<string | undefined>(undefined);
  const [view, setView] = useState<ViewBox | null>(null);
  const panRef = useRef<{ clientX: number; clientY: number; view: ViewBox; moved: boolean } | null>(null);
  const panJustEndedRef = useRef(false);
  const surfaceRef = useRef<SVGSVGElement>(null);

  const fitBox = useMemo(() => fitBoxOf(model), [model]);
  const effectiveView = view ?? fitBox;
  const viewRef = useRef(effectiveView);
  viewRef.current = effectiveView;

  const zoomBy = useCallback((factor: number, aboutX?: number, aboutY?: number) => {
    setView(() => {
      const current = viewRef.current;
      const w = Math.min(MAX_VIEW_WIDTH, Math.max(MIN_VIEW_WIDTH, current.w / factor));
      const applied = current.w / w;
      const h = current.h / applied;
      const px = aboutX ?? current.x + current.w / 2;
      const py = aboutY ?? current.y + current.h / 2;
      return { x: px - (px - current.x) / applied, y: py - (py - current.y) / applied, w, h };
    });
  }, []);

  // Attached by hand as non-passive: React's synthetic wheel listener cannot preventDefault,
  // and without that every zoom also scrolls the page.
  useEffect(() => {
    const surface = surfaceRef.current;
    if (surface === null) {
      return;
    }

    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      const rect = surface.getBoundingClientRect();
      const current = viewRef.current;
      const aboutX = rect.width > 0 ? current.x + ((event.clientX - rect.left) / rect.width) * current.w : undefined;
      const aboutY = rect.height > 0 ? current.y + ((event.clientY - rect.top) / rect.height) * current.h : undefined;
      zoomBy(event.deltaY < 0 ? ZOOM_STEP : 1 / ZOOM_STEP, aboutX, aboutY);
    };

    surface.addEventListener("wheel", onWheel, { passive: false });
    return () => surface.removeEventListener("wheel", onWheel);
  }, [zoomBy, loading, failed]);

  const viewControls = useMemo<DiagramViewControls>(
    () => ({
      zoomIn: () => zoomBy(ZOOM_STEP),
      zoomOut: () => zoomBy(1 / ZOOM_STEP),
      fitToView: () => setView(null),
    }),
    [zoomBy],
  );
  useRegisterDiagramView(viewControls);

  useViewReport({
    view: effectiveView,
    report: reportView,
    convert: () => shownRectOf(viewRef.current, surfaceRef.current),
    ready: !loading && !failed,
  });

  const unitsPerPixel = (box: ViewBox): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    return rect !== undefined && rect.width > 0 ? box.w / rect.width : 1;
  };

  const onSurfacePointerDown = (event: React.MouseEvent) => {
    if (event.target !== event.currentTarget) {
      return;
    }
    panRef.current = { clientX: event.clientX, clientY: event.clientY, view: viewRef.current, moved: false };
  };

  const onSurfacePointerMove = (event: React.MouseEvent) => {
    const drag = dragRef.current;
    if (drag) {
      // A few pixels of wobble while clicking is not a drag, and treating it as one would put
      // an entry on the history for having clicked something.
      if (!drag.moved && Math.hypot(event.clientX - drag.clientX, event.clientY - drag.clientY) <= 4) {
        return;
      }

      drag.moved = true;
      const scale = unitsPerPixel(viewRef.current);
      drag.dx = (event.clientX - drag.clientX) * scale;
      drag.dy = (event.clientY - drag.clientY) * scale;
      setDragOffset({ id: drag.id, dx: drag.dx, dy: drag.dy });
      return;
    }

    const pan = panRef.current;
    if (!pan) {
      return;
    }
    if (!pan.moved && Math.hypot(event.clientX - pan.clientX, event.clientY - pan.clientY) <= 4) {
      return;
    }
    pan.moved = true;
    const scale = unitsPerPixel(pan.view);
    setView({
      ...pan.view,
      x: pan.view.x - (event.clientX - pan.clientX) * scale,
      y: pan.view.y - (event.clientY - pan.clientY) * scale,
    });
  };

  const onSurfacePointerUp = () => {
    const drag = dragRef.current;
    dragRef.current = null;
    if (drag?.moved) {
      setDragOffset(null);
      // The click that trails a completed drag is the same gesture, not a new selection.
      dragJustEndedRef.current = true;
      // Read from the ref rather than from state: the last mousemove and this mouseup can
      // land in one batch, and then the state this handler closed over is a frame behind -
      // which would write a stale position, or none at all.
      //
      // Nothing optimistic either way: the element stays where it was until the backend's
      // delta says otherwise, so what is drawn is always what was recorded.
      void moveElementTo(drag.id, drag.x + drag.dx, drag.y + drag.dy);
      return;
    }

    setDragOffset(null);
    const pan = panRef.current;
    panRef.current = null;
    if (pan?.moved) {
      panJustEndedRef.current = true;
    }
  };

  const onBackgroundClick = (event: React.MouseEvent) => {
    if (event.target !== event.currentTarget) {
      return;
    }
    if (panJustEndedRef.current) {
      // The click that trails a pan is the same gesture: letting go must not also deselect.
      panJustEndedRef.current = false;
      return;
    }
    setFocusedId(undefined);
    select(null);
  };

  const onNodeClick = (node: C4Node) => {
    if (dragJustEndedRef.current) {
      // The click a completed drag fires is the same gesture, not a new selection.
      dragJustEndedRef.current = false;
      return;
    }

    setFocusedId(node.id);
    surfaceRef.current?.focus();
    select(elementSelectionOf(entryId, path, node.id));
  };

  const onNodePointerDown = (node: C4Node, event: React.MouseEvent) => {
    if (event.button !== 0) {
      return; // a right-click is the menu's gesture, not a drag
    }

    dragRef.current = { id: node.id, clientX: event.clientX, clientY: event.clientY, x: node.x, y: node.y, moved: false, dx: 0, dy: 0 };
  };

  /** Right-click an element: select it with the menu gesture, open the menu on the push. */
  const onNodeContextMenu = (node: C4Node, event: React.MouseEvent) => {
    setFocusedId(node.id);
    surfaceRef.current?.focus();
    openMenuAt(event, node.id);
  };

  /** A toolbox entry held over an element: allowed, and shown as the drop's outcome. */
  const onNodeDragOver = (node: C4Node, event: React.DragEvent) => {
    if (!event.dataTransfer.types.includes(TOOLBOX_DRAG_TYPE)) {
      return;
    }

    event.preventDefault(); // preventDefault is what permits the drop here
    event.dataTransfer.dropEffect = "copy";
    if (dropTargetId !== node.id) {
      setDropTargetId(node.id);
    }
  };

  /**
   * A toolbox entry dropped on an element. The drag carries the backend's own action id and
   * the element becomes the new one's parent - which is how C4's containment gets decided by
   * the gesture: a container onto a system goes in that system, a component onto a container
   * goes in that container. A pairing C4 forbids comes back refused, with a sentence.
   */
  const onNodeDrop = (node: C4Node, event: React.DragEvent) => {
    const actionId = event.dataTransfer.getData(TOOLBOX_DRAG_TYPE);
    setDropTargetId(undefined);
    if (!actionId) {
      return;
    }

    event.preventDefault();
    setFocusedId(node.id);
    void executeAction(actionId, elementSourceOf(node.id));
  };

  /**
   * A toolbox entry dropped on empty canvas: no parent, so only what stands on its own - a
   * person, a software system - can land. The backend refuses the rest and says where it
   * should have gone.
   */
  const onSurfaceDrop = (event: React.DragEvent) => {
    if (event.target !== event.currentTarget) {
      return; // an element's own drop; its handler answers
    }

    const actionId = event.dataTransfer.getData(TOOLBOX_DRAG_TYPE);
    setDropTargetId(undefined);
    if (!actionId) {
      return;
    }

    event.preventDefault();
    void executeAction(actionId);
  };

  const onSurfaceDragOver = (event: React.DragEvent) => {
    if (!event.dataTransfer.types.includes(TOOLBOX_DRAG_TYPE)) {
      return;
    }

    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
    if (event.target === event.currentTarget && dropTargetId !== undefined) {
      setDropTargetId(undefined);
    }
  };

  const onKeyDown = (event: React.KeyboardEvent) => {
    // A key that means something in a text field is left to it - a prompt is a real input the
    // shell mounts, so text editing wins there.
    if (!focusedId || isTextTarget(event.target)) {
      return;
    }

    const shortcut = structuralShortcutFor(event, ["F2", "Delete", "Insert"]);
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    // The backend holds the key-to-action table; the canvas forwards the keystroke as data.
    void executeShortcut(shortcut, elementSourceOf(focusedId));
  };

  if (failed) {
    return (
      <div className="c4-canvas" data-testid="c4-canvas">
        <div className="c4-canvas-unavailable" role="alert">
          This diagram is no longer available at {path.join("/")}.
        </div>
      </div>
    );
  }

  const nodes = [...model.nodes.values()];
  const relationships = [...model.relationships.values()];
  const boundaries = [...model.boundaries.values()];

  return (
    <div className="c4-canvas" data-testid="c4-canvas">
      {loading ? (
        <div className="c4-canvas-loading" role="status">
          Loading…
        </div>
      ) : (
        <>
          {/* C4 requires every diagram to carry a title describing its type and scope. */}
          {model.view && (
            <div className="c4-canvas-title" data-testid="c4-title">
              {model.view.title}
            </div>
          )}
          <svg
            ref={surfaceRef}
            className="c4-canvas-surface"
            viewBox={`${effectiveView.x} ${effectiveView.y} ${effectiveView.w} ${effectiveView.h}`}
            tabIndex={0}
            role="img"
            aria-label={model.view?.title ?? "C4 diagram"}
            onClick={onBackgroundClick}
            onMouseDown={onSurfacePointerDown}
            onMouseMove={onSurfacePointerMove}
            onMouseUp={onSurfacePointerUp}
            onMouseLeave={onSurfacePointerUp}
            onKeyDown={onKeyDown}
            onDragOver={onSurfaceDragOver}
            onDrop={onSurfaceDrop}
          >
            <defs>
              {/* One arrowhead, reused: C4 relationships are unidirectional. */}
              <marker id="c4-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                <path d="M 0 0 L 10 5 L 0 10 z" className="c4-arrowhead" />
              </marker>
            </defs>

            {/* Boundaries first, so everything they enclose draws on top of them. */}
            {boundaries.map((boundary) => (
              <C4BoundaryShape key={boundary.id} boundary={boundary} />
            ))}
            {relationships.map((relationship) => (
              <C4RelationshipShape key={relationship.id} relationship={relationship} />
            ))}
            {nodes.map((node) => (
              <C4NodeShape
                key={node.id}
                node={node}
                focused={node.id === focusedId}
                dropTarget={node.id === dropTargetId}
                offset={dragOffset?.id === node.id ? dragOffset : undefined}
                onSelect={() => onNodeClick(node)}
                onPointerDown={(event) => onNodePointerDown(node, event)}
                onContextMenu={(event) => onNodeContextMenu(node, event)}
                onDragOver={(event) => onNodeDragOver(node, event)}
                onDrop={(event) => onNodeDrop(node, event)}
              />
            ))}
          </svg>
          <CanvasScrollbars
            {...scrollAxesOf(effectiveView, model)}
            className="c4-scrollbars"
            onPan={(x, y) => setView({ ...effectiveView, x, y })}
          />

          {/* The backend's own actions for what is selected - rendered, never invented. */}
          <ContextMenu
            open={menuPosition !== null}
            groups={toMenuGroups(actions, (action) => {
              closeMenu();
              void executeAction(action.id);
            })}
            position={menuPosition ?? { x: 0, y: 0 }}
            onClose={closeMenu}
          />

          {/* C4 requires a key explaining every shape and colour the diagram uses, so it can be
              read without accompanying narrative. Built from what the backend actually drew. */}
          {model.view && model.view.legend.length > 0 && (
            <div className="c4-canvas-legend" data-testid="c4-legend">
              <span className="c4-legend-title">Key</span>
              {model.view.legend.map((entry) => (
                <span className="c4-legend-entry" key={entry.label}>
                  <span
                    className="c4-legend-swatch"
                    style={{ background: entry.style?.background, borderColor: entry.style?.background }}
                  />
                  {entry.label}
                </span>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

/** One element box: name, the bracketed type-and-technology line, and the description. */
function C4NodeShape({
  node,
  focused,
  dropTarget,
  offset,
  onSelect,
  onPointerDown,
  onContextMenu,
  onDragOver,
  onDrop,
}: {
  node: C4Node;
  focused: boolean;
  dropTarget: boolean;
  /** How far the pointer has carried this element in the drag currently in flight. */
  offset?: { dx: number; dy: number };
  onSelect: () => void;
  onPointerDown: (event: React.MouseEvent) => void;
  onContextMenu: (event: React.MouseEvent) => void;
  onDragOver: (event: React.DragEvent) => void;
  onDrop: (event: React.DragEvent) => void;
}) {
  const { name, typeLine, description, width, height, style } = node.payload;

  return (
    <StyledBoxElement
      className={`c4-node${focused ? " c4-node-focused" : ""}${dropTarget ? " c4-node-drop-target" : ""}${offset ? " c4-node-dragging" : ""}`}
      x={node.x + (offset?.dx ?? 0)}
      y={node.y + (offset?.dy ?? 0)}
      width={width}
      height={height}
      shape={style?.shape ?? "RoundedBox"}
      background={style?.background ?? "#1168bd"}
      color={style?.color ?? "#ffffff"}
      name={name}
      typeLine={typeLine}
      description={description}
      nameClassName="c4-node-name"
      typeClassName="c4-node-type"
      descriptionClassName="c4-node-description"
      onClick={onSelect}
      onMouseDown={onPointerDown}
      onContextMenu={onContextMenu}
      onDragOver={onDragOver}
      onDrop={onDrop}
      role="button"
      aria-label={name}
    />
  );
}

/**
 * One relationship: a dashed, unidirectional arrow labelled with what it is for and, where the
 * model says so, the technology it uses - which is what a Container diagram exists to show.
 */
function C4RelationshipShape({ relationship }: { relationship: C4Relationship }) {
  const p = relationship.payload;
  const label = p.technology ? `${p.description} [${p.technology}]` : p.description;
  const order = p.interactionOrder;

  // Straight, deliberately: a C4 relationship joins any two elements in any direction, so
  // there is no corridor for a curve to stay inside, and Structurizr and the C4 notation
  // both draw these straight.
  return (
    <StraightConnection
      from={sourceBoxOf(p)}
      to={destinationBoxOf(p)}
      className="c4-relationship"
      markerEnd="url(#c4-arrow)"
      label={label ? (order ? `${order}. ${label}` : label) : undefined}
      labelClassName="c4-relationship-label"
      labelTextAnchor="middle"
    />
  );
}

/** The dashed rectangle around a system's containers or a container's components. */
function C4BoundaryShape({ boundary }: { boundary: C4BoundaryBox }) {
  const { name, kind, width, height } = boundary.payload;
  return (
    <FrameElement
      className="c4-boundary"
      x={boundary.x}
      y={boundary.y}
      width={width}
      height={height}
      label={`${name} [${kind}]`}
      labelClassName="c4-boundary-label"
    />
  );
}

/**
 * The two ends of a relationship as boxes, which is what the shared connector geometry wants.
 * The payload carries each end's measured size precisely so an arrow can land on an edge.
 */
function sourceBoxOf(p: C4RelationshipPayload): ConnectorBox {
  return { x: p.sourceX, y: p.sourceY, width: p.sourceWidth, height: p.sourceHeight };
}

function destinationBoxOf(p: C4RelationshipPayload): ConnectorBox {
  return { x: p.destinationX, y: p.destinationY, width: p.destinationWidth, height: p.destinationHeight };
}

/**
 * Where the view sits inside the content, as the two axes the shared scrollbars take.
 *
 * A ViewBox canvas needs no DOM measurement for this: the view's span in content units IS
 * `w` and `h`. The pixels-per-unit consumer measures its element and divides because it has
 * to; copying that here would be importing a workaround for a problem this canvas does not
 * have. The extent is the drawn content plus a proportional margin, floored at the widest
 * box so a single-element diagram still has room to either side.
 */
function scrollAxesOf(view: ViewBox, model: C4Model) {
  const boxes = boxesOf(model);
  const widest = boxes.length > 0 ? Math.max(...boxes.map((box) => box.width)) : view.w;
  const tallest = boxes.length > 0 ? Math.max(...boxes.map((box) => box.height)) : view.h;
  const minX = boxes.length > 0 ? Math.min(...boxes.map((box) => box.x)) : view.x;
  const maxX = boxes.length > 0 ? Math.max(...boxes.map((box) => box.x + box.width)) : view.x + view.w;
  const minY = boxes.length > 0 ? Math.min(...boxes.map((box) => box.y)) : view.y;
  const maxY = boxes.length > 0 ? Math.max(...boxes.map((box) => box.y + box.height)) : view.y + view.h;

  return {
    horizontal: { viewStart: view.x, viewSpan: view.w, ...scrollExtentOf(minX, maxX, { factor: 0.5, minimumSpan: widest }) },
    vertical: { viewStart: view.y, viewSpan: view.h, ...scrollExtentOf(minY, maxY, { factor: 0.5, minimumSpan: tallest }) },
  };
}

/** The box that fits everything with a margin - what the canvas opens with and Fit to View returns to. */
function fitBoxOf(model: C4Model): ViewBox {
  const boxes = boxesOf(model);
  if (boxes.length === 0) {
    return { x: -200, y: -150, w: 400, h: 300 };
  }

  const margin = 40;
  const minX = Math.min(...boxes.map((box) => box.x)) - margin;
  const minY = Math.min(...boxes.map((box) => box.y)) - margin;
  const maxX = Math.max(...boxes.map((box) => box.x + box.width)) + margin;
  const maxY = Math.max(...boxes.map((box) => box.y + box.height)) + margin;
  return { x: minX, y: minY, w: maxX - minX, h: maxY - minY };
}

export type { C4Model };
