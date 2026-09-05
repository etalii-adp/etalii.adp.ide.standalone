import type { ConnectorBox } from "@client/canvas/connectors";
import { BezierConnection } from "@client/canvas/connections/bezier/BezierConnection";
import { CenteredBoxElement } from "@client/canvas/elements/centered-box/CenteredBoxElement";
import { CanvasScrollbars } from "@client/canvas/scroll/CanvasScrollbars";
import { scrollExtentOf } from "@client/canvas/scroll/scrollGeometry";
import { elementSelectionOf, elementSourceOf, selectedElementIdOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { InlineLabelEditor } from "@client/canvas/label/InlineLabelEditor";
import { useElementContextMenu } from "@client/canvas/useElementContextMenu";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useViewReport } from "@client/diagrams/useViewReport";
import { shownRectOf } from "@client/diagrams/viewReport";
import { innermostKey, useContextConnection, useContextPrompt, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { useRegisterDiagramView, type DiagramViewControls } from "@client/shell/panels/DiagramViewContext";
import { TOOLBOX_DRAG_TYPE, useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useRegisterInlineLabelPlacement, type LabelPlacement } from "@client/shell/panels/InlineLabelPlacementContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { isFolded, type MindmapElement, type MindmapModel } from "./mindmapModel";
import { useMindmapStream } from "./useMindmapStream";
import { usePointerGesture, type PointerPressWiring } from "@client/canvas/gesture/usePointerGesture";

/** The visible rectangle, in canvas units - the svg viewBox as data. */
interface ViewBox {
  x: number;
  y: number;
  w: number;
  h: number;
}

/**
 * What a press lands on. Threaded through the shared arbiter untouched: the arbiter decides
 * click-or-drag at the gesture's end, and this type is how its verdict comes back knowing
 * what the gesture was about.
 */
type MindmapPressTarget =
  | { kind: "node"; element: MindmapElement }
  | { kind: "background"; view: ViewBox };

/** How much one zoom step scales; wheel and ribbon share it so the two feel the same. */
const ZOOM_STEP = 1.25;
/** The view never narrows below or widens beyond these, so no zoom can strand the user. */
const MIN_VIEW_WIDTH = 40;
const MAX_VIEW_WIDTH = 100000;
/** How long the viewport may settle before the backend hears of it (Requirement 11.5). */

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

  // The user's own view of the map: null means "fit to content", which is also what the
  // canvas opens with and what Fit to View returns to. Panning or zooming takes over from
  // there; the map itself never moves - only this window onto it does.
  const [view, setView] = useState<ViewBox | null>(null);

  // The node a drag is currently held over - highlighted so the drop's outcome is visible
  // before the button is released. State, not a ref: it is exactly what renders differently.
  const [dropTargetId, setDropTargetId] = useState<string | undefined>(undefined);

  // Where a node drag currently is, in canvas units, and which node it lifted - what makes
  // the new location visible *during* the drag rather than only on the drop: a ghost of the
  // dragged node follows the pointer, and a dashed connector joins it to the candidate
  // parent, so the tree the drop would produce is on screen before the button is released.
  const [dragPosition, setDragPosition] = useState<{ id: string; x: number; y: number } | null>(null);

  // The palette this diagram's type contributes, registered for the Toolbox panel while
  // this canvas is the mounted one - the same discipline the ribbon's View group uses.
  const toolboxItems = useToolboxItems(projectId, path);
  useRegisterDiagramToolbox(toolboxItems);

  // A right-click's menu: opened once the pushed selection for that node arrives with its
  // actions, exactly the explorer's discipline - the menu shows the backend's answer, never
  // a guess (mindmap-diagram Requirement 8.4).
  const selectionKey = innermostKey(selection);
  const { menuPosition, openMenuAt, closeMenu } = useElementContextMenu(selectionKey, actions.length > 0, (id) =>
    select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
  );

  // The node the backend says is selected inside this diagram, so focus follows a selection
  // from anywhere - the canvas reacts to the push, not to its own click (Requirement 10.5).
  const selectedNodeId = useMemo(() => selectedElementIdOf(selection), [selection]);
  useEffect(() => {
    if (selectedNodeId && model.elements.has(selectedNodeId)) {
      setFocusedId(selectedNodeId);
    }
  }, [selectedNodeId, model]);

  const surfaceRef = useRef<SVGSVGElement>(null);

  // Where a node's label is, for the shell's inline editor. Memoized on the model rather than
  // made permanently stable: re-registering when the model changes is what lets the shell notice
  // that the node being edited has gone, which is otherwise a silent abandon.
  //
  // The spec also has this answer null while a selection holds several members, since there is
  // then no single label to replace (Requirement 6.3). There is nothing to check yet: this
  // canvas has one focused node and the backend's selection carries one element. When the
  // multi-select specification lands, the guard belongs here, in this one function, rather than
  // in the editor or the shell - both of which would then need to learn what a selection is.
  const placementOfNode = useCallback(
    (elementId: string): LabelPlacement | null => {
      const element = model.elements.get(elementId);
      if (element === undefined) {
        return null;
      }

      // A node box is positioned by its centre; the editor wants the rectangle's corner.
      const box = boxOf(element);
      return {
        x: box.x - box.width / 2,
        y: box.y - box.height / 2,
        width: box.width,
        height: box.height,
        text: element.payload.text,
      };
    },
    [model],
  );
  useRegisterInlineLabelPlacement(placementOfNode);

  // The prompt the shell is holding. This canvas draws the editor for it only when the backend
  // marked it as editing a visible label and the node is one this canvas has - the same question
  // the shell asks before standing down, through the same function, so they cannot disagree.
  const { prompt, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel } = useContextPrompt();
  const editingNodeId = inlineLabelElementIdOf(prompt);
  const editingPlacement = editingNodeId === null ? null : placementOfNode(editingNodeId);

  const returnFocusToSurface = useCallback(() => surfaceRef.current?.focus(), []);

  /**
   * Ends an open inline edit before a gesture that moves the canvas begins, so a drag or a pan
   * never lands on an ambiguous editing state (Requirement 5.5). Taking the focus is the whole
   * mechanism: the editor commits on blur, so moving focus to the surface commits it, and the
   * commit rule stays in one place instead of being reimplemented per gesture.
   */
  const endInlineEditBeforeGesture = () => {
    if (editingPlacement !== null) {
      surfaceRef.current?.focus();
    }
  };

  const reportSelection = (element: MindmapElement) => {
    setFocusedId(element.id);
    // Clicking a node must also give the surface the keyboard: browsers do not reliably move
    // DOM focus into an SVG when a child shape is clicked, and without it every shortcut
    // lands wherever focus last was - the explorer, typically (Requirement 8.4).
    surfaceRef.current?.focus();
    // A nested selection: the .adp file, then the node as a DIAGRAM_CANVAS child.
    select(elementSelectionOf(entryId, path, element.id));
  };

  /** Right-click on a node: select it with the menu gesture and open the menu on the push. */
  const onNodeContextMenu = (element: MindmapElement, event: React.MouseEvent) => {
    setFocusedId(element.id);
    surfaceRef.current?.focus();
    openMenuAt(event, element.id);
  };

  /** How many canvas units one screen pixel spans right now - what turns pointer movement into view movement. */
  const unitsPerPixel = (box: ViewBox): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    return rect !== undefined && rect.width > 0 ? box.w / rect.width : 1;
  };

  /**
   * How many canvas units one pointer pixel spans, through the same "meet" fitting
   * `shownRectOf` describes - the bare viewBox proportion drifts wherever the svg's aspect
   * ratio differs from the box's, and the ghost would trail beside the pointer instead of
   * riding under it.
   */
  const shownUnitsPerPixel = (): number => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    if (rect === undefined || rect.width <= 0) {
      return 1;
    }

    const shown = shownRectOf(viewRef.current, surfaceRef.current);
    return (shown.maxX - shown.minX) / rect.width;
  };

  /**
   * Where a drop at `point` (canvas units) would land: the topmost node under it that the
   * backend could accept. The dragged node itself, or a node inside its branch, is no
   * outcome at all (the backend would refuse the move), so it is not offered as one either.
   */
  const dropTargetAt = (draggedId: string, point: { x: number; y: number }): MindmapElement | undefined => {
    const candidates = [...model.elements.values()];
    for (let i = candidates.length - 1; i >= 0; i--) {
      const candidate = candidates[i];
      const box = boxOf(candidate);
      if (Math.abs(point.x - box.x) > box.width / 2 || Math.abs(point.y - box.y) > box.height / 2) {
        continue;
      }

      const valid = candidate.id !== draggedId && !isInSubtree(model, candidate.id, draggedId);
      return valid ? candidate : undefined;
    }
    return undefined;
  };

  /**
   * One arbiter decides click-or-drag for every press - node and background alike - at the
   * gesture's end, from what the gesture itself recorded. Nothing here listens to `click`:
   * that trailing event was the wrong witness, and the one-shot flag that suppressed it was
   * the latch that swallowed the next legitimate selection - or, armed for a pan but not for
   * a node drag, let the trailing background click throw a selection away (the
   * selection-after-drag specification traces both faces).
   */
  const gesture = usePointerGesture<MindmapPressTarget>({
    onPress: (target) => {
      if (target.kind === "background") {
        // Pressing the empty canvas deselects: focus clears and the backend hears it.
        setFocusedId(undefined);
        select(null);
        return;
      }

      reportSelection(target.element);
    },
    onDragMove: (target, dx, dy) => {
      if (target.kind === "background") {
        // Panning measures against the view captured at press: the view moves under this
        // very gesture, and measuring against the moving thing would compound each step.
        const scale = unitsPerPixel(target.view);
        setView({ ...target.view, x: target.view.x - dx * scale, y: target.view.y - dy * scale });
        return;
      }

      // The ghost rides the pointer's own movement, so the prospective location is visible
      // mid-drag; the candidate parent is whichever node the ghost's centre is over.
      const scale = shownUnitsPerPixel();
      const point = { x: target.element.x + dx * scale, y: target.element.y + dy * scale };
      setDragPosition({ id: target.element.id, ...point });
      setDropTargetId(dropTargetAt(target.element.id, point)?.id);
    },
    onDragEnd: (target, dx, dy) => {
      if (target.kind === "background") {
        return; // the pan already happened, move by move; letting go changes nothing more
      }

      setDragPosition(null);
      setDropTargetId(undefined);
      const scale = shownUnitsPerPixel();
      const parent = dropTargetAt(target.element.id, { x: target.element.x + dx * scale, y: target.element.y + dy * scale });
      if (parent !== undefined) {
        // Appended as the last child; the backend refuses the moves that make no sense (the
        // root, a node into its own branch) and the change comes back as ordinary deltas.
        void moveElement(target.element.id, parent.id);
      }
      // Released over empty canvas: the drag simply ends - neither a selection nor a
      // deselection follows.
    },
    onDragAbandon: (target) => {
      if (target.kind === "node") {
        setDragPosition(null);
        setDropTargetId(undefined);
      }
    },
  });

  /** A node's wiring, with the open inline edit committed first (Requirement 5.5). */
  const pressWiring = (target: MindmapPressTarget): PointerPressWiring => {
    const wiring = gesture.press(target);
    return {
      ...wiring,
      onPointerDown: (event: React.PointerEvent) => {
        endInlineEditBeforeGesture();
        wiring.onPointerDown(event);
      },
    };
  };

  /**
   * The surface's wiring. The inline edit is committed only when the press is really the
   * surface's own: the editor is a child of the svg, and ending the edit on a press that
   * merely bubbled up through it would commit the very edit being clicked into.
   */
  const backgroundWiring = (target: MindmapPressTarget): PointerPressWiring => {
    const wiring = gesture.background(target);
    return {
      ...wiring,
      onPointerDown: (event: React.PointerEvent) => {
        if (event.target === event.currentTarget) {
          endInlineEditBeforeGesture();
        }
        wiring.onPointerDown(event);
      },
    };
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
    void executeAction(actionId, elementSourceOf(element.id));
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

    // Tab is the XMind convention for "add child"; the backend's child action carries Insert,
    // so the alias resolves to it here - a key-to-key mapping, never a key-to-action one.
    const shortcut = structuralShortcutFor(event, ["Insert", "Enter", "F2", "Delete", " ", "Tab"], { Tab: "Insert" });
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    // The backend holds the key->action table; the canvas only forwards the keystroke as data
    // against the focused node's selection (Requirement 8.4).
    void executeShortcut(shortcut, elementSourceOf(focusedId));
  };

  const elements = [...model.elements.values()];

  // What the svg actually shows: the user's own view when they panned or zoomed, the
  // content-fitting box otherwise. The ref mirrors it so the stable callbacks below read the
  // current value without re-creating themselves every render.
  const fitBox = useMemo(() => fitBoxOf(elements), [model]);
  const effectiveView = view ?? fitBox;
  const viewRef = useRef(effectiveView);
  viewRef.current = effectiveView;

  // What the bars describe: the effective view against the content's extent - the same box
  // Fit to View uses, padded the shared way, so a fitted map shows thumbs claiming nearly
  // the whole track and inviting no pan (Requirement 1.4).
  const horizontalExtent = scrollExtentOf(fitBox.x, fitBox.x + fitBox.w, { factor: 0.5 });
  const verticalExtent = scrollExtentOf(fitBox.y, fitBox.y + fitBox.h, { factor: 0.5 });

  // A thumb drag pans: it takes over from the fitted state exactly as dragging the canvas
  // does, by writing a concrete box derived from what is currently shown. Zoom stays on the
  // wheel and the ribbon - a thumb never changes w or h.
  const onScrollPan = useCallback((horizontalStart: number, verticalStart: number) => {
    const current = viewRef.current;
    setView({ x: horizontalStart, y: verticalStart, w: current.w, h: current.h });
  }, []);

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
  useViewReport({
    view: effectiveView,
    report: reportView,
    convert: () => shownRectOf(viewRef.current, surfaceRef.current),
    ready: !loading && !failed,
  });

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
          {...backgroundWiring({ kind: "background", view: effectiveView })}
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
              dragging={dragPosition?.id === element.id}
              press={pressWiring({ kind: "node", element })}
              onContextMenu={(event) => onNodeContextMenu(element, event)}
              onDragOver={(event) => onNodeDragOver(element, event)}
              onDrop={(event) => onNodeDrop(element, event)}
            />
          ))}
          {/* The drag's live preview, last so it rides above everything: a dashed connector
              from the candidate parent to a ghost of the dragged node at the pointer. The new
              location is on screen during the drag, not only once the drop lands. */}
          {dragPreviewOf(model, dragPosition?.id, dropTargetId, dragPosition)}
          {/* Last of all, so the editor is above every node and connector it overlaps. It is
              placed in canvas units, so panning and zooming carry it with its node. */}
          {editingPlacement !== null && (
            <InlineLabelEditor
              placement={editingPlacement}
              onPropose={onProposeLabel}
              onSubmit={onSubmitLabel}
              onCancel={onCancelLabel}
              onReturnFocus={returnFocusToSurface}
            />
          )}
        </svg>
      )}
      {!loading && (
        <CanvasScrollbars
          horizontal={{ viewStart: effectiveView.x, viewSpan: effectiveView.w, ...horizontalExtent }}
          vertical={{ viewStart: effectiveView.y, viewSpan: effectiveView.h, ...verticalExtent }}
          onPan={onScrollPan}
        />
      )}
      {/* The node's right-click menu: the same shared menu the explorer uses, filled with the
          actions the backend pushed for this very selection - never a client-side guess. */}
      <ContextMenu
        open={menuPosition !== null}
        groups={toMenuGroups(actions, (action) => {
          closeMenu();
          void executeAction(action.id);
        })}
        position={menuPosition ?? { x: 0, y: 0 }}
        onClose={closeMenu}
      />
    </div>
  );
}

