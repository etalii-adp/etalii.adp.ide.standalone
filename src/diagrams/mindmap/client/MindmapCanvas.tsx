import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { create } from "@bufbuild/protobuf";
import { EmptySchema } from "@bufbuild/protobuf/wkt";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextSelectionAction, ContextSelectionSchema, ContextSourceSchema } from "@client/generated/context_pb";
import type { ContextSelection, ContextShortcut } from "@client/generated/context_pb";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { useRegisterDiagramView, type DiagramViewControls } from "@client/shell/panels/DiagramViewContext";
import { TOOLBOX_DRAG_TYPE, useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { isFolded, type MindmapElement, type MindmapModel } from "./mindmapModel";
import { useMindmapStream } from "./useMindmapStream";

/** The visible rectangle, in canvas units - the svg viewBox as data. */
interface ViewBox {
  x: number;
  y: number;
  w: number;
  h: number;
}

/** How much one zoom step scales; wheel and ribbon share it so the two feel the same. */
const ZOOM_STEP = 1.25;
/** The view never narrows below or widens beyond these, so no zoom can strand the user. */
const MIN_VIEW_WIDTH = 40;
const MAX_VIEW_WIDTH = 100000;
/** How long the viewport may settle before the backend hears of it (Requirement 11.5). */
const VIEW_REPORT_DEBOUNCE_MS = 200;

/** The diagram file this canvas shows: its project id, and the project-relative path of its `.adp`. */
export interface MindmapCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

const NODE_HALF_WIDTH = 60;
const NODE_HALF_HEIGHT = 16;

/**
 * Renders a mindmap and reports what the user does to the context service. It holds no
 * document state of its own beyond the stream's model and which node has focus; every edit is
 * an `ExecuteAction` (mindmap-diagram Requirements 6, 8, 10). Selection follows what the
 * backend pushes, so a selection made anywhere else moves focus here too.
 */
export function MindmapCanvas({ projectId, entryId, path }: MindmapCanvasProps) {
  const { model, loading, failed, moveElement, reportView } = useMindmapStream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();

  const [focusedId, setFocusedId] = useState<string | undefined>(undefined);
  // A drag in flight: which node it lifted, where it started, and whether it moved far enough
  // to be a drag rather than a wobbly click. Refs, not state - nothing renders differently
  // until the drop lands and the backend's deltas come back.
  const dragRef = useRef<{ id: string; x: number; y: number; moved: boolean } | null>(null);
  const dragJustEndedRef = useRef(false);

  // The user's own view of the map: null means "fit to content", which is also what the
  // canvas opens with and what Fit to View returns to. Panning or zooming takes over from
  // there; the map itself never moves - only this window onto it does.
  const [view, setView] = useState<ViewBox | null>(null);
  const panRef = useRef<{ clientX: number; clientY: number; view: ViewBox; moved: boolean } | null>(null);

  // The node a drag is currently held over - highlighted so the drop's outcome is visible
  // before the button is released. State, not a ref: it is exactly what renders differently.
  const [dropTargetId, setDropTargetId] = useState<string | undefined>(undefined);

  // Where a node drag currently is, in canvas units - what makes the new location visible
  // *during* the drag rather than only on the drop: a ghost of the dragged node follows the
  // pointer, and a dashed connector joins it to the candidate parent, so the tree the drop
  // would produce is on screen before the button is released.
  const [dragPosition, setDragPosition] = useState<{ x: number; y: number } | null>(null);

  // The palette this diagram's type contributes, registered for the Toolbox panel while
  // this canvas is the mounted one - the same discipline the ribbon's View group uses.
  const toolboxItems = useToolboxItems(projectId, path);
  useRegisterDiagramToolbox(toolboxItems);

  // A right-click's menu: opened once the pushed selection for that node arrives with its
  // actions, exactly the explorer's discipline - the menu shows the backend's answer, never
  // a guess (mindmap-diagram Requirement 8.4).
  const [menuPosition, setMenuPosition] = useState<{ x: number; y: number } | null>(null);
  const pendingMenuRef = useRef<{ nodeId: string; position: { x: number; y: number } } | null>(null);
  const selectionKey = innermostKey(selection);
  useEffect(() => {
    const pending = pendingMenuRef.current;
    if (pending && selectionKey === `element:${pending.nodeId}`) {
      pendingMenuRef.current = null;
      setMenuPosition(pending.position);
    }
  }, [selectionKey, actions]);

  // The node the backend says is selected inside this diagram, so focus follows a selection
  // from anywhere - the canvas reacts to the push, not to its own click (Requirement 10.5).
  const selectedNodeId = useMemo(() => nodeOf(selection, path), [selection, path]);
  useEffect(() => {
    if (selectedNodeId && model.elements.has(selectedNodeId)) {
      setFocusedId(selectedNodeId);
    }
  }, [selectedNodeId, model]);

  const surfaceRef = useRef<SVGSVGElement>(null);

  const reportSelection = (element: MindmapElement) => {
    if (dragJustEndedRef.current) {
      // The click that trails a completed drag is the same gesture, not a new selection.
      dragJustEndedRef.current = false;
      return;
    }

    setFocusedId(element.id);
    // Clicking a node must also give the surface the keyboard: browsers do not reliably move
    // DOM focus into an SVG when a child shape is clicked, and without it every shortcut
    // lands wherever focus last was - the explorer, typically (Requirement 8.4).
    surfaceRef.current?.focus();
    // A nested selection: the .adp file, then the node as a DIAGRAM_CANVAS child.
    select(nodeSelection(entryId, path, element));
  };

  /** Clicking the empty canvas deselects: the node loses focus and the backend hears the clear. */
  const onBackgroundClick = (event: React.MouseEvent) => {
    if (event.target !== event.currentTarget) {
      return; // a node's own click; its handler answers
    }

    if (dragJustEndedRef.current) {
      // The click that trails a pan is the same gesture: letting go of a drag of the view
      // must not also throw the selection away.
      dragJustEndedRef.current = false;
      return;
    }

    setFocusedId(undefined);
    select(null);
  };

  const onNodePointerDown = (element: MindmapElement, event: React.MouseEvent) => {
    dragRef.current = { id: element.id, x: event.clientX, y: event.clientY, moved: false };
  };

  /** Right-click on a node: select it with the menu gesture and open the menu on the push. */
  const onNodeContextMenu = (element: MindmapElement, event: React.MouseEvent) => {
    event.preventDefault();
    setFocusedId(element.id);
    surfaceRef.current?.focus();
    if (selectionKey === `element:${element.id}` && actions.length > 0) {
      // Already the pushed selection, actions in hand: the menu opens at once.
      pendingMenuRef.current = null;
      setMenuPosition({ x: event.clientX, y: event.clientY });
      return;
    }

    pendingMenuRef.current = { nodeId: element.id, position: { x: event.clientX, y: event.clientY } };
    select(nodeSelection(entryId, path, element, ContextSelectionAction.CONTEXT_MENU));
  };

  /**
   * While a drag is over this node, it is the drop's outcome - shown, not guessed at. A node
   * inside the dragged branch is no outcome at all (the backend would refuse the move), so it
   * is not offered as one either.
   */
  const onNodePointerOver = (element: MindmapElement) => {
    const drag = dragRef.current;
    const valid = drag?.moved === true && drag.id !== element.id && !isInSubtree(model, element.id, drag.id);
    setDropTargetId(valid ? element.id : undefined);
  };

  /** How many canvas units one screen pixel spans right now - what turns pointer movement into view movement. */
  const unitsPerPixel = (box: ViewBox): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    return rect !== undefined && rect.width > 0 ? box.w / rect.width : 1;
  };

  const onSurfacePointerDown = (event: React.MouseEvent) => {
    if (event.target !== event.currentTarget) {
      return; // a node's own press; its drag handler answers
    }

    panRef.current = { clientX: event.clientX, clientY: event.clientY, view: viewRef.current, moved: false };
  };

  const onSurfacePointerMove = (event: React.MouseEvent) => {
    const drag = dragRef.current;
    if (drag && !drag.moved && Math.hypot(event.clientX - drag.x, event.clientY - drag.y) > 4) {
      drag.moved = true;
    }

    if (drag?.moved) {
      // The ghost follows the pointer, so the prospective location is visible mid-drag.
      setDragPosition(toCanvasPoint(event.clientX, event.clientY, viewRef.current, surfaceRef.current));
    }

    if (event.target === event.currentTarget && dropTargetId !== undefined) {
      setDropTargetId(undefined); // the drag left every node: empty canvas takes no drop
    }

    const pan = panRef.current;
    if (pan) {
      if (!pan.moved && Math.hypot(event.clientX - pan.clientX, event.clientY - pan.clientY) <= 4) {
        return; // still within a click's wobble
      }

      pan.moved = true;
      const scale = unitsPerPixel(pan.view);
      setView({
        ...pan.view,
        x: pan.view.x - (event.clientX - pan.clientX) * scale,
        y: pan.view.y - (event.clientY - pan.clientY) * scale,
      });
    }
  };

  /** The drop half of a drag: releasing over another node moves the dragged one under it. */
  const onNodePointerUp = (element: MindmapElement) => {
    const drag = dragRef.current;
    dragRef.current = null;
    setDropTargetId(undefined);
    setDragPosition(null);
    if (!drag || !drag.moved) {
      return;
    }

    dragJustEndedRef.current = true;
    if (drag.id !== element.id) {
      // Appended as the last child; the backend refuses the moves that make no sense (the
      // root, a node into its own branch) and the change comes back as ordinary deltas.
      void moveElement(drag.id, element.id);
    }
  };

  const onSurfacePointerUp = () => {
    // Released over empty canvas: a node drag simply ends, and a pan is done. A pan that
    // moved suppresses the click that trails it, or letting go would also deselect.
    dragRef.current = null;
    setDropTargetId(undefined);
    setDragPosition(null);
    const pan = panRef.current;
    panRef.current = null;
    if (pan?.moved) {
      dragJustEndedRef.current = true;
    }
  };

  const onSurfacePointerLeave = () => {
    // The pointer left mid-gesture: end both, or a release outside the canvas leaves the
    // next movement panning with a button nobody holds.
    dragRef.current = null;
    panRef.current = null;
    setDropTargetId(undefined);
    setDragPosition(null);
  };

  /** A toolbox entry held over this node: allowed, and shown as the drop's outcome. */
  const onNodeDragOver = (element: MindmapElement, event: React.DragEvent) => {
    if (!event.dataTransfer.types.includes(TOOLBOX_DRAG_TYPE)) {
      return;
    }

    event.preventDefault(); // preventDefault is what permits the drop here
    event.dataTransfer.dropEffect = "copy";
    if (dropTargetId !== element.id) {
      setDropTargetId(element.id);
    }
  };

  /**
   * A toolbox entry dropped on a node: the drag carries the backend's own action id, and
   * dropping executes it against this node - the same add-child flow, prompt and undo the
   * context menu and the Insert key already share. The panel told the canvas nothing but
   * the id; what it means stays the backend's business.
   */
  const onNodeDrop = (element: MindmapElement, event: React.DragEvent) => {
    const actionId = event.dataTransfer.getData(TOOLBOX_DRAG_TYPE);
    setDropTargetId(undefined);
    if (!actionId) {
      return;
    }

    event.preventDefault();
    setFocusedId(element.id);
    void executeAction(actionId, create(ContextSourceSchema, { source: { case: "elementId", value: { value: element.id } } }));
  };

  /** A toolbox drag over empty canvas: no node, no drop - the highlight clears. */
  const onSurfaceDragOver = (event: React.DragEvent) => {
    if (event.target === event.currentTarget && dropTargetId !== undefined) {
      setDropTargetId(undefined);
    }
  };

  const onKeyDown = (event: React.KeyboardEvent) => {
    // A key that means something in a text field is left to it - the rename prompt is a real
    // input the shell mounts, so text editing wins there (Requirement 8.2).
    if (!focusedId || isTextTarget(event.target)) {
      return;
    }

    const shortcut = shortcutFor(event);
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    // The backend holds the key->action table; the canvas only forwards the keystroke as data
    // against the focused node's selection (Requirement 8.4).
    void executeShortcut(shortcut, create(ContextSourceSchema, { source: { case: "elementId", value: { value: focusedId } } }));
  };

  const elements = [...model.elements.values()];

  // What the svg actually shows: the user's own view when they panned or zoomed, the
  // content-fitting box otherwise. The ref mirrors it so the stable callbacks below read the
  // current value without re-creating themselves every render.
  const fitBox = useMemo(() => fitBoxOf(elements), [model]);
  const effectiveView = view ?? fitBox;
  const viewRef = useRef(effectiveView);
  viewRef.current = effectiveView;

  /** One zoom step about a point (canvas units); about the view's centre when none is given. */
  const zoomBy = useCallback((factor: number, aboutX?: number, aboutY?: number) => {
    setView(() => {
      const current = viewRef.current;
      const w = Math.min(MAX_VIEW_WIDTH, Math.max(MIN_VIEW_WIDTH, current.w / factor));
      const applied = current.w / w; // what the clamp actually allowed
      const h = current.h / applied;
      const px = aboutX ?? current.x + current.w / 2;
      const py = aboutY ?? current.y + current.h / 2;
      return {
        x: px - (px - current.x) / applied,
        y: py - (py - current.y) / applied,
        w,
        h,
      };
    });
  }, []);

  // The wheel zooms about the pointer. Attached by hand as non-passive: React's synthetic
  // wheel listener cannot preventDefault, and without that every zoom also scrolls the page.
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

  // The ribbon's View group drives whichever canvas is mounted; Fit to View simply hands the
  // window back to the content, which is also how the canvas opens.
  const viewControls = useMemo<DiagramViewControls>(
    () => ({
      zoomIn: () => zoomBy(ZOOM_STEP),
      zoomOut: () => zoomBy(1 / ZOOM_STEP),
      fitToView: () => setView(null),
    }),
    [zoomBy],
  );
  useRegisterDiagramView(viewControls);

  // What the user can see, reported once it settles, so the backend delivers what falls
  // inside it and not the whole map (Requirement 11.5). The ref keeps the report out of the
  // effect's dependencies - it is the box that matters, not the function's identity.
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

  if (failed) {
    // The backend gave a permanent answer - the file is gone, moved, or unroutable. The tab
    // stays, closable as ever; re-activating the diagram under its new location opens a fresh
    // one (diagram-workspace-tabs Requirement 5).
    return (
      <div className="mindmap-canvas" data-testid="mindmap-canvas">
        <div className="mindmap-canvas-unavailable" role="alert">
          This diagram is no longer available at {path.join("/")}.
        </div>
      </div>
    );
  }

  return (
    <div className="mindmap-canvas" data-testid="mindmap-canvas">
      {loading ? (
        <div className="mindmap-canvas-loading" role="status">
          Loading…
        </div>
      ) : (
        <svg
          ref={surfaceRef}
          className="mindmap-canvas-surface"
          viewBox={`${effectiveView.x} ${effectiveView.y} ${effectiveView.w} ${effectiveView.h}`}
          tabIndex={0}
          role="tree"
          aria-label="Mind map"
          onKeyDown={onKeyDown}
          onClick={onBackgroundClick}
          onMouseDown={onSurfacePointerDown}
          onMouseMove={onSurfacePointerMove}
          onMouseUp={onSurfacePointerUp}
          onMouseLeave={onSurfacePointerLeave}
          onDragOver={onSurfaceDragOver}
        >
          {/* The connectors first, so every curve runs under the node boxes. Drawn from the
              payload's parent id - each node knows whose child it is, and nothing more is
              needed to see the tree. */}
          {elements.map((element) => {
            const parent = element.payload.parentId ? model.elements.get(element.payload.parentId) : undefined;
            return parent === undefined ? null : <MindmapEdge key={`edge-${element.id}`} parent={parent} child={element} />;
          })}
          {elements.map((element) => (
            <MindmapNode
              key={element.id}
              element={element}
              focused={element.id === focusedId}
              folded={isFolded(model, element.id)}
              dropTarget={element.id === dropTargetId}
              dragging={dragPosition !== null && element.id === dragRef.current?.id}
              onSelect={() => reportSelection(element)}
              onPointerDown={(event) => onNodePointerDown(element, event)}
              onPointerUp={() => onNodePointerUp(element)}
              onPointerOver={() => onNodePointerOver(element)}
              onContextMenu={(event) => onNodeContextMenu(element, event)}
              onDragOver={(event) => onNodeDragOver(element, event)}
              onDrop={(event) => onNodeDrop(element, event)}
            />
          ))}
          {/* The drag's live preview, last so it rides above everything: a dashed connector
              from the candidate parent to a ghost of the dragged node at the pointer. The new
              location is on screen during the drag, not only once the drop lands. */}
          {dragPreviewOf(model, dragRef.current?.id, dropTargetId, dragPosition)}
        </svg>
      )}
      {/* The node's right-click menu: the same shared menu the explorer uses, filled with the
          actions the backend pushed for this very selection - never a client-side guess. */}
      <ContextMenu
        open={menuPosition !== null}
        groups={toMenuGroups(actions, (action) => {
          setMenuPosition(null);
          void executeAction(action.id);
        })}
        position={menuPosition ?? { x: 0, y: 0 }}
        onClose={() => setMenuPosition(null)}
      />
    </div>
  );
}

