import { useCallback, useEffect, useMemo, useRef, useState, Component, type ReactNode } from "react";
import { create } from "@bufbuild/protobuf";
import { ToolboxItemSchema, type ToolboxItem } from "@client/generated/diagrams_pb";
import type { ContextActionGroup } from "@client/generated/context_pb";
import {
  arcPath,
  edgePointOf,
  midpointOf,
  orthogonalPath,
  polylinePath,
  quadraticBezierPath,
  splinePath,
  straightPath,
  horizontalBezierPath,
  type ConnectorBox,
  type Point,
} from "../connectors";
import { usePointerGesture, type PointerPressWiring } from "../gesture/usePointerGesture";
import { BoxElement } from "../elements/box/BoxElement";
import { CenteredBoxElement } from "../elements/centered-box/CenteredBoxElement";
import { EllipseElement } from "../elements/ellipse/EllipseElement";
import { FrameElement } from "../elements/frame/FrameElement";
import { SpanElement } from "../elements/span/SpanElement";
import { StyledBoxElement } from "../elements/styled-box/StyledBoxElement";
import { SymbolElement } from "../elements/symbol/SymbolElement";
import { InlineLabelEditor, type InlineLabelEditorProps } from "../label/InlineLabelEditor";
import { asideLabelPlacement, centredLabelPlacement, insetLabelPlacement, midpointLabelPlacement } from "../label/labelPlacement";
import { CanvasScrollbars } from "../scroll/CanvasScrollbars";
import { scrollExtentOf } from "../scroll/scrollGeometry";
import { useElementContextMenu } from "../useElementContextMenu";
import { isTextTarget } from "../interaction";
import { ContextMenu } from "@client/shell/context/ContextMenu";
import { toMenuGroups } from "@client/shell/context/toMenuGroups";
import { useRegisterDiagramView, type DiagramViewControls } from "@client/shell/panels/DiagramViewContext";
import { TOOLBOX_DRAG_TYPE, useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useRegisterInlineLabelPlacement, type LabelPlacement } from "@client/shell/panels/InlineLabelPlacementContext";
import {
  dispatchDiagramEvent,
  type DiagramEvent,
  type DiagramEventHandlers,
  type DiagramSelection,
  type SelectedItem,
} from "./api/diagramEvents";
import { effectiveDefinition, type DiagramRuntimeConfig } from "./api/diagramRuntimeConfig";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "./api/diagramModel";
import { layoutAlgorithmFor, type LayoutInput } from "./layout/layoutAlgorithm";
import type {
  AnchorSet,
  CustomShapeRef,
  DiagramDefinition,
  ElementTypeDefinition,
  RelationTypeDefinition,
} from "./definition/diagramDefinition";

const ZOOM_STEP = 1.25;
const MIN_VIEW_WIDTH = 40;
const MAX_VIEW_WIDTH = 100000;
const FIT_PADDING = 40;
const DEFAULT_WIDTH = 120;
const DEFAULT_HEIGHT = 40;

/** The visible rectangle, in canvas units - the svg viewBox as data. */
interface ViewBox {
  x: number;
  y: number;
  w: number;
  h: number;
}

/**
 * The module's context-channel state, handed in rather than fetched: the canvas renders the
 * backend's answers and asks through the module's own callbacks, so there is one selection
 * policy and one menu discipline in the application, not two (Requirements 7.1, 7.2).
 */
export interface DiagramContextIntegration {
  /** `innermostKey(selection)` - which selection the backend currently holds. */
  selectionKey?: string;
  /** The pushed action groups for that selection - the menu renders these, never a guess. */
  actions: ContextActionGroup[];
  /** Push a selection for the menu gesture (the CONTEXT_MENU action). */
  selectForMenu: (id: string) => void;
  executeAction: (actionId: string) => void | Promise<unknown>;
}

/** The shell's inline-edit prompt, bridged: which target edits, and the editor's callbacks. */
export interface DiagramEditingIntegration {
  editingId: string | null;
  onPropose: InlineLabelEditorProps["onPropose"];
  onSubmit: InlineLabelEditorProps["onSubmit"];
  onCancel: InlineLabelEditorProps["onCancel"];
}

export interface DiagramCanvasProps {
  definition: DiagramDefinition;
  model: DiagramModel;
  events: DiagramEventHandlers;
  config?: DiagramRuntimeConfig;
  /**
   * Controlled selection - usually the backend's push, exactly as the hand-built canvases
   * highlight `selectedElementIdOf(selection)`. Omitted, the canvas highlights its own last
   * press; either way every press raises `selectionChanged` and nothing else decides.
   */
  selection?: DiagramSelection;
  context?: DiagramContextIntegration;
  editing?: DiagramEditingIntegration;
  /**
   * The backend's toolbox entries, where the module has them. Omitted, the toolbox derives
   * from the definition's element types (Requirement 4.4). Either way this canvas registers
   * the toolbox and the view controls as a pair, by construction (Requirement 1.4).
   */
  toolboxItems?: ToolboxItem[];
  className?: string;
  /** An extra class on both scrollbars, for placement only - a canvas with a ruler insets its bars. */
  scrollbarsClassName?: string;
  ariaLabel?: string;
}

/** What a press lands on; the shared arbiter threads it through untouched. */
type PressTarget =
  | { kind: "element"; element: DiagramModelElement }
  | { kind: "anchor"; element: DiagramModelElement; anchor: string | undefined; at: Point }
  | { kind: "resize"; element: DiagramModelElement; side: "left" | "right" }
  | { kind: "connection"; connection: DiagramModelConnection }
  | { kind: "adjust"; connection: DiagramModelConnection; from: Point }
  | { kind: "background"; view: ViewBox };

/** A connect gesture in flight: what it left from, where it is, and what it would land on. */
interface ConnectPreview {
  relation: RelationTypeDefinition;
  sourceId: string;
  sourceAnchor?: string;
  from: Point;
  point: Point;
  target?: { elementId: string; anchor?: string };
  valid: boolean;
}

/**
 * The library's canvas: one component driven by a declarative definition, raising typed
 * events a module answers by updating its model (diagram-library Requirement 1.1). It renders
 * through the existing shape components and route geometry, drives every gesture through the
 * shared arbiter, and **mutates nothing** - each gesture calls the matching handler and the
 * drawing changes when `model` does, exactly as the backend-fed canvases already behave.
 *
 * The definition is enforced where the user's hand is (Requirement 4.3): a connect drag
 * renders its candidates connectable or not as it moves, and an event is raised only for a
 * gesture the definition already allows - a module never vetoes a completed action.
 */
