import { anchorsBetween, midpointOf, straightPath, type ConnectorBox } from "@client/canvas/connectors";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { create } from "@bufbuild/protobuf";
import { EmptySchema } from "@bufbuild/protobuf/wkt";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextSelectionAction, ContextSelectionSchema, ContextSourceSchema } from "@client/generated/context_pb";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { TOOLBOX_DRAG_TYPE, useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { ContextSelection, ContextShortcut } from "@client/generated/context_pb";
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
const VIEW_REPORT_DEBOUNCE_MS = 200;

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
  // actions: the menu shows the backend's answer, never a guess.
  const [menuPosition, setMenuPosition] = useState<{ x: number; y: number } | null>(null);
  const pendingMenuRef = useRef<{ id: string; position: { x: number; y: number } } | null>(null);
  const selectionKey = innermostKey(selection);
  useEffect(() => {
    const pending = pendingMenuRef.current;
    if (pending && selectionKey === `element:${pending.id}`) {
      pendingMenuRef.current = null;
      setMenuPosition(pending.position);
    }
  }, [selectionKey, actions]);

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

  const reportViewRef = useRef(reportView);
  reportViewRef.current = reportView;
  const viewKey = `${effectiveView.x},${effectiveView.y},${effectiveView.w},${effectiveView.h}`;
  useEffect(() => {
    if (loading || failed) {
      return;
    }

    const timer = setTimeout(
      () => reportViewRef.current(shownRectOf(viewRef.current, surfaceRef.current)),
      VIEW_REPORT_DEBOUNCE_MS,
    );
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [viewKey, loading, failed]);

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
    select(nodeSelection(entryId, path, node.id));
  };

  const onNodePointerDown = (node: C4Node, event: React.MouseEvent) => {
    if (event.button !== 0) {
      return; // a right-click is the menu's gesture, not a drag
    }

    dragRef.current = { id: node.id, clientX: event.clientX, clientY: event.clientY, x: node.x, y: node.y, moved: false, dx: 0, dy: 0 };
  };

  /** Right-click an element: select it with the menu gesture, open the menu on the push. */
  const onNodeContextMenu = (node: C4Node, event: React.MouseEvent) => {
    event.preventDefault();
    setFocusedId(node.id);
    surfaceRef.current?.focus();
    if (selectionKey === `element:${node.id}` && actions.length > 0) {
      pendingMenuRef.current = null;
      setMenuPosition({ x: event.clientX, y: event.clientY });
      return;
    }

    pendingMenuRef.current = { id: node.id, position: { x: event.clientX, y: event.clientY } };
    select(nodeSelection(entryId, path, node.id, ContextSelectionAction.CONTEXT_MENU));
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
    void executeAction(actionId, create(ContextSourceSchema, { source: { case: "elementId", value: { value: node.id } } }));
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

    const shortcut = shortcutFor(event);
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    // The backend holds the key-to-action table; the canvas forwards the keystroke as data.
    void executeShortcut(shortcut, create(ContextSourceSchema, { source: { case: "elementId", value: { value: focusedId } } }));
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

          {/* The backend's own actions for what is selected - rendered, never invented. */}
          <ContextMenu
            open={menuPosition !== null}
            groups={toMenuGroups(actions, (action) => {
              setMenuPosition(null);
              void executeAction(action.id);
            })}
            position={menuPosition ?? { x: 0, y: 0 }}
            onClose={() => setMenuPosition(null)}
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
  const halfWidth = width / 2;
  const halfHeight = height / 2;
  const background = style?.background ?? "#1168bd";
  const color = style?.color ?? "#ffffff";
  const shape = style?.shape ?? "RoundedBox";

  return (
    <g
      className={`c4-node${focused ? " c4-node-focused" : ""}${dropTarget ? " c4-node-drop-target" : ""}${offset ? " c4-node-dragging" : ""}`}
      transform={`translate(${node.x + (offset?.dx ?? 0)} ${node.y + (offset?.dy ?? 0)})`}
      onClick={onSelect}
      onMouseDown={onPointerDown}
      onContextMenu={onContextMenu}
      onDragOver={onDragOver}
      onDrop={onDrop}
      role="button"
      aria-label={name}
    >
      {shape === "Person" ? (
        <>
          {/* The person shape: a head above the box, as the reference diagrams draw it. */}
          <circle cx={0} cy={-halfHeight - 8} r={10} fill={background} />
          <rect x={-halfWidth} y={-halfHeight} width={width} height={height} rx={8} fill={background} />
        </>
      ) : shape === "Cylinder" ? (
        <>
          {/* A data store, drawn as the cylinder the notation uses for one. */}
          <rect x={-halfWidth} y={-halfHeight + 6} width={width} height={height - 12} fill={background} />
          <ellipse cx={0} cy={-halfHeight + 6} rx={halfWidth} ry={6} fill={background} />
          <ellipse cx={0} cy={halfHeight - 6} rx={halfWidth} ry={6} fill={background} />
        </>
      ) : (
        <rect
          x={-halfWidth}
          y={-halfHeight}
          width={width}
          height={height}
          rx={shape === "Box" ? 0 : 8}
          fill={background}
        />
      )}

      <text className="c4-node-name" y={-halfHeight + 22} textAnchor="middle" fill={color}>
        {name}
      </text>
      <text className="c4-node-type" y={-halfHeight + 38} textAnchor="middle" fill={color}>
        {typeLine}
      </text>
      {description && (
        <text className="c4-node-description" y={-halfHeight + 58} textAnchor="middle" fill={color}>
          {description}
        </text>
      )}
    </g>
  );
}

/**
 * One relationship: a dashed, unidirectional arrow labelled with what it is for and, where the
 * model says so, the technology it uses - which is what a Container diagram exists to show.
 */
function C4RelationshipShape({ relationship }: { relationship: C4Relationship }) {
  const p = relationship.payload;
  const [from, to] = anchorsBetween(sourceBoxOf(p), destinationBoxOf(p));
  const label = p.technology ? `${p.description} [${p.technology}]` : p.description;
  const order = p.interactionOrder;
  const middle = midpointOf(from, to);

  return (
    <g className="c4-relationship">
      {/* Straight, deliberately. The shared geometry offers a horizontal bezier - what the
          mindmap draws - and C4 does not use it: a C4 relationship joins any two elements in
          any direction, so there is no corridor for a curve to stay inside, and Structurizr
          and the C4 notation both draw these straight. */}
      <path d={straightPath(from, to)} markerEnd="url(#c4-arrow)" />
      {label && (
        <text className="c4-relationship-label" x={middle.x} y={middle.y - 6} textAnchor="middle">
          {order ? `${order}. ${label}` : label}
        </text>
      )}
    </g>
  );
}

/** The dashed rectangle around a system's containers or a container's components. */
function C4BoundaryShape({ boundary }: { boundary: C4BoundaryBox }) {
  const { name, kind, width, height } = boundary.payload;
  return (
    <g className="c4-boundary" transform={`translate(${boundary.x} ${boundary.y})`}>
      <rect x={-width / 2} y={-height / 2} width={width} height={height} rx={6} />
      <text className="c4-boundary-label" x={-width / 2 + 12} y={height / 2 - 12}>
        {name} [{kind}]
      </text>
    </g>
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
 * What the svg actually puts on screen, in canvas units - which is not the viewBox. With the
 * default `preserveAspectRatio` the browser fits the box inside the element and centres it, so
 * the axis with room to spare shows more of the model than the box asked for. Reporting the
 * bare viewBox would have the backend cull elements the user is looking straight at.
 */
export function shownRectOf(box: ViewBox, surface: SVGSVGElement | null) {
  const rect = surface?.getBoundingClientRect();
  if (rect === undefined || rect.width <= 0 || rect.height <= 0 || box.w <= 0 || box.h <= 0) {
    return { minX: box.x, minY: box.y, maxX: box.x + box.w, maxY: box.y + box.h };
  }

  const scale = Math.min(rect.width / box.w, rect.height / box.h);
  const shownWidth = rect.width / scale;
  const shownHeight = rect.height / scale;
  const centerX = box.x + box.w / 2;
  const centerY = box.y + box.h / 2;
  return {
    minX: centerX - shownWidth / 2,
    minY: centerY - shownHeight / 2,
    maxX: centerX + shownWidth / 2,
    maxY: centerY + shownHeight / 2,
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

/** The nested `file -> element` selection a canvas click reports. */
/** A structural key as data. The backend holds the key-to-action table, not this canvas. */
function shortcutFor(event: React.KeyboardEvent): ContextShortcut | null {
  const structural = ["F2", "Delete", "Insert"];
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

function nodeSelection(
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

export type { C4Model };