/** The node's half-size from its payload: the box the backend measured, or the old fixed guess for a payload without one. */
function halfSizeOf(element: MindmapElement): { halfWidth: number; halfHeight: number } {
  return {
    halfWidth: element.payload.width > 0 ? element.payload.width / 2 : NODE_HALF_WIDTH,
    halfHeight: element.payload.height > 0 ? element.payload.height / 2 : NODE_HALF_HEIGHT,
  };
}

/**
 * The connector from a child to its parent: a horizontal cubic bezier that leaves the
 * parent's near side at its vertical middle and arrives at the child's near side - never a
 * centre-to-centre line cutting through boxes. The control points sit halfway, which keeps
 * the whole curve inside the gap corridor between the two columns, clear of every sibling.
 */
function MindmapEdge({ parent, child, preview = false }: { parent: MindmapElement; child: MindmapElement; preview?: boolean }) {
  const parentHalf = halfSizeOf(parent);
  const childHalf = halfSizeOf(child);
  const childOnRight = child.x >= parent.x;
  const startX = childOnRight ? parent.x + parentHalf.halfWidth : parent.x - parentHalf.halfWidth;
  const endX = childOnRight ? child.x - childHalf.halfWidth : child.x + childHalf.halfWidth;
  const midX = (startX + endX) / 2;

  return (
    <path
      className={`mindmap-edge${preview ? " mindmap-edge-preview" : ""}`}
      d={`M ${startX} ${parent.y} C ${midX} ${parent.y}, ${midX} ${child.y}, ${endX} ${child.y}`}
    />
  );
}