export function DiagramCanvas({
  definition: statedDefinition,
  model,
  events,
  config,
  selection: controlledSelection,
  context,
  editing,
  toolboxItems,
  className,
  scrollbarsClassName,
  ariaLabel,
}: DiagramCanvasProps) {
  // Memoized because two registrations key on its identity: a fresh object per render
  // would re-register the toolbox every render, and the provider's setState would render
  // again - a loop. The config object is a prop; its identity is the caller's contract.
  const definition = useMemo(() => effectiveDefinition(statedDefinition, config), [statedDefinition, config]);
  const elementTypes = useMemo(
    () => new Map(definition.elementTypes.map((type) => [type.id, type])),
    [definition.elementTypes],
  );
  const relationTypes = useMemo(
    () => new Map(definition.relationTypes.map((type) => [type.id, type])),
    [definition.relationTypes],
  );
  // The layout seam (Requirement 8.1): the active mode's algorithm places the elements, and
  // everything downstream - rendering, hit-testing, anchors, fit - reads the placed set.
  // Manual/external returns null and the model's own positions pass through untouched.
  const activeLayoutMode = config?.activeLayoutMode ?? definition.layout.modes[0] ?? "manual";
  const layoutPositions = useMemo(() => {
    const algorithm = layoutAlgorithmFor(activeLayoutMode);
    if (algorithm === undefined) {
      return null; // an unimplemented mode lays out as manual - the honest fallback
    }
    const input: LayoutInput = {
      elements: model.elements.map((element) => {
        const bounds = elementBounds(element, elementTypes.get(element.type));
        return { id: element.id, x: element.x, y: element.y, width: bounds.width, height: bounds.height, parentId: element.parentId };
      }),
      connections: model.connections.map((connection) => ({ sourceId: connection.sourceId, targetId: connection.targetId })),
    };
    return algorithm.place(input, definition.layout);
  }, [activeLayoutMode, model, elementTypes, definition.layout]);

  const elements = useMemo(
    () =>
      layoutPositions === null
        ? model.elements
        : model.elements.map((element) => {
            const at = layoutPositions.get(element.id);
            return at === undefined ? element : { ...element, x: at.x, y: at.y };
          }),
    [model.elements, layoutPositions],
  );

  const elementsById = useMemo(() => new Map(elements.map((element) => [element.id, element])), [elements]);

  const svgRef = useRef<SVGSVGElement>(null);
  const [view, setView] = useState<ViewBox | null>(null);
  const [ownSelection, setOwnSelection] = useState<DiagramSelection>([]);
  const [dragOffset, setDragOffset] = useState<{ id: string; dx: number; dy: number } | null>(null);
  const [resizePreview, setResizePreview] = useState<{ id: string; side: "left" | "right"; dx: number } | null>(null);
  const [connect, setConnect] = useState<ConnectPreview | null>(null);

  const selection = controlledSelection ?? ownSelection;

  const fitBox = useMemo(() => {
    if (definition.extent !== undefined) {
      // The space is definitional, not derived: fit shows exactly what the notation states.
      return { x: definition.extent.x, y: definition.extent.y, w: definition.extent.width, h: definition.extent.height };
    }
    return fitBoxOf(elements, elementTypes);
  }, [definition.extent, elements, elementTypes]);
  const effectiveView = view ?? fitBox;
  const viewRef = useRef(effectiveView);
  viewRef.current = effectiveView;

  const raise = useCallback(
    (event: DiagramEvent) => {
      dispatchDiagramEvent(events, event);
    },
    [events],
  );

  // view-changed means the view CHANGED - initially, on fit, zoom, pan and scrollbar alike -
  // so a module reporting its viewport observes one signal instead of wiring every gesture.
  // The handler is read through a ref because the module recreates its handler map per
  // render, and the effect below must fire on view changes, not on handler identity.
  const raiseRef = useRef(raise);
  raiseRef.current = raise;
  useEffect(() => {
    raiseRef.current({
      kind: "view-changed",
      viewport: { x: effectiveView.x, y: effectiveView.y, width: effectiveView.w, height: effectiveView.h },
    });
  }, [effectiveView.x, effectiveView.y, effectiveView.w, effectiveView.h]);

  /** How many canvas units one screen pixel spans - what turns pointer deltas into movement. */
  const unitsPerPixel = useCallback((box: ViewBox): number => {
    const rect = svgRef.current?.getBoundingClientRect();
    return rect !== undefined && rect.width > 0 ? box.w / rect.width : 1;
  }, []);

  const boundsOf = useCallback(
    (element: DiagramModelElement): ConnectorBox => elementBounds(element, elementTypes.get(element.type)),
    [elementTypes],
  );

  /**
   * The one attachment resolver (Requirement 3.4): rendering, the connect preview and
   * hit-testing all ask this same function where a line touches an element, so the three can
   * never disagree.
   */
  const attachmentPoint = useCallback(
    (element: DiagramModelElement, anchor: string | undefined, towards: Point): Point => {
      const type = elementTypes.get(element.type);
      const bounds = elementBounds(element, type);
      if (anchor !== undefined && type !== undefined) {
        const named = anchorPoints(type.anchors, bounds).find((candidate) => candidate.name === anchor);
        if (named !== undefined) {
          return named.point;
        }
      }

      if (type !== undefined && typeof type.shape !== "string") {
        return type.shape.edgePoint(bounds, towards);
      }

      // ConnectorBox is CENTRE-based while the library's bounds are corner-based; the
      // conversion here is load-bearing - without it every edge left from the corner as if
      // it were the centre, and the guard above this comment's test was seen to fail on it.
      const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
      return edgePointOf({ x: centre.x, y: centre.y, width: bounds.width, height: bounds.height }, towards.x - centre.x, towards.y - centre.y);
    },
    [elementTypes],
  );

  /**
   * Whether the definition admits this relation between these two elements - the verdict the
   * connect gesture consults on every move, so refusal happens under the pointer rather than
   * in a dialog after the drop (Requirement 4.3).
   */
  const connectVerdict = useCallback(
    (relation: RelationTypeDefinition, sourceId: string, target: DiagramModelElement): boolean => {
      if (!relation.endpoints.allowSelf && target.id === sourceId) {
        return false;
      }
      if (!relation.endpoints.target.elementTypes.includes(target.type)) {
        return false;
      }

      const cardinality = relation.endpoints.cardinality;
      if (cardinality?.maxFromSource !== undefined) {
        const outgoing = model.connections.filter(
          (connection) => connection.type === relation.id && connection.sourceId === sourceId,
        ).length;
        if (outgoing >= cardinality.maxFromSource) {
          return false;
        }
      }
      if (cardinality?.maxIntoTarget !== undefined) {
        const incoming = model.connections.filter(
          (connection) => connection.type === relation.id && connection.targetId === target.id,
        ).length;
        if (incoming >= cardinality.maxIntoTarget) {
          return false;
        }
      }

      return true;
    },
    [model.connections],
  );

  /**
   * Which relation a connect gesture from this element draws: the active tool where the
   * runtime names one, the first relation whose source constraint admits the type otherwise -
   * the definition's "default" answer to Requirement 5.5, so the library never guesses twice.
   */
  const relationFrom = useCallback(
    (element: DiagramModelElement, anchor?: string): RelationTypeDefinition | undefined => {
      // The anchor a connect starts from is part of the gesture's meaning: skos files a
      // concept under another from its TOP anchor and cross-links from its SIDE. A source
      // constraint naming anchors admits only drags that began on one of them.
      const admits = (relation: RelationTypeDefinition | undefined) => {
        if (relation === undefined || !relation.endpoints.source.elementTypes.includes(element.type)) {
          return undefined;
        }
        const allowed = relation.endpoints.source.anchors;
        if (Array.isArray(allowed) && (anchor === undefined || !allowed.includes(anchor))) {
          return undefined;
        }
        return relation;
      };
      return admits(config?.activeTool !== undefined ? relationTypes.get(config.activeTool) : undefined)
        ?? definition.relationTypes.map((relation) => admits(relation)).find((relation) => relation !== undefined);
    },
    [config?.activeTool, definition.relationTypes, relationTypes],
  );

  const draggingEnabled = useCallback(
    (element: DiagramModelElement): boolean => {
      const policy = config?.dragging ?? definition.dragging;
      const type = elementTypes.get(element.type);
      return type?.draggable ?? policy === "enabled";
    },
    [config?.dragging, definition.dragging, elementTypes],
  );

  const select = useCallback(
    (item: SelectedItem | null) => {
      const next: DiagramSelection = item === null ? [] : [item];
      setOwnSelection(next);
      raise({ kind: "selection-changed", selection: next });
    },
    [raise],
  );

  /** The topmost element whose bounds contain the point - later in the model draws on top. */
  const elementAt = useCallback(
    (point: Point): DiagramModelElement | undefined => {
      for (let i = elements.length - 1; i >= 0; i--) {
        const bounds = boundsOf(elements[i]);
        if (
          point.x >= bounds.x &&
          point.x <= bounds.x + bounds.width &&
          point.y >= bounds.y &&
          point.y <= bounds.y + bounds.height
        ) {
          return elements[i];
        }
      }
      return undefined;
    },
    [elements, boundsOf],
  );

  const gesture = usePointerGesture<PressTarget>({
    onPress: (target) => {
      switch (target.kind) {
        case "element":
          select({ kind: "element", id: target.element.id });
          break;
        case "connection":
        case "adjust":
          select({ kind: "connection", id: target.kind === "adjust" ? target.connection.id : target.connection.id });
          break;
        case "anchor":
          // An unmoved press on an anchor selects its element: the anchor is part of it.
          select({ kind: "element", id: target.element.id });
          break;
        case "background":
          select(null);
          break;
      }
      svgRef.current?.focus();
    },
    onDragMove: (target, dx, dy) => {
      const scale = unitsPerPixel(viewRef.current);
      switch (target.kind) {
        case "background": {
          const scaleAtPress = unitsPerPixel(target.view);
          setView({ ...target.view, x: target.view.x - dx * scaleAtPress, y: target.view.y - dy * scaleAtPress });
          break;
        }
        case "element": {
          if (!draggingEnabled(target.element)) {
            break; // disabled dragging: the press stays a press (Requirement 5.2)
          }
          const at = clampToDragBounds(definition.dragBounds, target.element, dx * scale, dy * scale);
          setDragOffset({ id: target.element.id, dx: at.x - target.element.x, dy: at.y - target.element.y });
          break;
        }
        case "anchor": {
          const relation = relationFrom(target.element, target.anchor);
          if (relation === undefined) {
            break; // no relation may leave this element; nothing to preview
          }
          const point = { x: target.at.x + dx * scale, y: target.at.y + dy * scale };
          const candidate = elementAt(point);
          const valid = candidate !== undefined && connectVerdict(relation, target.element.id, candidate);
          setConnect({
            relation,
            sourceId: target.element.id,
            sourceAnchor: target.anchor,
            from: target.at,
            point,
            target: valid && candidate !== undefined ? { elementId: candidate.id, anchor: nearestAnchor(elementTypes.get(candidate.type), boundsOf(candidate), point, relation) } : undefined,
            valid,
          });
          break;
        }
        case "resize": {
          setResizePreview({ id: target.element.id, side: target.side, dx: dx * scale });
          break;
        }
        case "adjust": {
          // Live feedback only; the commit is the release. The single midpoint waypoint is
          // the minimal adjustment surface; richer editing rides the same event.
          setConnect(null);
          break;
        }
        case "connection":
          break; // a connection has no position; dragging one is just not a click
      }
    },
    onDragEnd: (target, dx, dy) => {
      const scale = unitsPerPixel(viewRef.current);
      switch (target.kind) {
        case "background":
          break; // the view effect above reports the settled viewport
        case "element": {
          if (!draggingEnabled(target.element)) {
            break;
          }
          setDragOffset(null);
          // Under an automatic layout the definition says what the drag MEANS (Requirement
          // 8.4): a reclaimed displacement raises nothing - the next layout pass takes the
          // element back - while repin-to-manual raises the move, and the module answers by
          // recording the position its manual placement will then honour.
          if (activeLayoutMode !== "manual" && (definition.layout.dragUnderAutomaticLayout ?? "repin-to-manual") === "reclaimed-displacement") {
            break;
          }
          raise({
            kind: "element-moved",
            elementId: target.element.id,
            position: clampToDragBounds(definition.dragBounds, target.element, dx * scale, dy * scale),
          });
          break;
        }
        case "resize": {
          setResizePreview(null);
          const bounds = resizedBounds(elementBounds(target.element, elementTypes.get(target.element.type)), target.side, dx * scale);
          raise({ kind: "element-resized", elementId: target.element.id, side: target.side, bounds });
          break;
        }
        case "anchor": {
          const preview = connectRef.current;
          setConnect(null);
          if (preview !== undefined && preview !== null && preview.valid && preview.target !== undefined) {
            // Raised only because the verdict already said yes: an invalid release raises
            // nothing and the preview simply dissolves (design, Error Handling 2).
            raise({
              kind: "connection-drawn",
              relationType: preview.relation.id,
              sourceElementId: preview.sourceId,
              targetElementId: preview.target.elementId,
              sourceAnchor: preview.sourceAnchor,
              targetAnchor: preview.target.anchor,
            });
          } else if (preview !== undefined && preview !== null && preview.relation.emptyRelease === "complete" && elementAt(preview.point) === undefined) {
            // The definition declared an empty release meaningful - the create-and-relate
            // gesture - so the point travels to the module instead of dissolving.
            raise({
              kind: "connection-released-on-empty",
              relationType: preview.relation.id,
              sourceElementId: preview.sourceId,
              sourceAnchor: preview.sourceAnchor,
              position: preview.point,
            });
          }
          break;
        }
        case "adjust": {
          raise({
            kind: "connection-adjusted",
            connectionId: target.connection.id,
            waypoints: [{ x: target.from.x + dx * scale, y: target.from.y + dy * scale }],
          });
          break;
        }
        case "connection":
          break;
      }
    },
    onDragAbandon: () => {
      setDragOffset(null);
      setResizePreview(null);
      setConnect(null);
    },
  });

  // Read at gesture end through a ref: the last pointermove's state write and the pointerup
  // can share a render, and the closure would be a frame behind.
  const connectRef = useRef(connect);
  connectRef.current = connect;

  // ---- view: zoom, fit, wheel, controls-and-toolbox as a pair ------------------------------

  const zoomBy = useCallback(
    (factor: number, aboutX?: number, aboutY?: number) => {
      // Computed outside the updater: StrictMode double-invokes updaters, and a raise from
      // inside one would reach the module twice for one zoom.
      const current = viewRef.current;
      const w = Math.min(MAX_VIEW_WIDTH, Math.max(MIN_VIEW_WIDTH, current.w / factor));
      const applied = current.w / w;
      const h = current.h / applied;
      const px = aboutX ?? current.x + current.w / 2;
      const py = aboutY ?? current.y + current.h / 2;
      const next = { x: px - (px - current.x) / applied, y: py - (py - current.y) / applied, w, h };
      setView(next);
    },
    [],
  );

  useEffect(() => {
    const surface = svgRef.current;
    if (surface === null) {
      return;
    }

    // Attached by hand as non-passive: React's synthetic wheel listener cannot
    // preventDefault, and without that every zoom also scrolls the page.
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
  }, [zoomBy]);

  const viewControls = useMemo<DiagramViewControls>(
    () => ({
      zoomIn: () => zoomBy(ZOOM_STEP),
      zoomOut: () => zoomBy(1 / ZOOM_STEP),
      fitToView: () => setView(null),
    }),
    [zoomBy],
  );

  // The pair, by construction (Requirement 1.4): this component registers BOTH the view
  // controls and the toolbox, so a migrated module cannot register one and look finished.
  useRegisterDiagramView(viewControls);
  const derivedToolbox = useMemo(
    () => toolboxItems ?? deriveToolbox(definition),
    [toolboxItems, definition],
  );
  useRegisterDiagramToolbox(derivedToolbox);

  // ---- toolbox drops -----------------------------------------------------------------------

  const toCanvasPoint = useCallback((clientX: number, clientY: number): Point => {
    const rect = svgRef.current?.getBoundingClientRect();
    const current = viewRef.current;
    if (rect === undefined || rect.width <= 0 || rect.height <= 0) {
      return { x: current.x, y: current.y };
    }
    return {
      x: current.x + ((clientX - rect.left) / rect.width) * current.w,
      y: current.y + ((clientY - rect.top) / rect.height) * current.h,
    };
  }, []);

  const onSurfaceDragOver = (event: React.DragEvent) => {
    if (event.dataTransfer.types.includes(TOOLBOX_DRAG_TYPE)) {
      event.preventDefault();
      event.dataTransfer.dropEffect = "copy";
    }
  };

  const onSurfaceDrop = (event: React.DragEvent) => {
    const payload = event.dataTransfer.getData(TOOLBOX_DRAG_TYPE);
    if (!payload) {
      return;
    }

    // A payload naming a declared, unsuppressed element type is a definition-checked add.
    // With a module-supplied toolbox the payload is the module's own (a backend action id),
    // handed through verbatim - the module knows its vocabulary. A derived toolbox has no
    // such vocabulary, so an unknown payload there refuses: dropping what the definition
    // does not offer is the forbidden drop (Requirement 5.3).
    const declared = elementTypes.get(payload);
    const suppressed = definition.toolbox?.suppress?.includes(payload) ?? false;
    if (declared === undefined || suppressed) {
      if (toolboxItems === undefined) {
        return;
      }
    }

    event.preventDefault();
    raise({ kind: "element-dropped", elementType: payload, position: toCanvasPoint(event.clientX, event.clientY) });
  };

  // ---- keyboard: delete --------------------------------------------------------------------

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (isTextTarget(event.target)) {
      return;
    }

    if (event.key === "Escape") {
      // Whatever gesture is in flight dissolves, dispatching nothing.
      gesture.abandon();
      return;
    }

    if (event.key !== "Delete" && event.key !== "Backspace") {
      return;
    }

    for (const item of selection) {
      if (item.kind === "element") {
        const element = elementsById.get(item.id);
        const type = element !== undefined ? elementTypes.get(element.type) : undefined;
        if (element !== undefined && (type?.deletable ?? true)) {
          event.preventDefault();
          raise({ kind: "element-deleted", elementId: item.id });
        }
      } else {
        event.preventDefault();
        raise({ kind: "connection-deleted", connectionId: item.id });
      }
    }
  };

  // ---- context menu (Requirement 7.2) ------------------------------------------------------

  const { menuPosition, openMenuAt, closeMenu } = useElementContextMenu(
    context?.selectionKey,
    (context?.actions.length ?? 0) > 0,
    (id) => context?.selectForMenu(id),
  );

  const onItemContextMenu = (id: string) => (event: React.MouseEvent) => {
    if (context === undefined) {
      return;
    }
    openMenuAt(event, id);
  };

  // ---- inline label editing (Requirement 6.1) ----------------------------------------------

  /**
   * Where a label is, for the shell's editor - null for anything the definition does not mark
   * editable, which is Requirement 6.2 enforced in one place: a label with no single authored
   * value beneath it is simply never marked editable in the definition.
   */
  const placementOfLabel = useCallback(
    (id: string): LabelPlacement | null => {
      const element = elementsById.get(id);
      if (element !== undefined) {
        const type = elementTypes.get(element.type);
        if (type?.label?.editable !== true) {
          return null;
        }
        const bounds = elementBounds(element, type);
        // "beside" opens next to the shape - a moment's diamond has no box to open inside.
        // An element carrying its own label origin opens there: the author put the drawn
        // text at an offset (left of the mark included), and the editor covers the text.
        if (type.label.placement === "beside") {
          return element.labelAt !== undefined
            ? asideLabelPlacement(element.labelAt, 0, element.label ?? "")
            : asideLabelPlacement({ x: bounds.x + bounds.width, y: bounds.y + bounds.height / 2 }, 6, element.label ?? "");
        }
        // "inset" opens over the one named line of a composite card - the c4 shape - with the
        // horizontal inset applied by narrowing the centred box on both sides.
        if (type.label.placement === "inset") {
          return insetLabelPlacement(
            { x: element.x, y: element.y, width: bounds.width - (type.label.insetX ?? 0) * 2, height: bounds.height },
            type.label.insetTop ?? 0,
            type.label.insetHeight ?? 20,
            element.label ?? "",
          );
        }
        // centredLabelPlacement takes a CENTRE-based box while the library's bounds are
        // corner-based - the same conversion the edge-attachment guard was seen to fail on,
        // caught here by the mindmap migration's editor-position test.
        return centredLabelPlacement({ x: element.x, y: element.y, width: bounds.width, height: bounds.height }, element.label ?? "");
      }

      const connection = model.connections.find((candidate) => candidate.id === id);
      if (connection !== undefined) {
        const relation = relationTypes.get(connection.type);
        if (relation?.label?.editable !== true) {
          return null;
        }
        const ends = connectionEnds(connection, elementsById, elementTypes, attachmentPoint);
        if (ends === null) {
          return null;
        }
        // The editor opens with the one AUTHORED value where the drawn label decorates it,
        // and at the drawn text's own measured width where the browser can measure - jsdom
        // has no getBBox, so unit tests exercise the estimate branch by construction and the
        // measured path is the manual checks' to verify.
        // Matched by attribute value rather than a CSS selector: jsdom offers no CSS.escape,
        // and a connection id is module data no selector grammar should have to survive.
        const drawnGroup = [...(svgRef.current?.querySelectorAll("[data-connection-id]") ?? [])]
          .find((candidate) => candidate.getAttribute("data-connection-id") === connection.id);
        const drawn = drawnGroup?.querySelector("text");
        const measured = drawn instanceof SVGGraphicsElement && typeof drawn.getBBox === "function" ? drawn.getBBox().width : 0;
        return midpointLabelPlacement(
          ends[0],
          ends[1],
          relation.label.offset ?? -6,
          connection.editValue ?? connection.label ?? "",
          measured > 0 ? measured : null,
        );
      }

      return null;
    },
    [elementsById, elementTypes, relationTypes, model.connections, attachmentPoint],
  );
  useRegisterInlineLabelPlacement(placementOfLabel);

  const editingPlacement = editing?.editingId != null ? placementOfLabel(editing.editingId) : null;

  // ---- rendering ---------------------------------------------------------------------------

  // Frames first so enclosures draw under their members; then the rest in model order, so
  // hit-testing (which walks the model backwards) and painting agree about what is on top.
  const ordered = useMemo(() => {
    const isFrame = (element: DiagramModelElement) => elementTypes.get(element.type)?.shape === "frame";
    return [...elements.filter(isFrame), ...elements.filter((element) => !isFrame(element))];
  }, [elements, elementTypes]);

  // A type marked beneathConnections paints before the connections - an opaque container
  // whose members' edges must stay visible over it; everything else keeps the
  // connections-first order the canvases have always had.
  const beneathConnections = useMemo(
    () => ordered.filter((element) => elementTypes.get(element.type)?.beneathConnections === true),
    [ordered, elementTypes],
  );
  const aboveConnections = useMemo(
    () => ordered.filter((element) => elementTypes.get(element.type)?.beneathConnections !== true),
    [ordered, elementTypes],
  );

  const isSelected = (kind: SelectedItem["kind"], id: string) =>
    selection.some((item) => item.kind === kind && item.id === id);

  /**
   * Ends an open inline edit before a gesture begins (the mindmap spec's Requirement 5.5,
   * inherited by every adopter): the editor commits on blur, so taking the focus is the whole
   * mechanism, and it lives here once rather than per module. Capture phase, so it runs before
   * any press wiring; a press inside the editor itself is exempt - ending the edit on the
   * click being typed into would commit the very edit being clicked.
   */
  // Stable, and this is load-bearing: the editor's return-focus effect keys on this function's
  // IDENTITY, and an inline arrow here re-ran its cleanup every render - which hands focus to
  // the canvas mid-edit and latches the editor's closing flag, the silent-self-cancel class of
  // defect the editor's own comments warn about. Caught by the mindmap migration's
  // commit-before-gesture test.
  const returnFocusToSurface = useCallback(() => svgRef.current?.focus(), []);

  const endEditBeforeGesture = (event: React.PointerEvent) => {
    if (editing?.editingId != null && !(event.target instanceof Element && event.target.closest("foreignObject") !== null)) {
      svgRef.current?.focus();
    }
  };

  const renderLibraryElement = (element: DiagramModelElement) => (
    <LibraryElement
      key={element.id}
      element={element}
      type={elementTypes.get(element.type)}
      offset={dragOffset?.id === element.id ? dragOffset : undefined}
      resize={resizePreview?.id === element.id ? resizePreview : undefined}
      resizable={elementTypes.get(element.type)?.sizing === "user"}
      resizePress={(side) => gesture.press({ kind: "resize", element, side })}
      selected={isSelected("element", element.id)}
      connectHighlight={connect?.target?.elementId === element.id ? "valid" : connect !== null && !connect.valid && connectTargetUnder(connect, element, boundsOf) ? "invalid" : undefined}
      press={gesture.press({ kind: "element", element })}
      anchorPress={(anchor, at) => gesture.press({ kind: "anchor", element, anchor, at })}
      onContextMenu={onItemContextMenu(element.id)}
    />
  );

  return (
    <div className={`library-canvas ${className ?? ""}`.trim()} data-testid="library-canvas">
      <svg
        ref={svgRef}
        className="canvas-host library-canvas-surface"
        viewBox={`${effectiveView.x} ${effectiveView.y} ${effectiveView.w} ${effectiveView.h}`}
        tabIndex={0}
        role="img"
        aria-label={ariaLabel ?? "Diagram"}
        {...gesture.background({ kind: "background", view: effectiveView })}
        onPointerDownCapture={endEditBeforeGesture}
        onKeyDown={onKeyDown}
        onDragOver={onSurfaceDragOver}
        onDrop={onSurfaceDrop}
      >
        <defs>
          <marker id="library-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
            <path className="canvas-arrowhead" d="M 0 0 L 10 5 L 0 10 z" />
          </marker>
          <marker id="library-open-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
            <path d="M 1 1 L 9 5 L 1 9" fill="none" stroke="currentColor" />
          </marker>
          <marker id="library-diamond" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse">
            <path d="M 1 5 L 5 1 L 9 5 L 5 9 z" className="canvas-arrowhead" />
          </marker>
          <marker id="library-circle" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
            <circle cx="5" cy="5" r="4" className="canvas-arrowhead" />
          </marker>
        </defs>

        {definition.background !== undefined && (
          <g className="library-canvas-background" data-testid="canvas-background" pointerEvents="none">
            {definition.background.render({ x: effectiveView.x, y: effectiveView.y, width: effectiveView.w, height: effectiveView.h }) as ReactNode}
          </g>
        )}

        {beneathConnections.map((element) => renderLibraryElement(element))}

        {model.connections.map((connection) => (
          <LibraryConnection
            key={connection.id}
            connection={connection}
            relation={relationTypes.get(connection.type)}
            elementsById={elementsById}
            elementTypes={elementTypes}
            attachmentPoint={attachmentPoint}
            selected={isSelected("connection", connection.id)}
            press={gesture.press({ kind: "connection", connection })}
            adjustPress={(from) => gesture.press({ kind: "adjust", connection, from })}
            onContextMenu={onItemContextMenu(connection.id)}
          />
        ))}

        {aboveConnections.map((element) => renderLibraryElement(element))}

        {connect !== null && (
          // The live preview, routed and styled as the relation prescribes (Requirement 5.4).
          <path
            className={`library-connect-preview${connect.valid ? "" : " library-connect-preview-invalid"}`}
            data-testid="connect-preview"
            d={routePath(connect.relation, connect.from, connect.point, [])}
            pointerEvents="none"
          />
        )}

        {editing !== undefined && editing.editingId !== null && editingPlacement !== null && (
          <InlineLabelEditor
            placement={editingPlacement}
            onPropose={editing.onPropose}
            onSubmit={(...args) => {
              // The commit is an event first (Requirement 6.1): the module hears what was
              // asked, then the shell's own prompt flow carries it - one editor, one commit,
              // with the editor's own arguments forwarded exactly as it sent them.
              raise({
                kind: "label-commit-requested",
                target: elementsById.has(editing.editingId!)
                  ? { kind: "element", id: editing.editingId! }
                  : { kind: "connection", id: editing.editingId! },
                value: args[0],
              });
              return editing.onSubmit(...args);
            }}
            onCancel={editing.onCancel}
            onReturnFocus={returnFocusToSurface}
          />
        )}
      </svg>

      {definition.layout.modes.length > 1 && (
        <div className="library-layout-switcher" data-testid="layout-switcher">
          {definition.layout.modes.map((mode) => (
            <button
              key={mode}
              type="button"
              className={mode === activeLayoutMode ? "library-layout-active" : undefined}
              onClick={() => raise({ kind: "layout-mode-changed", mode })}
            >
              {mode}
            </button>
          ))}
        </div>
      )}

      <CanvasScrollbars
        horizontal={{ viewStart: effectiveView.x, viewSpan: effectiveView.w, ...scrollExtentOf(fitBox.x, fitBox.x + fitBox.w, { factor: 0.5 }) }}
        vertical={{ viewStart: effectiveView.y, viewSpan: effectiveView.h, ...scrollExtentOf(fitBox.y, fitBox.y + fitBox.h, { factor: 0.5 }) }}
        className={scrollbarsClassName}
        onPan={(x, y) => setView({ ...effectiveView, x, y })}
      />

      {context !== undefined && (
        <ContextMenu
          open={menuPosition !== null}
          groups={toMenuGroups(context.actions, (action) => {
            closeMenu();
            void context.executeAction(action.id);
          })}
          position={menuPosition ?? { x: 0, y: 0 }}
          onClose={closeMenu}
        />
      )}
    </div>
  );
}

