import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore, Component, type ReactNode } from "react";
import { create } from "@bufbuild/protobuf";
import { ToolboxItemSchema, type ToolboxItem } from "@client/generated/diagrams_pb";
import type { ContextActionGroup } from "@client/generated/context-contract_pb";
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
import {
  beginGestureFrame,
  createGestureValue,
  valueWrite,
  type GestureFrame,
  type GestureValue,
  type LiveWrite,
  type SurfaceRect,
} from "./gestureFrame";
import { BoxElement } from "../elements/box/BoxElement";
import { CenteredBoxElement } from "../elements/centered-box/CenteredBoxElement";
import { EllipseElement } from "../elements/ellipse/EllipseElement";
import { FrameElement } from "../elements/frame/FrameElement";
import { SpanElement } from "../elements/span/SpanElement";
import { StyledBoxElement } from "../elements/styled-box/StyledBoxElement";
import { SymbolElement } from "../elements/symbol/SymbolElement";
import { InlineLabelEditor, type InlineLabelEditorProps } from "../label/InlineLabelEditor";
import { asideLabelPlacement, centredLabelPlacement, insetLabelPlacement, midpointLabelPlacement } from "../label/labelPlacement";
import { layoutLabels } from "./definition/labels";
import { resolveDecorations, type ResolvedDecoration } from "./definition/decorations";
import { resolveBackground } from "./definition/background";
import { isBackgroundRef } from "./definition/diagramDefinition";
import { CanvasScrollbars } from "../scroll/CanvasScrollbars";
import { scrollExtentOf, thumbOf } from "../scroll/scrollGeometry";
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

/** The surface's size in real pixels, or null while nothing has been measured. */
interface PaneSize {
  width: number;
  height: number;
}

/**
 * The surface's own size, measured.
 *
 * jsdom computes no layout and has no `ResizeObserver`, so this stays null there and
 * {@link shapedToPane} hands the view back untouched - which is what every existing test in
 * this repository was written against.
 */
function usePaneSize(ref: React.RefObject<SVGSVGElement | null>): PaneSize | null {
  const [size, setSize] = useState<PaneSize | null>(null);

  useLayoutEffect(() => {
    const surface = ref.current;
    if (surface === null) {
      return;
    }

    const measure = () => {
      const rect = surface.getBoundingClientRect();
      if (rect.width <= 0 || rect.height <= 0) {
        return;
      }
      setSize((current) =>
        current !== null && current.width === rect.width && current.height === rect.height
          ? current
          : { width: rect.width, height: rect.height },
      );
    };

    measure();
    if (typeof ResizeObserver === "undefined") {
      return;
    }

    const observer = new ResizeObserver(measure);
    observer.observe(surface);
    return () => observer.disconnect();
  }, [ref]);

  return size;
}

/**
 * The same view, grown on one axis until it has the pane's proportions.
 *
 * It only ever **grows**, so nothing that was visible stops being visible: the slack an svg
 * would have letterboxed becomes view the reader can actually use, centred on what was there
 * before. Growing rather than cropping is what makes it safe to apply to a fitted box - fit
 * still shows the whole diagram, with more room around it rather than less.
 */