interface MindmapNodeProps {
  element: MindmapElement;
  focused: boolean;
  folded: boolean;
  dropTarget: boolean;
  dragging: boolean;
  onSelect: () => void;
  onPointerDown: (event: React.MouseEvent) => void;
  onPointerUp: () => void;
  onPointerOver: () => void;
  onContextMenu: (event: React.MouseEvent) => void;
  onDragOver: (event: React.DragEvent) => void;
  onDrop: (event: React.DragEvent) => void;
}

function MindmapNode({ element, focused, folded, dropTarget, dragging, onSelect, onPointerDown, onPointerUp, onPointerOver, onContextMenu, onDragOver, onDrop }: MindmapNodeProps) {
  const { text, notes, hasChildren, link } = element.payload;
  const { halfWidth, halfHeight } = halfSizeOf(element);
  const indicators = [notes ? "\u2022" : "", link ? "\u2197" : "", folded && hasChildren ? "\u2295" : ""].join(" ").trim();
  const classes = [
    "mindmap-node",
    focused ? "mindmap-node-focused" : "",
    dropTarget ? "mindmap-node-drop-target" : "",
    dragging ? "mindmap-node-dragging" : "",
  ].filter(Boolean).join(" ");

  return (
    <g
      className={classes}
      transform={`translate(${element.x} ${element.y})`}
      onClick={onSelect}
      onMouseDown={onPointerDown}
      onMouseUp={onPointerUp}
      onMouseOver={onPointerOver}
      onContextMenu={onContextMenu}
      onDragOver={onDragOver}
      onDrop={onDrop}
      role="treeitem"
      aria-selected={focused}
    >
      <rect x={-halfWidth} y={-halfHeight} width={halfWidth * 2} height={halfHeight * 2} rx={6} />
      <text textAnchor="middle" dominantBaseline="central">
        {text || " "}
      </text>
      {indicators && (
        <text className="mindmap-node-indicators" x={halfWidth - 4} y={-halfHeight + 4} textAnchor="end">
          {indicators}
        </text>
      )}
    </g>
  );
}