// ---- the pieces the component composes -----------------------------------------------------

function LibraryElement({
  element,
  type,
  offset,
  resize,
  resizable,
  resizePress,
  selected,
  connectHighlight,
  press,
  anchorPress,
  onContextMenu,
}: {
  element: DiagramModelElement;
  type: ElementTypeDefinition | undefined;
  offset?: { dx: number; dy: number };
  resize?: { side: "left" | "right"; dx: number };
  resizable: boolean;
  resizePress: (side: "left" | "right") => PointerPressWiring;
  selected: boolean;
  connectHighlight?: "valid" | "invalid";
  press: PointerPressWiring;
  anchorPress: (anchor: string | undefined, at: Point) => PointerPressWiring;
  onContextMenu: (event: React.MouseEvent) => void;
}) {
  const shifted: DiagramModelElement = offset ? { ...element, x: element.x + offset.dx, y: element.y + offset.dy } : element;
  const plainBounds = elementBounds(shifted, type);
  const bounds = resize ? resizedBounds(plainBounds, resize.side, resize.dx) : plainBounds;
  const resizing: DiagramModelElement = resize
    ? { ...shifted, x: bounds.x + bounds.width / 2, width: bounds.width }
    : shifted;
  const classes = [
    "library-element",
    selected ? "canvas-selected" : "",
    offset ? "library-element-dragging" : "",
    connectHighlight === "valid" ? "library-connect-target" : "",
    connectHighlight === "invalid" ? "library-connect-forbidden" : "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <g className={classes} data-element-id={element.id} {...press} onContextMenu={onContextMenu}>
      <ShapeErrorBoundary bounds={bounds} label={element.label ?? element.id}>
        {renderShape(resizing, type, bounds, { selected, dragging: offset !== undefined, connectTarget: connectHighlight === "valid" })}
      </ShapeErrorBoundary>
      {resizable && selected && (
        // The resize adorners a user-sized element earns when selected: each edge strip
        // drives the arbiter and raises element-resized on release (sizing: "user", R2.5).
        <>
          <rect className="library-resize-handle" data-resize="left" x={bounds.x - 3} y={bounds.y} width={6} height={bounds.height} {...resizePress("left")} />
          <rect className="library-resize-handle" data-resize="right" x={bounds.x + bounds.width - 3} y={bounds.y} width={6} height={bounds.height} {...resizePress("right")} />
        </>
      )}
      {/* Anchors render always and show on hover or mid-connect, via the stylesheet - so a
          test can press them and a user only sees them when they matter (Requirement 2.4). */}
      {type !== undefined &&
        anchorPoints(type.anchors, bounds).map((anchor) => (
          <circle
            key={anchor.name ?? `${anchor.point.x},${anchor.point.y}`}
            className="library-anchor"
            data-anchor={anchor.name}
            cx={anchor.point.x}
            cy={anchor.point.y}
            r={5}
            {...anchorPress(anchor.name, anchor.point)}
          />
        ))}
    </g>
  );
}