function shapedToPane(view: ViewBox, pane: PaneSize | null): ViewBox {
  if (pane === null || view.w <= 0 || view.h <= 0) {
    return view;
  }

  const paneAspect = pane.width / pane.height;
  const viewAspect = view.w / view.h;
  // Within a rounding error of each other already: hand back the same object, so the memo
  // above keeps its identity and the view-changed effect does not fire on a no-op.
  if (Math.abs(paneAspect - viewAspect) < 1e-6) {
    return view;
  }

  if (viewAspect < paneAspect) {
    const w = view.h * paneAspect;
    return { x: view.x - (w - view.w) / 2, y: view.y, w, h: view.h };
  }

  const h = view.w / paneAspect;
  return { x: view.x, y: view.y - (h - view.h) / 2, w: view.w, h };
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

/** The dragged element's live displacement, in canvas units, clamped - one per gesture. */
interface ElementDragOffset {
  id: string;
  dx: number;
  dy: number;
}

/** A span resize in flight, in canvas units - the amendment's first added kind (R1.5). */
interface ResizeDragPreview {
  id: string;
  side: "left" | "right";
  dx: number;
}

/** A connection-adjust in flight: the waypoint under the pointer - the second added kind. */
interface AdjustDragPreview {
  connectionId: string;
  waypoint: Point;
}

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
  const rootRef = useRef<HTMLDivElement>(null);
  const [view, setView] = useState<ViewBox | null>(null);
  const [ownSelection, setOwnSelection] = useState<DiagramSelection>([]);

  // A gesture's per-frame values are NOT React state on the canvas: they flow through the
  // gesture-frame scheduler into these cells, and only the gesture's own participants
  // subscribe with snapshots that concern them - so per pointer frame React renders the
  // dragged element or the connect preview and nothing else (Requirement 1.1, the design's
  // scoped re-render). A pan writes no cell at all: its per-frame values go straight to the
  // svg's viewBox attribute and the scrollbar thumbs, and the view becomes state once, at
  // gesture end (Requirement 1.5).
  //
  // Measured on 2026-09-06 (jsdom, 30 pointer frames, best of three, same harness both
  // sides): before this scheduling, per-frame drag cost tracked drawn DOM nodes - helm
  // prometheus (196 nodes) 1.40ms, owl-time (422) 2.68ms, rdf Wikidata shape (2,086)
  // 10.44ms, rdf laureates shape (9,016) 47.78ms. After, the same drags cost 0.19, 0.21,
  // 0.23 and 0.33ms - flat across a 46x spread of drawn nodes, and a laureates drag now
  // sits between the timeline's own 100-span (0.24ms) and 1,000-span (0.49ms) readings,
  // which is the user's "prefer the timeline drag" benchmark answered with a number.
  const dragValue = useMemo(() => createGestureValue<ElementDragOffset>(), []);
  const dragFrameRef = useRef<GestureFrame<ElementDragOffset> | null>(null);
  const connectValue = useMemo(() => createGestureValue<ConnectPreview>(), []);
  const connectFrameRef = useRef<GestureFrame<ConnectPreview> | null>(null);
  const resizeValue = useMemo(() => createGestureValue<ResizeDragPreview>(), []);
  const resizeFrameRef = useRef<GestureFrame<ResizeDragPreview> | null>(null);
  const adjustValue = useMemo(() => createGestureValue<AdjustDragPreview>(), []);
  const adjustFrameRef = useRef<GestureFrame<AdjustDragPreview> | null>(null);
  const panFrameRef = useRef<GestureFrame<ViewBox> | null>(null);
  const panLatestRef = useRef<ViewBox | null>(null);
  useEffect(
    () => () => {
      // The surface is unmounting: the nodes the writes went to are going away with it,
      // so only the pending animation frames need cancelling (Requirement 2.3).
      dragFrameRef.current?.cancel();
      dragFrameRef.current = null;
      connectFrameRef.current?.cancel();
      connectFrameRef.current = null;
      resizeFrameRef.current?.cancel();
      resizeFrameRef.current = null;
      adjustFrameRef.current?.cancel();
      adjustFrameRef.current = null;
      panFrameRef.current?.cancel();
      panFrameRef.current = null;
    },
    [],
  );

  /**
   * The surface rectangle, read ONCE when a gesture begins and cached on the gesture's
   * frame - the only layout read a whole gesture performs (Requirement 1.3). A window
   * resized mid-gesture serves the stale rect until the gesture ends; the next reads afresh.
   */
  const surfaceRectAtGestureStart = useCallback((): SurfaceRect => {
    const rect = svgRef.current?.getBoundingClientRect();
    return rect !== undefined
      ? { left: rect.left, top: rect.top, width: rect.width, height: rect.height }
      : { left: 0, top: 0, width: 0, height: 0 };
  }, []);

  const selection = controlledSelection ?? ownSelection;

  const fitBox = useMemo(() => {
    if (definition.extent !== undefined) {
      // The space is definitional, not derived: fit shows exactly what the notation states.
      return { x: definition.extent.x, y: definition.extent.y, w: definition.extent.width, h: definition.extent.height };
    }
    return fitBoxOf(elements, elementTypes);
  }, [definition.extent, elements, elementTypes]);
  // The view is widened or heightened to the pane's own proportions before anything uses it.
  //
  // An svg with a viewBox scales uniformly and centres the slack (`preserveAspectRatio`'s
  // default), so a view whose shape differs from the pane's is letterboxed - and then a canvas
  // unit is not worth the same number of pixels across as it is down. Every screen mapping here
  // assumes it is: `unitsPerPixel` divides the view's width by the surface's width, the module's
  // viewport report describes the rectangle as if it filled the pane, and a ruler drawn beside
  // the canvas lays its labels out across that width. Matching the shapes makes the assumption
  // true instead of nearly true, and there is no letterbox left to centre.
  //
  // It also replaces what used to make this work by accident: the surface carried the container
  // class, took a height and no width, and so sized *itself* from the viewBox's aspect ratio.
  // The proportions matched, but the surface was then narrower than the pane and clipped the
  // diagram down a vertical line - the line moved while panning and zooming, because the view's
  // proportions did.
  const paneSize = usePaneSize(svgRef);
  const effectiveView = useMemo(() => shapedToPane(view ?? fitBox, paneSize), [view, fitBox, paneSize]);
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

  /**
   * The pan gesture's live writes: the svg's viewBox attribute and the scrollbar thumb
   * positions, straight to the DOM per applied frame - the thumbs are in the write set
   * because committing the view only at gesture end would freeze them mid-pan, and
   * Requirement 2 covers what the user watches, not only what is dispatched. Each write
   * captures what React last rendered on its first application and restores it on revert.
   */
  const panWrites = useCallback((): Array<LiveWrite<ViewBox>> => {
    const svg = svgRef.current;
    const root = rootRef.current;
    const horizontalThumb = root?.querySelector<HTMLElement>(".canvas-scrollbar-horizontal .canvas-scrollbar-thumb") ?? null;
    const verticalThumb = root?.querySelector<HTMLElement>(".canvas-scrollbar-vertical .canvas-scrollbar-thumb") ?? null;
    const horizontalExtent = scrollExtentOf(fitBox.x, fitBox.x + fitBox.w, { factor: 0.5 });
    const verticalExtent = scrollExtentOf(fitBox.y, fitBox.y + fitBox.h, { factor: 0.5 });
    let before: { viewBox: string | null; left: string; top: string } | null = null;
    return [
      {
        apply(next) {
          before ??= {
            viewBox: svg?.getAttribute("viewBox") ?? null,
            left: horizontalThumb?.style.left ?? "",
            top: verticalThumb?.style.top ?? "",
          };
          svg?.setAttribute("viewBox", `${next.x} ${next.y} ${next.w} ${next.h}`);
          if (horizontalThumb !== null) {
            horizontalThumb.style.left = `${thumbOf({ viewStart: next.x, viewSpan: next.w, ...horizontalExtent }).offset * 100}%`;
          }
          if (verticalThumb !== null) {
            verticalThumb.style.top = `${thumbOf({ viewStart: next.y, viewSpan: next.h, ...verticalExtent }).offset * 100}%`;
          }
        },
        revert() {
          if (before === null) {
            return;
          }
          if (before.viewBox !== null) {
            svg?.setAttribute("viewBox", before.viewBox);
          }
          if (horizontalThumb !== null) {
            horizontalThumb.style.left = before.left;
          }
          if (verticalThumb !== null) {
            verticalThumb.style.top = before.top;
          }
          before = null;
        },
      },
    ];
  }, [fitBox]);

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
      switch (target.kind) {
        case "background": {
          const frame = (panFrameRef.current ??= beginGestureFrame(surfaceRectAtGestureStart(), panWrites()));
          const scaleAtPress = frame.rect.width > 0 ? target.view.w / frame.rect.width : 1;
          const next = { ...target.view, x: target.view.x - dx * scaleAtPress, y: target.view.y - dy * scaleAtPress };
          panLatestRef.current = next;
          frame.move(next);
          break;
        }
        case "element": {
          if (!draggingEnabled(target.element)) {
            break; // disabled dragging: the press stays a press (Requirement 5.2)
          }
          // The frame begins on the first move: the rect is read here, once, and served
          // for the gesture's life - no layout read per pointer frame (Requirement 1.3).
          const frame = (dragFrameRef.current ??= beginGestureFrame(surfaceRectAtGestureStart(), [valueWrite(dragValue)]));
          const dragScale = frame.rect.width > 0 ? viewRef.current.w / frame.rect.width : 1;
          const at = clampToDragBounds(definition.dragBounds, target.element, dx * dragScale, dy * dragScale);
          frame.move({ id: target.element.id, dx: at.x - target.element.x, dy: at.y - target.element.y });
          break;
        }
        case "anchor": {
          const relation = relationFrom(target.element, target.anchor);
          if (relation === undefined) {
            break; // no relation may leave this element; nothing to preview
          }
          const frame = (connectFrameRef.current ??= beginGestureFrame(surfaceRectAtGestureStart(), [valueWrite(connectValue)]));
          const scale = frame.rect.width > 0 ? viewRef.current.w / frame.rect.width : 1;
          const point = { x: target.at.x + dx * scale, y: target.at.y + dy * scale };
          // Per-frame COMPUTATION, deliberately kept: the hit-test and the verdict are what
          // make refusal render under the pointer; the requirement governs rendering.
          const candidate = elementAt(point);
          const valid = candidate !== undefined && connectVerdict(relation, target.element.id, candidate);
          frame.move({
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
          const frame = (resizeFrameRef.current ??= beginGestureFrame(surfaceRectAtGestureStart(), [valueWrite(resizeValue)]));
          const scale = frame.rect.width > 0 ? viewRef.current.w / frame.rect.width : 1;
          frame.move({ id: target.element.id, side: target.side, dx: dx * scale });
          break;
        }
        case "adjust": {
          // Live feedback through the gesture cell: the adjusted connection re-renders
          // alone, carrying the waypoint under the pointer; the commit is the release.
          const frame = (adjustFrameRef.current ??= beginGestureFrame(surfaceRectAtGestureStart(), [valueWrite(adjustValue)]));
          const scale = frame.rect.width > 0 ? viewRef.current.w / frame.rect.width : 1;
          frame.move({
            connectionId: target.connection.id,
            waypoint: { x: target.from.x + dx * scale, y: target.from.y + dy * scale },
          });
          break;
        }
        case "connection":
          break; // a connection has no position; dragging one is just not a click
      }
    },
    onDragEnd: (target, dx, dy) => {
      switch (target.kind) {
        case "background": {
          // The single state write of the whole pan (Requirement 1.5): the commit undoes
          // the live viewBox and thumb writes, and setView paints the settled view through
          // the same code path as before - one render, one view-changed for the modules.
          const frame = panFrameRef.current;
          panFrameRef.current = null;
          const settled = panLatestRef.current;
          panLatestRef.current = null;
          frame?.commit();
          if (settled !== null) {
            setView(settled);
          }
          break;
        }
        case "element": {
          if (!draggingEnabled(target.element)) {
            break;
          }
          // The commit undoes the live publication and the state write below paints the
          // final position - the same code path as before the gesture, in one React batch,
          // so nothing flickers (design, The commit and the abandon).
          const frame = dragFrameRef.current;
          dragFrameRef.current = null;
          const dragScale = frame !== null && frame.rect.width > 0 ? viewRef.current.w / frame.rect.width : unitsPerPixel(viewRef.current);
          frame?.commit();
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
            position: clampToDragBounds(definition.dragBounds, target.element, dx * dragScale, dy * dragScale),
          });
          break;
        }
        case "resize": {
          const frame = resizeFrameRef.current;
          resizeFrameRef.current = null;
          const scale = frame !== null && frame.rect.width > 0 ? viewRef.current.w / frame.rect.width : unitsPerPixel(viewRef.current);
          frame?.commit();
          const bounds = resizedBounds(elementBounds(target.element, elementTypes.get(target.element.type)), target.side, dx * scale);
          raise({ kind: "element-resized", elementId: target.element.id, side: target.side, bounds });
          break;
        }
        case "anchor": {
          // The latest preview is read from the cell BEFORE the commit clears it: the
          // verdict the last applied frame rendered is exactly what the release honours.
          const preview = connectValue.get();
          const frame = connectFrameRef.current;
          connectFrameRef.current = null;
          frame?.commit();
          if (preview !== null && preview.valid && preview.target !== undefined) {
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
          } else if (preview !== null && preview.relation.emptyRelease === "complete" && elementAt(preview.point) === undefined) {
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
          const frame = adjustFrameRef.current;
          adjustFrameRef.current = null;
          const scale = frame !== null && frame.rect.width > 0 ? viewRef.current.w / frame.rect.width : unitsPerPixel(viewRef.current);
          frame?.commit();
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
      // The revert is the scheduler's: an abandonment cannot leak a publication any more
      // than it can leak an attribute, and nothing is dispatched (Requirement 2.3). For a
      // pan that means the view rolls back to where the gesture began - the abandoned
      // gesture's transient visual dissolves, exactly as the requirement words it.
      dragFrameRef.current?.revert();
      dragFrameRef.current = null;
      connectFrameRef.current?.revert();
      connectFrameRef.current = null;
      resizeFrameRef.current?.revert();
      resizeFrameRef.current = null;
      adjustFrameRef.current?.revert();
      adjustFrameRef.current = null;
      panFrameRef.current?.revert();
      panFrameRef.current = null;
      panLatestRef.current = null;
    },
  });

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

  // ---- drawing a relation with the right button (Requirement: connectOnRightDrag) ----------

  /**
   * The right-button draw, where a definition opts in. It reuses the connect preview and the
   * `connection-drawn` event the left-button anchor drag already raises - the only new thing is
   * the way it STARTS: a right press on an element body rather than on an anchor handle, so a
   * causal loop diagram links by dragging between variables. It lives here, in the one gesture
   * layer, so a module needs no gesture state of its own (the noPrivateGestures rule).
   */
  const rightConnectRef = useRef<{ element: DiagramModelElement; relation: RelationTypeDefinition; from: Point; pressX: number; pressY: number } | null>(null);
  const rightConnectMovedRef = useRef(false);

  const beginRightConnect = useCallback((event: React.PointerEvent) => {
    if (event.button !== 2 || definition.connectOnRightDrag !== true) {
      return;
    }
    const host = (event.target as Element).closest("[data-element-id]");
    const id = host?.getAttribute("data-element-id") ?? "";
    const element = elementsById.get(id);
    if (element === undefined) {
      return; // a right press on empty canvas or a connection is the menu's, not a draw's
    }
    const relation = relationFrom(element);
    if (relation === undefined) {
      return; // nothing may leave this element; leave the press to the menu
    }
    rightConnectRef.current = { element, relation, from: { x: element.x, y: element.y }, pressX: event.clientX, pressY: event.clientY };
    rightConnectMovedRef.current = false;
    try {
      svgRef.current?.setPointerCapture(event.pointerId);
    } catch {
      // A browser that refuses capture for this pointer still delivers moves by bubbling.
    }
    // No preview yet: like the anchor drag, the frame - and the preview it publishes through
    // the scheduler - begins on the first move, so an unmoved right press stays the menu's.
  }, [definition.connectOnRightDrag, elementsById, relationFrom]);

  const moveRightConnect = useCallback((event: React.PointerEvent) => {
    const active = rightConnectRef.current;
    if (active === null) {
      return;
    }
    rightConnectMovedRef.current = true;
    // Published through the SAME scheduler frame the anchor drag uses, so the preview renders
    // and the release reads back through one path (Requirement 1.1, the scoped re-render).
    const frame = (connectFrameRef.current ??= beginGestureFrame(surfaceRectAtGestureStart(), [valueWrite(connectValue)]));
    // The point is the press-anchor plus the pixel delta in canvas units, exactly as the
    // anchor drag maps its move - one rect read served for the gesture's life.
    const scale = frame.rect.width > 0 ? viewRef.current.w / frame.rect.width : 1;
    const point = { x: active.from.x + (event.clientX - active.pressX) * scale, y: active.from.y + (event.clientY - active.pressY) * scale };
    const candidate = elementAt(point);
    const valid = candidate !== undefined && candidate.id !== active.element.id && connectVerdict(active.relation, active.element.id, candidate);
    frame.move({
      relation: active.relation,
      sourceId: active.element.id,
      from: active.from,
      point,
      target: valid && candidate !== undefined
        ? { elementId: candidate.id, anchor: nearestAnchor(elementTypes.get(candidate.type), boundsOf(candidate), point, active.relation) }
        : undefined,
      valid,
    });
  }, [surfaceRectAtGestureStart, connectValue, elementAt, connectVerdict, elementTypes, boundsOf]);

  const endRightConnect = useCallback((event: React.PointerEvent) => {
    const active = rightConnectRef.current;
    if (active === null) {
      return;
    }
    try {
      svgRef.current?.releasePointerCapture(event.pointerId);
    } catch {
      // Mirror of the capture guard in beginRightConnect.
    }
    rightConnectRef.current = null;
    // The latest preview is read from the cell BEFORE the commit clears it - the verdict the
    // last applied frame rendered is exactly what the release honours, as the anchor end does.
    const preview = connectValue.get();
    const frame = connectFrameRef.current;
    connectFrameRef.current = null;
    frame?.commit();
    if (rightConnectMovedRef.current && preview !== null && preview.valid && preview.target !== undefined) {
      // Raised only because the verdict already said yes, exactly as the anchor drag does.
      raise({
        kind: "connection-drawn",
        relationType: preview.relation.id,
        sourceElementId: preview.sourceId,
        targetElementId: preview.target.elementId,
        sourceAnchor: preview.sourceAnchor,
        targetAnchor: preview.target.anchor,
      });
    }
  }, [connectValue, raise]);

  // An interrupted right draw (pointer cancel, capture lost) reverts the shared connect frame
  // rather than committing it: were it left set, the next anchor drag's `??=` would reuse this
  // stale frame. Nothing is raised - an abandonment states no link.
  const cancelRightConnect = useCallback(() => {
    if (rightConnectRef.current === null) {
      return;
    }
    rightConnectRef.current = null;
    const frame = connectFrameRef.current;
    connectFrameRef.current = null;
    frame?.revert();
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
    // A right-button drag that drew a relation is not also a menu: the contextmenu that follows
    // the release is swallowed rather than opening a menu over the new connection.
    if (rightConnectMovedRef.current) {
      rightConnectMovedRef.current = false;
      event.preventDefault();
      return;
    }
    openMenuAt(event, id);
  };

  /** The surface's own right-click: only the tail of a completed draw reaches here, and it is swallowed. */
  const onSurfaceContextMenu = (event: React.MouseEvent) => {
    if (rightConnectMovedRef.current) {
      rightConnectMovedRef.current = false;
      event.preventDefault();
    }
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
      dragValue={dragValue}
      resizeValue={resizeValue}
      resizable={elementTypes.get(element.type)?.sizing === "user"}
      resizePress={(side) => gesture.press({ kind: "resize", element, side })}
      selected={isSelected("element", element.id)}
      connectValue={connectValue}
      press={gesture.press({ kind: "element", element })}
      anchorPress={(anchor, at) => gesture.press({ kind: "anchor", element, anchor, at })}
      onContextMenu={onItemContextMenu(element.id)}
    />
  );

  // The surface's own pan/deselect wiring, composed with the right-drag connect on the svg below
  // so both live in this one gesture layer rather than in a module.
  const backgroundWiring = gesture.background({ kind: "background", view: effectiveView });

  return (
    <div ref={rootRef} className={`library-canvas ${className ?? ""}`.trim()} data-testid="library-canvas">
      <svg
        ref={svgRef}
        // `canvas-drawing`, never `canvas-host`: the host class is for the *container* and
        // gives height without width, so the svg falls back to sizing its width from the
        // viewBox's aspect ratio. An svg clips to its own viewport, so a surface narrower than
        // the pane it sits in cuts the diagram off down a vertical line - one that moves as
        // the viewBox's proportions change, which is why it appeared while panning and
        // zooming. Every hand-written canvas in the repository already wears `canvas-drawing`.
        className="canvas-drawing library-canvas-surface"
        viewBox={`${effectiveView.x} ${effectiveView.y} ${effectiveView.w} ${effectiveView.h}`}
        tabIndex={0}
        role="img"
        aria-label={ariaLabel ?? "Diagram"}
        onPointerDownCapture={(event) => { endEditBeforeGesture(event); beginRightConnect(event); }}
        onPointerDown={backgroundWiring.onPointerDown}
        onPointerMove={(event) => { moveRightConnect(event); backgroundWiring.onPointerMove(event); }}
        onPointerUp={(event) => { endRightConnect(event); backgroundWiring.onPointerUp(event); }}
        onPointerCancel={(event) => { cancelRightConnect(); backgroundWiring.onPointerCancel(event); }}
        onLostPointerCapture={(event) => { cancelRightConnect(); backgroundWiring.onLostPointerCapture(event); }}
        onContextMenu={onSurfaceContextMenu}
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
          <g className="library-canvas-background" data-testid="canvas-background" pointerEvents="none" aria-hidden="true">
            {isBackgroundRef(definition.background)
              ? (definition.background.render({ x: effectiveView.x, y: effectiveView.y, width: effectiveView.w, height: effectiveView.h }) as ReactNode)
              : declaredBackground(definition.background, model, {
                  x: effectiveView.x,
                  y: effectiveView.y,
                  width: effectiveView.w,
                  height: effectiveView.h,
                })}
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
            adjustValue={adjustValue}
            selected={isSelected("connection", connection.id)}
            press={gesture.press({ kind: "connection", connection })}
            adjustPress={(from) => gesture.press({ kind: "adjust", connection, from })}
            onContextMenu={onItemContextMenu(connection.id)}
          />
        ))}

        {aboveConnections.map((element) => renderLibraryElement(element))}

        <ConnectPreviewLayer value={connectValue} />

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