/**
 * The live preview of a node drag: a ghost of the dragged node at the pointer, joined to
 * the candidate parent by a dashed connector where one is under the pointer. Pointer-events
 * off, so the ghost never steals the hover that decides the drop target under it.
 */
function dragPreviewOf(
  model: MindmapModel,
  draggedId: string | undefined,
  dropTargetId: string | undefined,
  position: { x: number; y: number } | null,
) {
  const dragged = draggedId !== undefined ? model.elements.get(draggedId) : undefined;
  if (dragged === undefined || position === null) {
    return null;
  }

  const ghost: MindmapElement = { ...dragged, x: position.x, y: position.y };
  const parent = dropTargetId !== undefined ? model.elements.get(dropTargetId) : undefined;
  const { halfWidth, halfHeight } = halfSizeOf(ghost);

  return (
    <g className="mindmap-drag-preview" pointerEvents="none" data-testid="mindmap-drag-preview">
      {parent !== undefined && <MindmapEdge parent={parent} child={ghost} preview />}
      <g className="mindmap-node mindmap-node-ghost" transform={`translate(${ghost.x} ${ghost.y})`}>
        <rect x={-halfWidth} y={-halfHeight} width={halfWidth * 2} height={halfHeight * 2} rx={6} />
        <text textAnchor="middle" dominantBaseline="central">
          {ghost.payload.text || " "}
        </text>
      </g>
    </g>
  );
}