function LibraryConnection({
  connection,
  relation,
  elementsById,
  elementTypes,
  attachmentPoint,
  selected,
  press,
  adjustPress,
  onContextMenu,
}: {
  connection: DiagramModelConnection;
  relation: RelationTypeDefinition | undefined;
  elementsById: Map<string, DiagramModelElement>;
  elementTypes: Map<string, ElementTypeDefinition>;
  attachmentPoint: (element: DiagramModelElement, anchor: string | undefined, towards: Point) => Point;
  selected: boolean;
  press: PointerPressWiring;
  adjustPress: (from: Point) => PointerPressWiring;
  onContextMenu: (event: React.MouseEvent) => void;
}) {
  if (relation === undefined) {
    return null; // an undeclared relation type has nothing to route; the validator rejects it upstream
  }

  const ends = connectionEnds(connection, elementsById, elementTypes, attachmentPoint);
  if (ends === null) {
    return null; // a dangling end is the module's model bug to notice; there is nothing to draw
  }

  const [from, to] = ends;
  const waypoints = connection.waypoints ?? [];
  const source = elementsById.get(connection.sourceId);
  const target = elementsById.get(connection.targetId);
  const routeEnds =
    source !== undefined && target !== undefined
      ? { source: elementBounds(source, elementTypes.get(source.type)), target: elementBounds(target, elementTypes.get(target.type)) }
      : undefined;
  const d = routePath(relation, from, to, waypoints, connection.style?.cornerRadius ?? relation.style?.cornerRadius, routeEnds);
  const style = { ...relation.style, ...connection.style };
  const mid = waypoints.length > 0 ? waypoints[Math.floor(waypoints.length / 2)] : midpointOf(from, to);
  const label = connection.label;
  const labelAt = relation.label?.placement === "source" ? from : relation.label?.placement === "target" ? to : mid;

  return (
    <g
      className={`canvas-connection library-connection ${relation.className ?? ""} ${connection.className ?? ""}${selected ? " canvas-selected" : ""}`.replace(/\s+/g, " ").trim()}
      data-connection-id={connection.id}
      {...press}
      onContextMenu={onContextMenu}
    >
      <path className={`canvas-connection-hit ${relation.hitClassName ?? ""}`.trim()} d={d} />
      <path
        className={`canvas-connection-line library-connection-line ${relation.lineClassName ?? ""}`.trim()}
        d={d}
        markerEnd={markerRef(style.endMarker ?? "arrow")}
        markerStart={markerRef(style.startMarker ?? "none")}
        style={{
          stroke: tokenColour(style.stroke),
          strokeWidth: style.strokeWidth,
          strokeDasharray: style.dash?.join(" "),
        }}
      />
      {label !== undefined && label !== "" && (
        <text className="library-connection-label" x={labelAt.x} y={labelAt.y + (relation.label?.offset ?? -6)} textAnchor="middle">
          {label}
        </text>
      )}
      {connection.title !== undefined && connection.title !== "" && <title>{connection.title}</title>}
      {relation.adorn !== undefined && <>{relation.adorn({ from, to, waypoints, ends: routeEnds }, connection) as ReactNode}</>}
      {selected && relation.adjustable === true && (
        // The one adjustment handle: dragging it raises connection-adjusted with the carried
        // waypoint; a definition that forbids adjustment never renders it (Requirement 3.5).
        <circle className="library-adjust-handle" data-testid={`adjust-${connection.id}`} cx={mid.x} cy={mid.y} r={5} {...adjustPress(mid)} />
      )}
    </g>
  );
}