/**
 * The connect gesture's own participant: the one component that re-renders per applied
 * frame, drawing the preview routed and styled as the relation prescribes (Requirement 5.4).
 * Subscribing here rather than holding the preview as canvas state is what keeps a connect
 * drag from re-rendering every element per pointer frame (Requirement 1.5).
 */
function ConnectPreviewLayer({ value }: { value: GestureValue<ConnectPreview> }) {
  const preview = useSyncExternalStore(value.subscribe, value.get);
  if (preview === null) {
    return null;
  }
  return (
    <path
      className={`library-connect-preview${preview.valid ? "" : " library-connect-preview-invalid"}`}
      data-testid="connect-preview"
      d={routePath(preview.relation, preview.from, preview.point, [])}
      pointerEvents="none"
    />
  );
}

function LibraryElement({
  element,
  type,
  dragValue,
  connectValue,
  resizeValue,
  resizable,
  resizePress,
  selected,
  press,
  anchorPress,
  onContextMenu,
}: {
  element: DiagramModelElement;
  type: ElementTypeDefinition | undefined;
  dragValue: GestureValue<ElementDragOffset>;
  connectValue: GestureValue<ConnectPreview>;
  resizeValue: GestureValue<ResizeDragPreview>;
  resizable: boolean;
  resizePress: (side: "left" | "right") => PointerPressWiring;
  selected: boolean;
  press: PointerPressWiring;
  anchorPress: (anchor: string | undefined, at: Point) => PointerPressWiring;
  onContextMenu: (event: React.MouseEvent) => void;
}) {
  // The scoped re-render: each element subscribes with a snapshot that is null unless the
  // published displacement is ITS OWN, so a per-frame publication re-renders the dragged
  // element in place - rings, clamps and state classes stay live - while every other
  // element's snapshot is unchanged and React renders it zero times (Requirement 1.1).
  const offset = useSyncExternalStore(dragValue.subscribe, () => {
    const value = dragValue.get();
    return value !== null && value.id === element.id ? value : null;
  });
  // The connect highlight subscribes the same way, with a PRIMITIVE snapshot: per applied
  // frame every element answers "does this preview concern me" - a bounds check, which is
  // per-frame computation - and only an element whose answer CHANGED re-renders, so the
  // refusal still renders under the pointer (Requirement 4.3) at transition cost.
  const connectHighlight = useSyncExternalStore(connectValue.subscribe, (): "valid" | "invalid" | undefined => {
    const preview = connectValue.get();
    if (preview === null) {
      return undefined;
    }
    if (preview.target?.elementId === element.id) {
      return "valid";
    }
    return !preview.valid && connectTargetUnder(preview, element, (candidate) => elementBounds(candidate, type)) ? "invalid" : undefined;
  });
  // The resize preview rides the same seam: only the element whose edge is being dragged
  // re-renders per frame with its live bounds - the amendment's first added kind (R1.5).
  const resize = useSyncExternalStore(resizeValue.subscribe, () => {
    const value = resizeValue.get();
    return value !== null && value.id === element.id ? value : null;
  });
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
        {renderShape(resizing, type, bounds, { selected, dragging: offset !== null, connectTarget: connectHighlight === "valid" })}
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
  adjustValue,
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
  adjustValue: GestureValue<AdjustDragPreview>;
  selected: boolean;
  press: PointerPressWiring;
  adjustPress: (from: Point) => PointerPressWiring;
  onContextMenu: (event: React.MouseEvent) => void;
}) {
  // The adjust preview: only the connection whose handle is being dragged re-renders per
  // frame, redrawing its route through the waypoint under the pointer - the amendment's
  // second added kind (R1.5). Subscribed before the early returns, per the rules of hooks.
  const liveAdjust = useSyncExternalStore(adjustValue.subscribe, () => {
    const value = adjustValue.get();
    return value !== null && value.connectionId === connection.id ? value : null;
  });

  if (relation === undefined) {
    return null; // an undeclared relation type has nothing to route; the validator rejects it upstream
  }

  const ends = connectionEnds(connection, elementsById, elementTypes, attachmentPoint);
  if (ends === null) {
    return null; // a dangling end is the module's model bug to notice; there is nothing to draw
  }

  const [from, to] = ends;
  const waypoints = liveAdjust !== null ? [liveAdjust.waypoint] : connection.waypoints ?? [];
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
/**
 * An element's drawing: its shape, and the lines its type declares.
 *
 * Declared labels are drawn as siblings ON TOP of the shape rather than inside it, so a
 * built-in shape needs no knowledge of them and a module needs no renderer to place a second
 * line - which is the whole point of the addition. Geometry comes from `layoutLabels`, which is
 * pure and tested apart from React.
 */
function renderShape(
  element: DiagramModelElement,
  type: ElementTypeDefinition | undefined,
  bounds: ConnectorBox,
  state?: import("./definition/diagramDefinition").CustomShapeState,
): ReactNode {
  const body = renderShapeBody(element, type, bounds, state);
  if (type?.labels === undefined && type?.decorations === undefined) {
    return body;
  }

  return (
    <>
      {body}
      {type?.decorations ? declaredDecorations(element, type, bounds) : null}
      {type?.labels ? declaredLabels(element, type, bounds) : null}
    </>
  );
}

/**
 * The ornaments a type's `decorations` declare.
 *
 * Drawn BENEATH the declared labels and above the shape, and carrying `pointerEvents: none`
 * throughout: a decoration takes no gesture, which is the line that keeps it an ornament rather
 * than a second kind of element. Anything that needs to be clicked is an element type.
 */
function declaredDecorations(element: DiagramModelElement, type: ElementTypeDefinition, bounds: ConnectorBox): ReactNode {
  const resolved = resolveDecorations(type.decorations, { element, payload: element.payload }, bounds);
  return resolved.map((decoration) => (
    <g key={decoration.index} className={decoration.className} style={{ pointerEvents: "none" }} aria-hidden="true">
      {decorationGlyph(decoration)}
      {decoration.text !== undefined ? (
        <text
          className="library-decoration-text"
          x={decoration.textAt.x}
          y={decoration.textAt.y}
          textAnchor={decoration.textAnchor}
          style={{
            fontSize: decoration.typography?.fontSize,
            fontWeight: decoration.typography?.fontWeight,
            fontStyle: decoration.typography?.fontStyle,
            fill: tokenColour(decoration.typography?.color),
          }}
        >
          {decoration.text}
        </text>
      ) : null}
    </g>
  ));
}

/** One glyph of the closed set. A `marker` with no name draws nothing but its text. */
function decorationGlyph(decoration: ResolvedDecoration): ReactNode {
  switch (decoration.glyph) {
    case "line":
      return <line x1={decoration.from.x} y1={decoration.from.y} x2={decoration.to.x} y2={decoration.to.y} />;
    case "path":
      return decoration.d !== undefined ? <path d={decoration.d} /> : null;
    case "circle":
      return <circle cx={decoration.from.x} cy={decoration.from.y} r={decoration.radius} />;
    case "rect":
      return (
        <rect
          x={decoration.from.x}
          y={decoration.from.y}
          width={decoration.width}
          height={decoration.height}
          rx={decoration.radius}
        />
      );
    case "marker":
      return decoration.marker !== undefined ? (
        <line
          x1={decoration.from.x}
          y1={decoration.from.y}
          x2={decoration.to.x}
          y2={decoration.to.y}
          markerEnd={`url(#library-${decoration.marker})`}
        />
      ) : null;
  }
}


/**
 * The declared backdrop: bands, axes, gridlines and regions, in the view's own units.
 *
 * It resolves against the MODEL's background rather than an element's payload, because no
 * element owns the axis. The synthetic element exists only to satisfy the one binding source
 * shape every resolver takes - it carries no data and nothing reads it.
 */
function declaredBackground(
  background: import("./definition/background").BackgroundDeclaration,
  model: DiagramModel,
  extent: { x: number; y: number; width: number; height: number },
): ReactNode {
  const resolved = resolveBackground(
    background,
    { element: { id: "__background__", type: "__background__", x: 0, y: 0 }, payload: model.background },
    extent,
  );

  return (
    <>
      {resolved.rects.map((rect) => (
        <rect key={rect.key} className={rect.className} x={rect.x} y={rect.y} width={rect.width} height={rect.height} />
      ))}
      {resolved.lines.map((line) => (
        <line key={line.key} className={line.className} x1={line.x1} y1={line.y1} x2={line.x2} y2={line.y2} />
      ))}
      {resolved.texts.map((text) => (
        <text
          key={text.key}
          className={text.className}
          x={text.rotate === undefined ? text.x : undefined}
          y={text.rotate === undefined ? text.y : undefined}
          transform={text.rotate === undefined ? undefined : `translate(${text.x} ${text.y}) rotate(${text.rotate})`}
          textAnchor={text.anchor}
        >
          {text.text}
        </text>
      ))}
    </>
  );
}

/** The lines a type's `labels` declare, positioned and painted. */
function declaredLabels(element: DiagramModelElement, type: ElementTypeDefinition, bounds: ConnectorBox): ReactNode {
  const lines = layoutLabels(type.labels, { element, payload: element.payload }, bounds);
  return lines.map((line) => (
    <text
      key={`${line.declarationIndex}-${line.lineIndex}`}
      className={["library-element-label", line.className].filter(Boolean).join(" ")}
      x={line.x}
      y={line.y}
      textAnchor={line.anchor}
      style={{
        fontSize: line.typography?.fontSize,
        fontWeight: line.typography?.fontWeight,
        fontStyle: line.typography?.fontStyle,
        fill: tokenColour(line.typography?.color),
      }}
    >
      {line.tooltip ? <title>{line.tooltip}</title> : null}
      {line.text}
    </text>
  ));
}

/**
 * The shape itself, and the single `label` a built-in carries.
 *
 * A type declaring `labels` passes an EMPTY string here and draws its text through
 * {@link declaredLabels} instead - the two never compose, so a reader never has to work out
 * which line came from which mechanism. An unmigrated type is untouched by any of this.
 */
function renderShapeBody(
  element: DiagramModelElement,
  type: ElementTypeDefinition | undefined,
  bounds: ConnectorBox,
  state?: import("./definition/diagramDefinition").CustomShapeState,
): ReactNode {
  const label = type?.labels !== undefined ? "" : (element.label ?? "");
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