/** The node's half-size from its payload: the box the backend measured, or the old fixed guess for a payload without one. */
/** An element as the shared connector geometry wants it: a centre, and a real size. */
function boxOf(element: MindmapElement): ConnectorBox {
  return {
    x: element.x,
    y: element.y,
    // A node the client has not measured yet falls back to the nominal size, so a connector
    // drawn on the first frame still lands on an edge rather than on the centre.
    width: element.payload.width > 0 ? element.payload.width : NODE_HALF_WIDTH * 2,
    height: element.payload.height > 0 ? element.payload.height : NODE_HALF_HEIGHT * 2,
  };
}

/** The same size, halved - what the node shapes and the bounds calculation ask for. */
function halfSizeOf(element: MindmapElement): { halfWidth: number; halfHeight: number } {
  const box = boxOf(element);
  return { halfWidth: box.width / 2, halfHeight: box.height / 2 };
}

/**
 * The connector from a child to its parent.
 *
 * The geometry is shared - see `@client/canvas/connectors`. What is mindmap-specific is only
 * the choice: a tree anchors on the sides facing each other and curves horizontally, rather
 * than running centre to centre and cutting through whatever sits between.
 */
function MindmapEdge({ parent, child, preview = false }: { parent: MindmapElement; child: MindmapElement; preview?: boolean }) {
  return (
    <BezierConnection
      from={boxOf(parent)}
      to={boxOf(child)}
      className={`mindmap-edge${preview ? " mindmap-edge-preview" : ""}`}
    />
  );
}