/** One bad element cannot blank the canvas: the offender renders as the fallback box. */
class ShapeErrorBoundary extends Component<{ bounds: ConnectorBox; label: string; children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError(): { failed: boolean } {
    return { failed: true };
  }

  componentDidCatch(error: unknown): void {
    // The error surfaces rather than vanishing; the fallback keeps the rest of the canvas up.
    console.error("A custom shape renderer threw; drawing its element as the fallback box.", error);
  }

  render(): ReactNode {
    if (this.state.failed) {
      return fallbackBox(this.props.bounds, this.props.label);
    }
    return this.props.children;
  }
}

/**
 * Draws one element through the shape its type names: the seven existing components for the
 * shapes they already are, direct silhouettes for the six the requirements add, the module's
 * renderer for a custom shape, and a visible fallback for anything undeclared - a mapping bug
 * shown rather than hidden (design, Error Handling 1).
 */
function renderShape(
  element: DiagramModelElement,
  type: ElementTypeDefinition | undefined,
  bounds: ConnectorBox,
  state?: import("./definition/diagramDefinition").CustomShapeState,
): ReactNode {
  const label = element.label ?? "";
  if (type === undefined) {
    return fallbackBox(bounds, element.label ?? element.id);
  }

  const style = { ...type.style, ...element.style };
  const paint = {
    fill: tokenColour(style.fill),
    stroke: tokenColour(style.stroke),
    strokeWidth: style.strokeWidth,
    strokeDasharray: style.dash?.join(" "),
  };

  if (typeof type.shape !== "string") {
    return <>{(type.shape as CustomShapeRef).render(element, state) as ReactNode}</>;
  }

  switch (type.shape) {
    case "box":
      return <BoxElement x={bounds.x} y={bounds.y} width={bounds.width} height={bounds.height} rx={style.cornerRadius} label={label} boxClassName="library-shape" style={paint} />;
    case "rounded-rectangle":
      return <BoxElement x={bounds.x} y={bounds.y} width={bounds.width} height={bounds.height} rx={style.cornerRadius ?? 8} label={label} boxClassName="library-shape" style={paint} />;
    case "pill":
      return <BoxElement x={bounds.x} y={bounds.y} width={bounds.width} height={bounds.height} rx={bounds.height / 2} label={label} boxClassName="library-shape" style={paint} />;
    case "centered-box":
      return <CenteredBoxElement x={element.x} y={element.y} halfWidth={bounds.width / 2} halfHeight={bounds.height / 2} text={label} style={paint} />;
    case "ellipse":
      return <EllipseElement x={element.x} y={element.y} radiusX={bounds.width / 2} radiusY={bounds.height / 2} text={label} ellipseClassName="library-shape" style={paint} />;
    case "frame":
      // FrameElement is centre-based like the other shared elements; the corner-based bounds
      // shifted every boundary by half its box (the third corner/centre instance, caught by
      // the c4 migration's frame test before one ever mounted).
      return <FrameElement className="library-frame" x={element.x} y={element.y} width={bounds.width} height={bounds.height} label={label} style={paint} />;
    case "span":
      return <SpanElement box={{ x: element.x, y: element.y, width: bounds.width, height: bounds.height }} label={label} classes={{ span: "library-shape library-span", moment: "library-span-moment", label: "library-span-label", hint: "library-span-hint", adorner: "library-span-adorner", anchor: "library-span-anchor", anchorHit: "library-span-anchor-hit" }} style={paint} />;
    case "styled-box":
      return <StyledBoxElement x={element.x} y={element.y} width={bounds.width} height={bounds.height} background={tokenColour(style.fill) ?? "var(--canvas-node-fill, #3b6ea5)"} color={tokenColour(style.labelTypography?.color) ?? "var(--canvas-node-label, #ffffff)"} name={label} />;
    case "symbol":
      return <SymbolElement x={element.x} y={element.y} label={label} labelX={element.x + 12} labelY={element.y - 8} markClassName="library-shape" style={paint} />;
    case "diamond":
      return polygonShape(bounds, label, paint, [[0.5, 0], [1, 0.5], [0.5, 1], [0, 0.5]]);
    case "hexagon":
      return polygonShape(bounds, label, paint, [[0.25, 0], [0.75, 0], [1, 0.5], [0.75, 1], [0.25, 1], [0, 0.5]]);
    case "parallelogram":
      return polygonShape(bounds, label, paint, [[0.2, 0], [1, 0], [0.8, 1], [0, 1]]);
    case "cylinder":
      return (
        <g>
          <rect className="library-shape" x={bounds.x} y={bounds.y + 6} width={bounds.width} height={bounds.height - 12} style={paint} />
          <ellipse className="library-shape" cx={element.x} cy={bounds.y + 6} rx={bounds.width / 2} ry={6} style={paint} />
          <ellipse className="library-shape" cx={element.x} cy={bounds.y + bounds.height - 6} rx={bounds.width / 2} ry={6} style={paint} />
          {centredText(element, label)}
        </g>
      );
  }
}