/** Whether `candidateId` sits inside the branch rooted at `rootId` - itself included. */
function isInSubtree(model: MindmapModel, candidateId: string, rootId: string): boolean {
  let cursor: string | undefined = candidateId;
  const seen = new Set<string>(); // a defensive stop; the model never actually cycles
  while (cursor !== undefined && !seen.has(cursor)) {
    if (cursor === rootId) {
      return true;
    }
    seen.add(cursor);
    cursor = model.elements.get(cursor)?.payload.parentId || undefined;
  }
  return false;
}

/**
 * A client point in canvas units, inverted through the same "meet" fitting `shownRectOf`
 * describes - the plain viewBox proportion would drift wherever the svg's aspect ratio
 * differs from the box's, and the ghost would float beside the pointer instead of under it.
 */
function toCanvasPoint(clientX: number, clientY: number, box: ViewBox, surface: SVGSVGElement | null): { x: number; y: number } {
  const rect = surface?.getBoundingClientRect();
  if (rect === undefined || rect.width <= 0 || rect.height <= 0) {
    return { x: box.x, y: box.y };
  }

  const shown = shownRectOf(box, surface);
  return {
    x: shown.minX + ((clientX - rect.left) / rect.width) * (shown.maxX - shown.minX),
    y: shown.minY + ((clientY - rect.top) / rect.height) * (shown.maxY - shown.minY),
  };
}