interface MindmapNodeProps {
  element: MindmapElement;
  focused: boolean;
  folded: boolean;
  dropTarget: boolean;
  dragging: boolean;
  /** The arbiter's wiring - one spread carries both what selects and what drags this node. */
  press: PointerPressWiring;
  onContextMenu: (event: React.MouseEvent) => void;
  onDragOver: (event: React.DragEvent) => void;
  onDrop: (event: React.DragEvent) => void;
}

function MindmapNode({ element, focused, folded, dropTarget, dragging, press, onContextMenu, onDragOver, onDrop }: MindmapNodeProps) {
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
    <CenteredBoxElement
      className={classes}
      x={element.x}
      y={element.y}
      halfWidth={halfWidth}
      halfHeight={halfHeight}
      text={text}
      indicators={indicators || undefined}
      indicatorsClassName="mindmap-node-indicators"
      {...press}
      onContextMenu={onContextMenu}
      onDragOver={onDragOver}
      onDrop={onDrop}
      role="treeitem"
      aria-selected={focused}
    />
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
      <CenteredBoxElement
        className="mindmap-node mindmap-node-ghost"
        x={ghost.x}
        y={ghost.y}
        halfWidth={halfWidth}
        halfHeight={halfHeight}
        text={ghost.payload.text}
      />
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