function polygonShape(bounds: ConnectorBox, label: string, paint: React.CSSProperties, corners: readonly (readonly [number, number])[]): ReactNode {
  const points = corners.map(([fx, fy]) => `${bounds.x + fx * bounds.width},${bounds.y + fy * bounds.height}`).join(" ");
  return (
    <g>
      <polygon className="library-shape" points={points} style={paint} />
      {centredText({ x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 }, label)}
    </g>
  );
}

function centredText(at: { x: number; y: number }, label: string): ReactNode {
  return (
    <text className="library-element-label" x={at.x} y={at.y + 4} textAnchor="middle">
      {label}
    </text>
  );
}

function fallbackBox(bounds: ConnectorBox, label: string): ReactNode {
  return (
    <g className="library-fallback" data-testid="library-fallback">
      <rect x={bounds.x} y={bounds.y} width={bounds.width} height={bounds.height} fill="none" stroke="currentColor" strokeDasharray="4 2" />
      {centredText({ x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 }, label)}
    </g>
  );
}

/** A style colour resolves through a theme token; a `--token` name becomes `var(--token)`. */
function tokenColour(value: string | number | undefined): string | undefined {
  if (typeof value !== "string" || value === "") {
    return undefined;
  }
  return value.startsWith("--") ? `var(${value})` : value;
}