/** The node id the pushed selection names inside this diagram, or undefined. */
function nodeOf(selection: ContextSelection | null, path: readonly string[]): string | undefined {
  let cursor: ContextSelection | undefined = selection ?? undefined;
  // Walk the chain; the element id, wherever it sits, is the selected node.
  while (cursor) {
    if (cursor.id?.source.case === "elementId") {
      return cursor.id.source.value.value;
    }
    cursor = cursor.detail.case === "child" ? cursor.detail.value : undefined;
  }
  void path;
  return undefined;
}

/** The nested `file -> node` selection a canvas click reports (Requirement 10.1). */
function nodeSelection(entryId: Uint8Array, path: readonly string[], element: MindmapElement, gesture?: ContextSelectionAction): ContextSelection {
  const child = create(ContextSelectionSchema, {
    source: 2, // DIAGRAM_CANVAS
    id: { source: { case: "elementId", value: { value: element.id } } },
    // Empty asks the backend to fill it in: the node's path is its whole text chain from the
    // root, which the resolver derives and echoes back - a canvas that sent only the node's
    // own text was rejected for exactly that partial path (found by the manual pass; the
    // root's one-segment chain had masked it in every earlier check).
    path: { segments: [] },
    detail: gesture === undefined ? { case: "none", value: create(EmptySchema) } : { case: "action", value: gesture },
  });

  return create(ContextSelectionSchema, {
    source: 1, // EXPLORER-origin file, selected on the canvas's behalf
    id: { source: { case: "entryId", value: { value: entryId } } },
    path: { segments: [...path] },
    detail: { case: "child", value: child },
  });
}