function markerRef(kind: import("./definition/diagramDefinition").MarkerKind): string | undefined {
  if (typeof kind !== "string") {
    return `url(#library-custom-${kind.customMarker})`;
  }
  switch (kind) {
    case "none":
      return undefined;
    case "arrow":
      return "url(#library-arrow)";
    case "open-arrow":
      return "url(#library-open-arrow)";
    case "diamond":
      return "url(#library-diamond)";
    case "circle":
      return "url(#library-circle)";
  }
}

/**
 * The one route table (Requirement 3.1): every built-in is a path from the shared geometry in
 * `connectors.ts`, and a custom route is the module's own builder - which is the whole of what
 * a route IS to this canvas.
 */
export function routePath(
  relation: RelationTypeDefinition,
  from: Point,
  to: Point,
  waypoints: readonly Point[],
  cornerRadius?: number,
  ends?: import("./definition/diagramDefinition").RouteEnds,
): string {
  const route = relation.route;
  if (typeof route !== "string") {
    return route.path(from, to, waypoints, ends);
  }

  switch (route) {
    case "straight":
      return straightPath(from, to);
    case "polyline":
      return polylinePath(from, to, waypoints);
    case "orthogonal":
      return orthogonalPath(from, to, cornerRadius ?? 0);
    case "arc":
      return arcPath(from, to);
    case "quadratic-bezier":
      return quadraticBezierPath(from, to);
    case "cubic-bezier":
      return horizontalBezierPath(from, to);
    case "spline":
      return splinePath(from, to, waypoints);
  }
}

function connectionEnds(
  connection: DiagramModelConnection,
  elementsById: Map<string, DiagramModelElement>,
  elementTypes: Map<string, ElementTypeDefinition>,
  attachmentPoint: (element: DiagramModelElement, anchor: string | undefined, towards: Point) => Point,
): [Point, Point] | null {
  const source = elementsById.get(connection.sourceId);
  const target = elementsById.get(connection.targetId);
  if (source === undefined || target === undefined) {
    return null;
  }

  const from = attachmentPoint(source, connection.sourceAnchor, { x: target.x, y: target.y });
  const to = attachmentPoint(target, connection.targetAnchor, { x: source.x, y: source.y });
  void elementTypes;
  return [from, to];
}

/** Every anchor an anchor set declares, resolved into the element's bounds. */
export function anchorPoints(anchors: AnchorSet, bounds: ConnectorBox): readonly { name?: string; point: Point }[] {
  switch (anchors.kind) {
    case "edge":
      return [];
    case "compass":
      return anchors.positions.map((position) => ({ name: position, point: compassPoint(position, bounds) }));
    case "sides":
      return anchors.fractions.map((fraction) => ({ name: fraction.name ?? `${fraction.side}:${fraction.at}`, point: sidePoint(fraction.side, fraction.at, bounds) }));
    case "points":
      return anchors.points.map((point, index) => ({ name: point.name ?? `p${index}`, point: { x: bounds.x + point.x, y: bounds.y + point.y } }));
  }
}

function compassPoint(position: string, bounds: ConnectorBox): Point {
  const midX = bounds.x + bounds.width / 2;
  const midY = bounds.y + bounds.height / 2;
  const right = bounds.x + bounds.width;
  const bottom = bounds.y + bounds.height;
  switch (position) {
    case "n": return { x: midX, y: bounds.y };
    case "ne": return { x: right, y: bounds.y };
    case "e": return { x: right, y: midY };
    case "se": return { x: right, y: bottom };
    case "s": return { x: midX, y: bottom };
    case "sw": return { x: bounds.x, y: bottom };
    case "w": return { x: bounds.x, y: midY };
    default: return { x: bounds.x, y: bounds.y };
  }
}

function sidePoint(side: "top" | "right" | "bottom" | "left", at: number, bounds: ConnectorBox): Point {
  switch (side) {
    case "top": return { x: bounds.x + at * bounds.width, y: bounds.y };
    case "right": return { x: bounds.x + bounds.width, y: bounds.y + at * bounds.height };
    case "bottom": return { x: bounds.x + (1 - at) * bounds.width, y: bounds.y + bounds.height };
    case "left": return { x: bounds.x, y: bounds.y + (1 - at) * bounds.height };
  }
}

/** The allowed anchor on the target nearest the pointer, honouring the endpoint constraint. */
function nearestAnchor(
  type: ElementTypeDefinition | undefined,
  bounds: ConnectorBox,
  point: Point,
  relation: RelationTypeDefinition,
): string | undefined {
  if (type === undefined) {
    return undefined;
  }
  const constraint = relation.endpoints.target.anchors;
  if (constraint === "edge") {
    return undefined;
  }

  const candidates = anchorPoints(type.anchors, bounds).filter(
    (anchor) => constraint === undefined || constraint === "any" || (anchor.name !== undefined && constraint.includes(anchor.name)),
  );
  let best: { name?: string; distance: number } | undefined;
  for (const candidate of candidates) {
    const distance = Math.hypot(candidate.point.x - point.x, candidate.point.y - point.y);
    if (best === undefined || distance < best.distance) {
      best = { name: candidate.name, distance };
    }
  }
  return best?.name;
}

/** Whether an invalid connect preview currently hovers this element - what renders the refusal. */
function connectTargetUnder(
  connect: ConnectPreview,
  element: DiagramModelElement,
  boundsOf: (element: DiagramModelElement) => ConnectorBox,
): boolean {
  const bounds = boundsOf(element);
  return (
    connect.point.x >= bounds.x &&
    connect.point.x <= bounds.x + bounds.width &&
    connect.point.y >= bounds.y &&
    connect.point.y <= bounds.y + bounds.height
  );
}

/** The bounds with one edge carried by a resize drag; the far edge stays put and is never crossed. */
function resizedBounds(bounds: ConnectorBox, side: "left" | "right", dx: number): ConnectorBox {
  if (side === "left") {
    const left = Math.min(bounds.x + dx, bounds.x + bounds.width - 1);
    return { ...bounds, x: left, width: bounds.x + bounds.width - left };
  }
  const width = Math.max(bounds.width + dx, 1);
  return { ...bounds, width };
}

/**
 * The dragged centre, kept inside the definition's drag bounds where it declares any - the
 * hard edge of an intrinsic space, applied to the preview and the raised position alike so
 * the user is never shown a position that cannot exist.
 */
function clampToDragBounds(
  bounds: import("./definition/diagramDefinition").ShapeBounds | undefined,
  element: DiagramModelElement,
  dx: number,
  dy: number,
): Point {
  const x = element.x + dx;
  const y = element.y + dy;
  if (bounds === undefined) {
    return { x, y };
  }
  return {
    x: Math.min(bounds.x + bounds.width, Math.max(bounds.x, x)),
    y: Math.min(bounds.y + bounds.height, Math.max(bounds.y, y)),
  };
}

function elementBounds(element: DiagramModelElement, type: ElementTypeDefinition | undefined): ConnectorBox {
  const width = element.width ?? (type?.sizing === "content" ? Math.max(DEFAULT_WIDTH, (element.label?.length ?? 0) * 8 + 16) : DEFAULT_WIDTH);
  const height = element.height ?? DEFAULT_HEIGHT;
  return { x: element.x - width / 2, y: element.y - height / 2, width, height };
}

/** The box that fits every element with a margin - what the canvas opens with and Fit returns to. */
function fitBoxOf(elements: readonly DiagramModelElement[], elementTypes: Map<string, ElementTypeDefinition>): ViewBox {
  if (elements.length === 0) {
    return { x: -200, y: -150, w: 400, h: 300 };
  }

  const boxes = elements.map((element) => elementBounds(element, elementTypes.get(element.type)));
  const minX = Math.min(...boxes.map((box) => box.x)) - FIT_PADDING;
  const minY = Math.min(...boxes.map((box) => box.y)) - FIT_PADDING;
  const maxX = Math.max(...boxes.map((box) => box.x + box.width)) + FIT_PADDING;
  const maxY = Math.max(...boxes.map((box) => box.y + box.height)) + FIT_PADDING;
  return { x: minX, y: minY, w: maxX - minX, h: maxY - minY };
}

/** The toolbox the definition implies: one item per element type, minus the suppressed, plus the added (Requirement 4.4). */
function deriveToolbox(definition: DiagramDefinition): ToolboxItem[] {
  const suppressed = new Set(definition.toolbox?.suppress ?? []);
  const derived = definition.elementTypes
    .filter((type) => !suppressed.has(type.id))
    .map((type) => create(ToolboxItemSchema, { id: type.id, label: type.id, icon: "mdi-shape-outline" }));
  const added = (definition.toolbox?.add ?? []).map((item) =>
    create(ToolboxItemSchema, { id: item.payload, label: item.title, icon: item.icon ?? "mdi-shape-outline" }),
  );
  return [...derived, ...added];
}