/** A keyboard event as a backend shortcut, or null for a key that carries no structural meaning. */
function shortcutFor(event: React.KeyboardEvent): ContextShortcut | null {
  const structural = ["Insert", "Enter", "F2", "Delete", " ", "Tab"];
  if (!structural.includes(event.key)) {
    return null;
  }

  // Tab is the XMind convention for "add child"; the backend's child action carries Insert, so
  // the alias is resolved to it here - a key-to-key mapping, never a key-to-action one.
  const key = event.key === "Tab" ? "Insert" : event.key;
  return { key, ctrl: event.ctrlKey, shift: event.shiftKey, alt: event.altKey, meta: event.metaKey } as ContextShortcut;
}

/**
 * What the svg actually puts on screen, in canvas units - which is not the viewBox. With the
 * default `preserveAspectRatio` ("xMidYMid meet") the browser scales the box to fit inside the
 * element and centres it, so whichever axis has room left over shows more of the map than the
 * box asked for. Reporting the bare viewBox therefore understates the visible area, and the
 * backend culls nodes sitting in that margin while the user is looking straight at them.
 *
 * Without a laid-out surface - jsdom, or before the first measure - the box is the best answer
 * available and is reported unchanged.
 */
function shownRectOf(box: ViewBox, surface: SVGSVGElement | null): { minX: number; minY: number; maxX: number; maxY: number } {
  const rect = surface?.getBoundingClientRect();
  if (rect === undefined || rect.width <= 0 || rect.height <= 0 || box.w <= 0 || box.h <= 0) {
    return { minX: box.x, minY: box.y, maxX: box.x + box.w, maxY: box.y + box.h };
  }

  // "meet" scales by whichever axis is the tighter fit; the other one then spans more units.
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

/** The box that fits every element with a margin - what the canvas opens with and Fit to View returns to. */
function fitBoxOf(elements: readonly MindmapElement[]): ViewBox {
  if (elements.length === 0) {
    return { x: -200, y: -150, w: 400, h: 300 };
  }

  const lefts = elements.map((element) => element.x - halfSizeOf(element).halfWidth);
  const rights = elements.map((element) => element.x + halfSizeOf(element).halfWidth);
  const tops = elements.map((element) => element.y - halfSizeOf(element).halfHeight);
  const bottoms = elements.map((element) => element.y + halfSizeOf(element).halfHeight);
  const minX = Math.min(...lefts) - 20;
  const minY = Math.min(...tops) - 20;
  const maxX = Math.max(...rights) + 20;
  const maxY = Math.max(...bottoms) + 20;
  return { x: minX, y: minY, w: maxX - minX, h: maxY - minY };
}

// Kept exported for the panel and tests; the model type is re-exported so consumers need one import.
export type { MindmapModel };

/** Whether an event's target is a text input the browser should handle instead of the canvas. */
function isTextTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  const tag = target.tagName.toLowerCase();
  return tag === "input" || tag === "textarea" || target.isContentEditable;
}
