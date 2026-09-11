import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore, Component, type ReactNode } from "react";
import { create } from "@bufbuild/protobuf";
import { ToolboxItemSchema, type ToolboxItem } from "@client/generated/diagrams_pb";
import {
  arcPath,
  edgePointOf,
  midpointOf,
  orthogonalPath,
  polylinePath,
  quadraticBezierPath,
  sideAnchorOf,
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
import { actionForGesture, actionForKey } from "./definition/actions";
import { isCustomShape } from "./definition/diagramDefinition";
import { CanvasScrollbars } from "../scroll/CanvasScrollbars";
import { scrollExtentOf, thumbOf } from "../scroll/scrollGeometry";
import { useElementContextMenu } from "../useElementContextMenu";
import { useLibrarySelection, type CanvasSource, type LibraryContextIntegration } from "./librarySelection";
import { ACCEPT_RING_STYLE, SELECTED_RING_STYLE } from "./ringLooks";
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
  type LibraryEventHandlers,
  type DiagramSelection,
  type SelectedItem,
} from "./api/diagramEvents";
import { effectiveDefinition, type DiagramRuntimeConfig } from "./api/diagramRuntimeConfig";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "./api/diagramModel";
import { layoutAlgorithmFor, type LayoutInput } from "./layout/layoutAlgorithm";
import type {
  AnchorSet,
  BuiltInShape,
  CustomShapeState,
  DiagramDefinition,
  DropTargetDeclaration,
  ElementTypeDefinition,
  RelationTypeDefinition,
  ShapeSelection,
} from "./definition/diagramDefinition";
import { holds, resolveOne, type Binding, type BindingSource } from "./definition/binding";

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
   * Which diagram this canvas draws - the shell's `entryId` and `path`. **Given, the library owns
   * selection**: it reads the backend's selection, pushes a press's, and wires the shared context
   * menu itself, so the module writes no selection code at all (centralized-selection
   * Requirement 1). There is nothing else to pass: `selection`, `context` and `onSelectionChanged`
   * are not part of this contract, so a module cannot hand-wire selection. Omitted - a library test,
   * a picture with no backend - the canvas highlights its own last press and tells nobody.
   */
  source?: CanvasSource;
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
  /**
   * Released, and waiting for the model to confirm where it landed - still drawn at the drop,
   * no longer being dragged. See the release in the element case below.
   */
  held?: boolean;
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
export function DiagramCanvas(props: DiagramCanvasProps) {
  // Two components rather than one with a conditional hook: the library-owned path reads the
  // context channel, and a canvas mounted without a source - most library tests - must not have
  // to supply a provider it does not use.
  return props.source !== undefined ? <LibraryOwnedCanvas {...props} source={props.source} /> : <DiagramCanvasCore {...props} />;
}

/**
 * What the canvas core takes: the module contract, plus the three things only the library itself
 * supplies - the resolved selection, the menu's wiring, and the handler map with the library's own
 * `selection-changed` in it. <b>Library-internal</b>: the library's selection wrapper and the
 * library's own tests mount the core; a module mounts {@link DiagramCanvas}, and the text guard
 * fails one that reaches for this.
 */
export interface DiagramCanvasCoreProps extends Omit<DiagramCanvasProps, "events" | "source"> {
  events: LibraryEventHandlers;
  /** The selection to draw - the backend's, resolved by the library. Omitted, the core keeps its own last press. */
  selection?: DiagramSelection;
  /** The shared menu's wiring. Omitted, the core offers no menu. */
  context?: LibraryContextIntegration;
}

/** The canvas with its selection owned by the library: the one place the three props are made. */
function LibraryOwnedCanvas(props: DiagramCanvasProps & { source: CanvasSource }) {
  const { selection, context, events } = useLibrarySelection(props.source, props.model, props.definition, props.events);
  return <DiagramCanvasCore {...props} selection={selection} context={context} events={events} />;
}

/** The canvas itself, beneath the selection wrapper. Library-internal - see {@link DiagramCanvasCoreProps}. */
export function DiagramCanvasCore({
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
}: DiagramCanvasCoreProps) {
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
  /** The element held at its drop, and where the model had it before - see the release. */
  const heldDropRef = useRef<{ id: string; fromX: number; fromY: number } | null>(null);

  // THE MODEL'S ANSWER RELEASES THE HOLD. Once the element sits somewhere other than where the
  // drag found it, the model is placing it and the held offset must go - left in place it would
  // be applied a second time on top of the confirmed position. An element that left the model
  // releases it too, since there is nothing left to hold.
  useEffect(() => {
    const held = heldDropRef.current;
    if (held === null) {
      return;
    }

    const element = model.elements.find((candidate) => candidate.id === held.id);
    if (element === undefined || element.x !== held.fromX || element.y !== held.fromY) {
      heldDropRef.current = null;
      dragValue.clear();
    }
  }, [model.elements, dragValue]);
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

      if (type !== undefined && isCustomShape(type.shape)) {
        return type.shape.edgePoint(bounds, towards);
      }

      // ConnectorBox is CENTRE-based while the library's bounds are corner-based; the
      // conversion here is load-bearing - without it every edge left from the corner as if
      // it were the centre, and the guard above this comment's test was seen to fail on it.
      const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
      const box = { x: centre.x, y: centre.y, width: bounds.width, height: bounds.height };

      // An axis-constrained edge: the side facing the other end, whatever the angle. What
      // three canvases spell in a custom `edgePoint` today, and one of the two reasons those
      // shapes exist at all.
      const sides = type?.anchors.edgeSides ?? "all";
      if (sides === "horizontal") {
        return sideAnchorOf(box, towards.x >= box.x ? "right" : "left");
      }

      if (sides === "vertical") {
        return { x: box.x, y: towards.y >= box.y ? box.y + box.height / 2 : box.y - box.height / 2 };
      }

      return edgePointOf(box, towards.x - centre.x, towards.y - centre.y);
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

  // A press on a type declared unselectable is a press on the background: the selection clears
  // (centralized-selection Requirement 2.3). Omitted means selectable, so nothing changes for a
  // definition that says nothing.
  const selectableElement = (element: DiagramModelElement) => elementTypes.get(element.type)?.selectable !== false;
  const selectableConnection = (connection: DiagramModelConnection) => relationTypes.get(connection.type)?.selectable !== false;

  const gesture = usePointerGesture<PressTarget>({
    onPress: (target) => {
      switch (target.kind) {
        case "element":
          select(selectableElement(target.element) ? { kind: "element", id: target.element.id } : null);
          break;
        case "connection":
        case "adjust":
          select(selectableConnection(target.connection) ? { kind: "connection", id: target.connection.id } : null);
          break;
        case "anchor":
          // An unmoved press on an anchor selects its element: the anchor is part of it.
          select(selectableElement(target.element) ? { kind: "element", id: target.element.id } : null);
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
          heldDropRef.current = null; // a new gesture replaces any drop still awaiting its answer
          const frame = (dragFrameRef.current ??= beginGestureFrame(surfaceRectAtGestureStart(), [valueWrite(dragValue)]));
          const dragScale = frame.rect.width > 0 ? viewRef.current.w / frame.rect.width : 1;
          const at = dragLanding(definition, target.element, dx * dragScale, dy * dragScale);
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
          const frame = dragFrameRef.current;
          dragFrameRef.current = null;
          const dragScale = frame !== null && frame.rect.width > 0 ? viewRef.current.w / frame.rect.width : unitsPerPixel(viewRef.current);
          // Under an automatic layout the definition says what the drag MEANS (Requirement
          // 8.4): a reclaimed displacement raises nothing - the next layout pass takes the
          // element back - while repin-to-manual raises the move, and the module answers by
          // recording the position its manual placement will then honour.
          if (activeLayoutMode !== "manual" && (definition.layout.dragUnderAutomaticLayout ?? "repin-to-manual") === "reclaimed-displacement") {
            frame?.commit();
            break;
          }

          const landing = dragLanding(definition, target.element, dx * dragScale, dy * dragScale);

          // HOLD THE DROP UNTIL THE MODEL ANSWERS, instead of reverting at once.
          //
          // The release used to undo the live publication immediately, on the assumption that the
          // module writes the new position in the same React batch - so nothing flickers. That
          // holds for a module with local state and not for one that asks its backend and waits,
          // which is most of them: between release and the confirming delta the element was drawn
          // at the model's position, the OLD one. Ansible showed it as a flash back to where the
          // drag began; dotnet-dependency-graph, whose backend also failed to confirm at all,
          // showed it until a zoom forced a redraw. So the offset now stays - no longer a drag,
          // still drawn - and the model's next position for this element releases it.
          //
          // NOT for a drop that is a PROPOSAL. A definition that declares a `dropTarget` (the
          // mindmap) means a drop re-parents, and the element's resulting place is the layout's,
          // not the cursor's - holding it at the cursor would be showing a position it will never
          // have. Nor for a drop that landed where it started: there is nothing to wait for.
          const positional = definition.dropTarget === undefined;
          const moved = landing.x !== target.element.x || landing.y !== target.element.y;
          if (positional && moved) {
            frame?.cancel(); // stop the frame WITHOUT reverting what it published
            heldDropRef.current = { id: target.element.id, fromX: target.element.x, fromY: target.element.y };
            // Set explicitly rather than trusting the last frame: frames coalesce, and the drop
            // must be drawn at exactly the landing the module is told about.
            dragValue.set({ id: target.element.id, dx: landing.x - target.element.x, dy: landing.y - target.element.y, held: true });
          } else {
            frame?.commit();
          }

          raise({ kind: "element-moved", elementId: target.element.id, position: landing });
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

    // A DECLARED ACTION FIRST, and only for a type that declares one.
    //
    // Inert for the twelve modules that have not migrated: `definition.actions` undefined means
    // this whole branch is skipped and the delete path below behaves exactly as it always has.
    // That is Requirement 8.1 at the library level - the addition changes nothing until a module
    // asks for it.
    if (definition.actions !== undefined || [...elementTypes.values()].some((type) => type.actions !== undefined)) {
      if (dispatchDeclaredAction(event)) {
        return;
      }
    }

    if (event.key !== "Delete" && event.key !== "Backspace") {
      return;
    }

    for (const item of selection) {
      if (item.kind === "element") {
        const element = elementsById.get(item.id);
        const type = element !== undefined ? elementTypes.get(element.type) : undefined;

        // A declared delete action replaces the synthesised keystroke: the module hears its own
        // action id rather than building `{ key: "Delete", ... }` to say the same thing.
        const declared =
          element === undefined
            ? null
            : actionForGesture(
                {
                  actions: [...(definition.actions ?? []), ...(type?.actions ?? [])],
                  targetKind: "element",
                  targetId: item.id,
                  typeId: element.type,
                  source: { element, payload: element.payload },
                },
                "delete",
              );

        if (declared !== null) {
          event.preventDefault();
          raise({ kind: "action-invoked", ...declared });
        } else if (element !== undefined && (type?.deletable ?? true)) {
          event.preventDefault();
          raise({ kind: "element-deleted", elementId: item.id });
        }
      } else {
        const connection = model.connections.find((candidate) => candidate.id === item.id);
        const declared =
          connection === undefined
            ? null
            : actionForGesture(
                {
                  actions: definition.actions,
                  targetKind: "connection",
                  targetId: item.id,
                  typeId: connection.type,
                  source: { element: { id: item.id, type: connection.type, x: 0, y: 0 } },
                },
                "delete",
              );

        event.preventDefault();
        raise(declared !== null ? { kind: "action-invoked", ...declared } : { kind: "connection-deleted", connectionId: item.id });
      }
    }
  };

  /**
   * A declared gesture on ONE element - a double-click, a right-click - dispatched by action id.
   *
   * Three canvases hang `onDoubleClick` and `onContextMenu` on the element they render, which is
   * the other half of why they need a custom shape: not the drawing, the handlers attached to
   * it. Returns true when an action fired, so the caller can leave its own behaviour alone when
   * none did - the addition changes nothing for a module that declares no such action.
   */
  const dispatchElementGesture = useCallback(
    (elementId: string, gesture: "activate" | "context-menu"): boolean => {
      const element = elementsById.get(elementId);
      if (element === undefined) {
        return false;
      }

      const type = elementTypes.get(element.type);
      const declared = actionForGesture(
        {
          actions: [...(definition.actions ?? []), ...(type?.actions ?? [])],
          targetKind: "element",
          targetId: elementId,
          typeId: element.type,
          source: { element, payload: element.payload },
        },
        gesture,
      );

      if (declared === null) {
        return false;
      }

      raise({ kind: "action-invoked", ...declared });
      return true;
    },
    [elementsById, elementTypes, definition.actions, raise],
  );

  /** A declared shortcut, dispatched by action id. True when one fired. */
  function dispatchDeclaredAction(event: React.KeyboardEvent): boolean {
    for (const item of selection) {
      const element = item.kind === "element" ? elementsById.get(item.id) : undefined;
      const type = element !== undefined ? elementTypes.get(element.type) : undefined;
      const connection = item.kind === "connection" ? model.connections.find((c) => c.id === item.id) : undefined;

      const found = actionForKey(
        {
          actions: [...(definition.actions ?? []), ...(type?.actions ?? [])],
          targetKind: item.kind === "element" ? "element" : "connection",
          targetId: item.id,
          typeId: element?.type ?? connection?.type,
          source: { element: element ?? { id: item.id, type: connection?.type ?? "", x: 0, y: 0 }, payload: element?.payload },
        },
        { key: event.key, ctrlKey: event.ctrlKey, shiftKey: event.shiftKey, altKey: event.altKey, metaKey: event.metaKey },
      );

      if (found !== null) {
        event.preventDefault();
        raise({ kind: "action-invoked", ...found });
        return true;
      }
    }

    return false;
  }

  // ---- context menu (Requirement 7.2) ------------------------------------------------------

  const { menuPosition, openMenuAt, closeMenu } = useElementContextMenu(
    context?.selectionKey,
    (context?.actions.length ?? 0) > 0,
    (id) => context?.selectForMenu(id),
  );

  const onItemContextMenu = (id: string) => (event: React.MouseEvent) => {
    // A declared context-menu action takes it first: the module hears its own action id, which
    // is what its `onContextMenu` did by hand. Nothing declared, and this behaves exactly as it
    // always has.
    if (dispatchElementGesture(id, "context-menu")) {
      event.preventDefault();
      return;
    }

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

  /**
   * The surface's own right-click. The tail of a completed right-button draw is swallowed; on a
   * canvas declaring `backgroundMenu`, a right-click on empty canvas opens the shared menu for the
   * point clicked - the placement `new:x,y` in canvas coordinates, the convention drops already
   * use - on the backend's answer, like any other target. Undeclared, it does nothing, as before.
   */
  const onSurfaceContextMenu = (event: React.MouseEvent) => {
    if (rightConnectMovedRef.current) {
      rightConnectMovedRef.current = false;
      event.preventDefault();
      return;
    }

    const onAnItem = (event.target as Element).closest("[data-element-id],[data-connection-id]") !== null;
    if (definition.backgroundMenu === true && context !== undefined && !onAnItem) {
      const at = toCanvasPoint(event.clientX, event.clientY);
      openMenuAt(event, `new:${at.x},${at.y}`);
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

        /*
         * A DECLARED label's editor, which is register entry G21 - found by the reference
         * migration rather than by the sufficiency table, because it is not something an
         * element DRAWS.
         *
         * `labels` replaced `label` for drawing in task 2, and this function was left reading
         * only the deprecated rule: a migrated type marked its label editable and got no
         * editor at all. Silent, and invisible to every unit test of `layoutLabels`, which is
         * exactly the class of thing the migration exists to catch.
         *
         * The editable line's own declaration decides where the editor opens, so the editor is
         * over the text rather than over the element - the property `insetLabelPlacement` was
         * written for and the one a three-line card needs.
         */
        const declared = (type?.labels ?? []).find((declaration) => declaration.editable === true);
        if (declared !== undefined) {
          const bounds = elementBounds(element, type);
          const text = element.label ?? "";
          if (declared.placement === "beside") {
            return element.labelAt !== undefined
              ? asideLabelPlacement(element.labelAt, 0, text)
              : asideLabelPlacement({ x: bounds.x + bounds.width, y: bounds.y + bounds.height / 2 }, 6, text);
          }

          // A PLAIN CENTRED LABEL OPENS OVER THE ELEMENT, which is what `placement: "inside"`
          // meant and what a single-label type wants: the editor is the box. A label that
          // states an offset or an editor box is one line of a composite card, and opens over
          // that line instead. Derived from the typography, a plain label produced a 20px
          // editor over a 56px box, which databricks' own test caught.
          if (declared.offset === undefined && declared.editorBox === undefined) {
            return centredLabelPlacement({ x: element.x, y: element.y, width: bounds.width, height: bounds.height }, text);
          }

          const lines = layoutLabels([declared], sourceOf(element), bounds);
          const line = lines.find((candidate) => candidate.editable);
          if (line === undefined) {
            // The declaration is editable but this element draws no line for it - an absent
            // field, a failed condition. There is nothing to open an editor over.
            return null;
          }

          // The declaration's own editor box where it states one - the port of `insetTop` and
          // `insetHeight` - and otherwise derived from the line's baseline and its size.
          const height = declared.editorBox?.height ?? (declared.typography?.fontSize ?? 12) + 8;
          const top = declared.editorBox?.top ?? line.y - bounds.y - height + 4;
          return insetLabelPlacement({ x: element.x, y: element.y, width: bounds.width, height: bounds.height }, top, height, text);
        }

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
      onDoubleClick={() => dispatchElementGesture(element.id, "activate")}
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
          <g
            className={["library-canvas-background", definition.background.className].filter(Boolean).join(" ")}
            data-testid="canvas-background"
            pointerEvents="none"
            aria-hidden="true"
          >
            {declaredBackground(
              definition.background,
              model,
              // THE DIAGRAM'S OWN PLANE where it declares one, and the view otherwise. A
              // backdrop's fractions describe the notation's space - a Wardley map's evolution
              // bands are at fixed places ON THE MAP, not at fixed places on screen. Anything
              // that must stay put as the view moves is `chrome`, which is where the timeline's
              // ruler went for exactly this reason.
              definition.extent ?? { x: effectiveView.x, y: effectiveView.y, width: effectiveView.w, height: effectiveView.h },
              // The view over the diagram's OWN extent: the ratio a label declaring
              // `scaleWithView` grows by, so text keeps a readable size on screen while the
              // geometry it names does not. One when a diagram declares no extent, which is
              // every diagram whose plane is whatever its contents span.
              definition.extent !== undefined && definition.extent.width > 0 ? effectiveView.w / definition.extent.width : 1,
            )}
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
            dragValue={dragValue}
            selected={isSelected("connection", connection.id)}
            press={gesture.press({ kind: "connection", connection })}
            adjustPress={(from) => gesture.press({ kind: "adjust", connection, from })}
            onContextMenu={onItemContextMenu(connection.id)}
          />
        ))}

        {aboveConnections.map((element) => renderLibraryElement(element))}

        {definition.dropTarget !== undefined && (
          <DropTargetLayer
            value={dragValue}
            declaration={definition.dropTarget}
            elements={model.elements}
            elementTypes={elementTypes}
          />
        )}

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

/**
 * What a drag would land on, drawn while it is in flight - register entry G12.
 *
 * <b>It subscribes to the drag the way the connect preview does</b>, so a drag re-renders this
 * one component per frame and no element at all. That is the whole reason the candidate is
 * computed here rather than pushed into a module's state: a per-frame `setState` in a module
 * would re-render every element of the diagram, which is the cost the library's gesture
 * plumbing was built to avoid.
 *
 * The DECISION about what a drop means stays the module's, in `onElementMoved`. This draws the
 * proposal and raises nothing.
 */
function DropTargetLayer({
  value,
  declaration,
  elements,
  elementTypes,
}: {
  value: GestureValue<ElementDragOffset>;
  declaration: DropTargetDeclaration;
  elements: readonly DiagramModelElement[];
  elementTypes: Map<string, ElementTypeDefinition>;
}) {
  const drag = useSyncExternalStore(value.subscribe, value.get);
  if (drag === null) {
    return null;
  }

  const dragged = elements.find((element) => element.id === drag.id);
  if (dragged === undefined) {
    return null;
  }

  const at = { x: dragged.x + drag.dx, y: dragged.y + drag.dy };
  const candidate = dropCandidate(elements, elementTypes, declaration.parentPath, dragged.id, at);
  if (candidate === undefined) {
    return null;
  }

  const candidateBounds = elementBounds(candidate, elementTypes.get(candidate.type));
  const draggedBounds = elementBounds({ ...dragged, ...at }, elementTypes.get(dragged.type));
  const from = facingSide(candidateBounds, draggedBounds);
  const to = facingSide(draggedBounds, candidateBounds);

  return (
    <g className={declaration.group?.className} {...dataAttributesOf(resolvedStatic(declaration.group?.data))} pointerEvents="none">
      <rect
        className={declaration.ring?.className}
        {...dataAttributesOf(resolvedStatic(declaration.ring?.data))}
        x={candidateBounds.x}
        y={candidateBounds.y}
        width={candidateBounds.width}
        height={candidateBounds.height}
      />
      <path
        className={declaration.preview?.className}
        {...dataAttributesOf(resolvedStatic(declaration.preview?.data))}
        d={horizontalBezierPath(from, to)}
      />
    </g>
  );
}

/**
 * The element a drop would land on, or none.
 *
 * <b>The topmost hit decides, even when it is not allowed.</b> A node held over one of its own
 * descendants offers NO parent rather than the next box down - which is the recorded behaviour,
 * and the right one: the reader is pointing at that node, and quietly re-parenting somewhere
 * else because the obvious answer was refused would be worse than refusing.
 */
function dropCandidate(
  elements: readonly DiagramModelElement[],
  elementTypes: Map<string, ElementTypeDefinition>,
  parentPath: string,
  draggedId: string,
  at: Point,
): DiagramModelElement | undefined {
  for (let index = elements.length - 1; index >= 0; index--) {
    const candidate = elements[index]!;
    const bounds = elementBounds(candidate, elementTypes.get(candidate.type));
    if (at.x < bounds.x || at.x > bounds.x + bounds.width || at.y < bounds.y || at.y > bounds.y + bounds.height) {
      continue;
    }

    const allowed = candidate.id !== draggedId && !isInside(elements, parentPath, candidate.id, draggedId);
    return allowed ? candidate : undefined;
  }

  return undefined;
}

/** Whether `candidateId` sits inside the branch rooted at `rootId` - itself included. */
function isInside(elements: readonly DiagramModelElement[], parentPath: string, candidateId: string, rootId: string): boolean {
  const byId = new Map(elements.map((element) => [element.id, element]));
  const seen = new Set<string>(); // a defensive stop; a well-formed model never cycles
  let cursor: string | undefined = candidateId;
  while (cursor !== undefined && !seen.has(cursor)) {
    if (cursor === rootId) {
      return true;
    }

    seen.add(cursor);
    const element = byId.get(cursor);
    cursor = element === undefined ? undefined : (resolveOne({ path: parentPath }, { element, payload: element.payload }) ?? undefined);
  }

  return false;
}

/** The side of `box` facing `other`, at mid-height - how a branch leaves a node in a tree. */
function facingSide(box: ConnectorBox, other: ConnectorBox): Point {
  const centreX = box.x + box.width / 2;
  const otherCentreX = other.x + other.width / 2;
  return { x: otherCentreX >= centreX ? box.x + box.width : box.x, y: box.y + box.height / 2 };
}

/** A declared `data-*` map with no bindings in it - the drop preview belongs to no element. */
function resolvedStatic(data: Readonly<Record<string, unknown>> | undefined): Record<string, string> | undefined {
  if (data === undefined) {
    return undefined;
  }

  const out: Record<string, string> = {};
  for (const [name, value] of Object.entries(data)) {
    if (typeof value === "string") {
      out[name] = value;
    }
  }

  return out;
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
  onDoubleClick,
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
  onDoubleClick: (event: React.MouseEvent) => void;
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
  // A HELD offset is drawn but is no longer a drag - it must not keep the dragging look.
  const dragging = offset !== null && offset.held !== true;
  const groupState = { selected, dragging, connectTarget: connectHighlight === "valid" };
  const classes = [
    "library-element",
    selected ? "canvas-selected" : "",
    dragging ? "library-element-dragging" : "",
    connectHighlight === "valid" ? "library-connect-target" : "",
    connectHighlight === "invalid" ? "library-connect-forbidden" : "",
    // The declared half that belongs to the element rather than to its body.
    type !== undefined ? declaredClassNames(type, sourceOf(element, groupState), "element") : "",
  ]
    .filter(Boolean)
    .join(" ");

  const accessibility = type?.accessibility;
  const accessibleName = accessibility?.label !== undefined ? resolveOne(accessibility.label, sourceOf(element)) : null;

  return (
    <g
      className={classes}
      data-element-id={element.id}
      {...press}
      onContextMenu={onContextMenu}
      onDoubleClick={onDoubleClick}
      {...dataAttributesOf(declaredData(type, sourceOf(element, groupState, bounds)))}
      role={accessibility?.role}
      tabIndex={accessibility?.focusable === true ? 0 : undefined}
      aria-label={accessibleName ?? undefined}
    >
      <ShapeErrorBoundary bounds={bounds} label={element.label ?? element.id}>
        {renderShape(resizing, type, bounds, { selected, dragging, connectTarget: connectHighlight === "valid" })}
      </ShapeErrorBoundary>
      {/* THE TWO LOOKS, each the library's own ring just outside the element and each read from
          its own flag alone (centralized-selection Requirements 5.2, 6.1, 6.2). Outside, so no
          fill or inline stroke of the element can hide it; at different offsets, so an element
          that is both selected and a drop target shows both. ringLooks.ts paints them, inline. */}
      {selected && <OutlineRing className="library-selected-outline" style={SELECTED_RING_STYLE} bounds={bounds} offset={SELECTED_OUTLINE_OFFSET} />}
      {connectHighlight === "valid" && <OutlineRing className="library-accept-outline" style={ACCEPT_RING_STYLE} bounds={bounds} offset={ACCEPT_OUTLINE_OFFSET} />}
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

/** How far outside an element the selected ring sits, in canvas units. */
const SELECTED_OUTLINE_OFFSET = 4;

/** How far outside the accept ring sits: further than the selected one, so both can show. */
const ACCEPT_OUTLINE_OFFSET = 9;

/** A ring around an element's bounds, `offset` units clear of them on every side. */
function OutlineRing({
  className,
  style,
  bounds,
  offset,
}: {
  className: string;
  style: React.CSSProperties;
  bounds: { x: number; y: number; width: number; height: number };
  offset: number;
}) {
  return (
    <rect
      className={className}
      style={style}
      x={bounds.x - offset}
      y={bounds.y - offset}
      width={bounds.width + offset * 2}
      height={bounds.height + offset * 2}
      rx={offset}
    />
  );
}

function LibraryConnection({
  connection,
  relation,
  elementsById,
  elementTypes,
  attachmentPoint,
  adjustValue,
  dragValue,
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
  dragValue: GestureValue<ElementDragOffset>;
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
  // A CONNECTION FOLLOWS THE ELEMENT BEING DRAGGED, so the picture during the drag is the
  // picture after it. The element itself has always moved live; its connections stayed pinned
  // to where it used to be and jumped on release, which is the one moment a user cannot judge
  // the result they are choosing.
  //
  // The same seam as the adjust preview directly above, and the same cost: only a connection
  // with an end ON the dragged element subscribes to anything, and only that connection
  // re-renders per frame. A diagram of a thousand connections re-renders the two that moved.
  const liveDrag = useSyncExternalStore(dragValue.subscribe, () => {
    const value = dragValue.get();
    if (value === null) {
      return null;
    }
    return value.id === connection.sourceId || value.id === connection.targetId ? value : null;
  });

  if (relation === undefined) {
    return null; // an undeclared relation type has nothing to route; the validator rejects it upstream
  }

  const ends = connectionEnds(connection, elementsById, elementTypes, attachmentPoint, liveDrag);
  if (ends === null) {
    return null; // a dangling end is the module's model bug to notice; there is nothing to draw
  }

  const [from, to] = ends;
  const waypoints = liveAdjust !== null ? [liveAdjust.waypoint] : connection.waypoints ?? [];
  const source = draggedInto(elementsById.get(connection.sourceId), liveDrag);
  const target = draggedInto(elementsById.get(connection.targetId), liveDrag);
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
  if (type?.labels === undefined && type?.decorations === undefined && type?.tooltip === undefined) {
    return body;
  }

  const source = sourceOf(element, state, bounds);
  // The element's own title, which five renderers write by hand today. A <title> describes its
  // PARENT element, so it rides here beside the body rather than inside whichever shape drew.
  const tooltip = type?.tooltip ? resolveOne(type.tooltip, source) : null;

  return (
    <>
      {tooltip !== null ? <title>{tooltip}</title> : null}
      {body}
      {type?.decorations ? declaredDecorations(type, bounds, source) : null}
      {type?.labels ? declaredLabels(type, bounds, source) : null}
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
function declaredDecorations(type: ElementTypeDefinition, bounds: ConnectorBox, source: BindingSource): ReactNode {
  const resolved = resolveDecorations(type.decorations, source, bounds);
  return resolved.map((decoration) => (
    <g
      key={decoration.index}
      // THE GROUP CARRIES THE LIBRARY'S CLASS AND THE ORNAMENT CARRIES THE MODULE'S. A module
      // names the thing it drew - `.causal-loop-marker` is a path with a `d`, and its own test
      // asks that path for it - so putting the module's name on the container as well would
      // make `querySelector` find the wrapper first and answer null. The group keeps what makes
      // it a group: no pointer events, the aria state, and the arrowhead a line may carry.
      className="library-decoration"
      {...dataAttributesOf(decoration.data)}
      role={decoration.role}
      aria-label={decoration.accessibleName}
      style={{ pointerEvents: "none" }}
      // A decoration carrying a title or a name is describing something a reader may need - a
      // problem's message - so it stops being hidden from assistive technology at that point,
      // and only at that point.
      aria-hidden={decoration.tooltip === undefined && decoration.accessibleName === undefined ? true : undefined}
      markerEnd={markerRef(decoration.markerEnd ?? "none")}
    >
      {decoration.tooltip !== undefined ? <title>{decoration.tooltip}</title> : null}
      {decorationGlyph(decoration)}
      {decoration.text !== undefined ? (
        <text
          className={["library-decoration-text", decoration.className].filter(Boolean).join(" ")}
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

/**
 * One glyph of the closed set. A `marker` with no name draws nothing but its text.
 *
 * <b>The declared class rides the GLYPH as well as its group.</b> A selector for an ornament
 * should find the drawn thing rather than only its container - causal-loop's own test asks the
 * marker for its `d`, which is a question a `<g>` cannot answer. Both carry it, because the
 * group is also what a stylesheet reaches for when it wants the text with it.
 */
function decorationGlyph(decoration: ResolvedDecoration): ReactNode {
  const className = decoration.className;
  switch (decoration.glyph) {
    case "line":
      return <line className={className} x1={decoration.from.x} y1={decoration.from.y} x2={decoration.to.x} y2={decoration.to.y} />;
    case "path":
      return decoration.d !== undefined ? <path className={className} d={decoration.d} /> : null;
    case "circle":
      return <circle className={className} cx={decoration.from.x} cy={decoration.from.y} r={decoration.radius} />;
    case "rect":
      return (
        <rect
          className={className}
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
  viewScale: number,
): ReactNode {
  const resolved = resolveBackground(
    background,
    { element: { id: "__background__", type: "__background__", x: 0, y: 0 }, payload: model.background },
    extent,
    viewScale,
  );

  return (
    <>
      {resolved.rects.map((rect) => (
        <rect key={rect.key} className={rect.className} x={rect.x} y={rect.y} width={rect.width} height={rect.height} />
      ))}
      {resolved.lines.map((line) => (
        <line key={line.key} className={line.className} x1={line.x1} y1={line.y1} x2={line.x2} y2={line.y2} />
      ))}
      {resolved.marks.map((mark) => (
        <g key={mark.key} className={mark.className}>
          {mark.tooltip !== undefined ? <title>{mark.tooltip}</title> : null}
          {mark.circle ? <circle cx={mark.circle.cx} cy={mark.circle.cy} r={mark.circle.r} /> : null}
          {mark.line ? <line x1={mark.line.x1} y1={mark.line.y1} x2={mark.line.x2} y2={mark.line.y2} /> : null}
          {mark.text ? (
            <text
              className={mark.text.className}
              x={mark.text.x}
              y={mark.text.y}
              textAnchor={mark.text.anchor}
              style={{
                fontSize: mark.text.typography?.fontSize,
                fontWeight: mark.text.typography?.fontWeight,
                fontStyle: mark.text.typography?.fontStyle,
                fill: tokenColour(mark.text.typography?.color),
              }}
            >
              {mark.text.text}
            </text>
          ) : null}
        </g>
      ))}
      {resolved.texts.map((text) => (
        <text
          key={text.key}
          className={text.className}
          style={{
            fontSize: text.typography?.fontSize,
            fontWeight: text.typography?.fontWeight,
            fontStyle: text.typography?.fontStyle,
            fill: tokenColour(text.typography?.color),
          }}
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
function declaredLabels(type: ElementTypeDefinition, bounds: ConnectorBox, source: BindingSource): ReactNode {
  const lines = layoutLabels(type.labels, source, bounds);
  // A document that sets its elements' text colour sets it for their LINES, which are what a
  // reader sees. A label naming its own colour keeps it: the declaration is more specific than
  // the element's, so it wins.
  const inherited = resolveBound(type.boundStyle?.labelColor, source);
  return lines.map((line) => (
    <text
      key={`${line.declarationIndex}-${line.lineIndex}`}
      className={["library-element-label", line.className].filter(Boolean).join(" ")}
      x={line.x}
      y={line.y}
      style={{
        // THE ANCHOR TRAVELS WITH THE X IT WAS COMPUTED FOR, AS AN INLINE STYLE, ALWAYS. The
        // layout places `start` at the left inset, `middle` at the centre and `end` at the right
        // inset, so an anchor that differs from the layout's puts the text somewhere the layout
        // never meant. This used to be a presentation ATTRIBUTE, written only when it differed
        // from `middle`, on the belief that an attribute outranks a stylesheet - it is the other
        // way round: an attribute loses to ANY author rule, even a single class. So the library's
        // own `.library-element-label { text-anchor: middle }` centred every start- and end-aligned
        // line on its inset, and a module rule (databricks' `text-anchor: start`) re-anchored the
        // centred ones. Text ran out of its element on databricks, SHACL and azure-pipeline alike;
        // eight modules declare start or end. An inline style outranks every class rule, which is
        // what makes the layout's answer the one the browser draws.
        textAnchor: line.anchor,
        fontSize: line.typography?.fontSize,
        fontWeight: line.typography?.fontWeight,
        fontStyle: line.typography?.fontStyle,
        fill: tokenColour(line.typography?.color ?? inherited),
      }}
    >
      {line.tooltip ? <title>{line.tooltip}</title> : null}
      {line.text}
    </text>
  ));
}


/**
 * What a declaration resolves against, with the canvas's own state included.
 *
 * <b>The state half is what register entry G2 was about.</b> Twenty-three of the twenty-eight
 * sufficiency rows put a class on `selected`, `dragging` or `connectTarget`; before this the
 * conditions could see the model and nothing else, so a declared element could not look
 * selected. Built in one place so a label, a class and a decoration all answer alike - three
 * sources that could disagree is the drift this specification exists to remove.
 */
function sourceOf(element: DiagramModelElement, state?: CustomShapeState, bounds?: ConnectorBox): BindingSource {
  return {
    element,
    payload: element.payload,
    state: state === undefined ? {} : { selected: state.selected, dragging: state.dragging, connectTarget: state.connectTarget },
    // The LIVE rectangle, so a hint bound to `bounds.left` shows where a drag would land rather
    // than where the element was before it started.
    bounds:
      bounds === undefined
        ? undefined
        : {
            left: bounds.x,
            top: bounds.y,
            right: bounds.x + bounds.width,
            bottom: bounds.y + bounds.height,
            width: bounds.width,
            height: bounds.height,
            centreX: bounds.x + bounds.width / 2,
            centreY: bounds.y + bounds.height / 2,
          },
  };
}

/** The mark a `symbol` draws, from the document's own word for it. */
function symbolVariant(name: string | undefined): "circle" | "square" | "double-circle" {
  return name === "square" || name === "double-circle" ? name : "circle";
}

/** A resolved `data-*` map as React props: `{ testid: "x" }` becomes `data-testid="x"`. */
function dataAttributesOf(data: Readonly<Record<string, string>> | undefined): Record<string, string> {
  const props: Record<string, string> = {};
  for (const [name, value] of Object.entries(data ?? {})) {
    props[`data-${name}`] = value;
  }

  return props;
}

/** The `data-*` attributes a type declares for its elements, resolved against this one. */
function declaredData(type: ElementTypeDefinition | undefined, source: BindingSource): Readonly<Record<string, string>> | undefined {
  if (type?.data === undefined) {
    return undefined;
  }

  const out: Record<string, string> = {};
  for (const [name, value] of Object.entries(type.data)) {
    const resolved = typeof value === "string" ? value : resolveOne(value, source);
    if (resolved !== null) {
      out[name] = resolved;
    }
  }

  return out;
}

/** The classes a type declares for one target - the shape's body, or the element's group. */
function declaredClassNames(
  type: ElementTypeDefinition,
  source: BindingSource,
  on: "element" | "shape" | "shape-inner" = "shape",
): string {
  const classes: string[] = [];
  for (const declaration of type.classNames ?? []) {
    if ((declaration.on ?? "shape") !== on || !holds(declaration.when, source)) {
      continue;
    }

    // Stated outright, or bound - `helm-node-{payload.kind}` is a template, which is how a
    // kind-suffixed class arrives without the library knowing any module's kinds.
    const resolved = typeof declaration.className === "string" ? declaration.className : resolveOne(declaration.className, source);
    if (resolved !== null && resolved.length > 0) {
      classes.push(resolved);
    }
  }

  return classes.join(" ");
}

/** One bound paint value, or undefined - a theme token name, exactly as `style` takes. */
function resolveBound(binding: Binding | undefined, source: BindingSource): string | undefined {
  if (binding === undefined) {
    return undefined;
  }

  return resolveOne(binding, source) ?? undefined;
}

/**
 * The built-in a type draws for THIS element.
 *
 * A selection resolves through the model; an unlisted or unresolved value takes the declared
 * fallback, because an element always draws something. There is deliberately no way for a
 * selection to reach a custom renderer: that would be the escape hatch arriving as data, which
 * a guard reading the source could not see.
 */
function shapeOf(shape: BuiltInShape | ShapeSelection, source: BindingSource): BuiltInShape {
  if (typeof shape === "string") {
    return shape;
  }

  const value = resolveOne({ path: shape.path }, source);
  return (value !== null ? shape.cases[value] : undefined) ?? shape.fallback;
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

  if (isCustomShape(type.shape)) {
    return <>{type.shape.render(element, state) as ReactNode}</>;
  }

  const source = sourceOf(element, state, bounds);
  const style = { ...type.style, ...element.style };
  const bound = type.boundStyle;
  const paint = {
    // Bound paint wins over the type's tokens for the fields it names, because it is the
    // document speaking about this element rather than the declaration speaking about the type.
    fill: tokenColour(resolveBound(bound?.fill, source) ?? style.fill),
    stroke: tokenColour(resolveBound(bound?.stroke, source) ?? style.stroke),
    strokeWidth: style.strokeWidth,
    strokeDasharray: style.dash?.join(" "),
  };
  const shape = shapeOf(type.shape, source);
  // The class hook every one of the twenty-eight sufficiency rows needed: without it a migrated
  // element draws the right geometry in the library's colours instead of its own.
  const declared = declaredClassNames(type, source);
  const shapeClass = ["library-shape", declared].filter(Boolean).join(" ");

  switch (shape) {
    case "none":
      // No body at all: this element is its labels and its decorations. Rows 2, 8, 14 and 25.
      return null;
    case "moment": {
      // A DIAMOND rather than a circle, because that is what the notation that justified this
      // shape draws: `SpanElement` renders a moment as `M x-r y L x y-r L x+r y L x y+r Z`, and
      // a built-in whose only row draws something else is a built-in nobody can use. Caught by
      // reading the component the row's renderer wraps rather than by assuming a "point" is
      // round.
      const r = Math.max(2, Math.min(bounds.width, bounds.height) / 2);
      const d = `M ${element.x - r} ${element.y} L ${element.x} ${element.y - r} L ${element.x + r} ${element.y} L ${element.x} ${element.y + r} Z`;
      return <path className={shapeClass} d={d} style={paint} />;
    }
    case "double-ellipse":
      return (
        <g>
          <ellipse className={shapeClass} cx={element.x} cy={element.y} rx={bounds.width / 2} ry={bounds.height / 2} style={paint} />
          <ellipse
            className={["library-shape library-shape-inner", declaredClassNames(type, source, "shape-inner")].filter(Boolean).join(" ")}
            cx={element.x}
            cy={element.y}
            rx={Math.max(1, bounds.width / 2 - 4)}
            ry={Math.max(1, bounds.height / 2 - 4)}
            style={paint}
          />
          {label === "" ? null : centredText(element, label)}
        </g>
      );
    case "box":
      return <BoxElement x={bounds.x} y={bounds.y} width={bounds.width} height={bounds.height} rx={style.cornerRadius} label={label} boxClassName={shapeClass} style={paint} />;
    case "rounded-rectangle":
      return <BoxElement x={bounds.x} y={bounds.y} width={bounds.width} height={bounds.height} rx={style.cornerRadius ?? 8} label={label} boxClassName={shapeClass} style={paint} />;
    case "pill":
      return <BoxElement x={bounds.x} y={bounds.y} width={bounds.width} height={bounds.height} rx={bounds.height / 2} label={label} boxClassName={shapeClass} style={paint} />;
    case "centered-box":
      return <CenteredBoxElement className={declared || undefined} x={element.x} y={element.y} halfWidth={bounds.width / 2} halfHeight={bounds.height / 2} text={label} style={paint} />;
    case "ellipse":
      return <EllipseElement x={element.x} y={element.y} radiusX={bounds.width / 2} radiusY={bounds.height / 2} text={label} ellipseClassName={shapeClass} style={paint} />;
    case "frame":
      // FrameElement is centre-based like the other shared elements; the corner-based bounds
      // shifted every boundary by half its box (the third corner/centre instance, caught by
      // the c4 migration's frame test before one ever mounted).
      return <FrameElement className={["library-frame", declared].filter(Boolean).join(" ")} x={element.x} y={element.y} width={bounds.width} height={bounds.height} label={label} style={paint} />;
    case "span":
      return <SpanElement box={{ x: element.x, y: element.y, width: bounds.width, height: bounds.height }} label={label} classes={{ span: ["library-shape library-span", declared].filter(Boolean).join(" "), moment: "library-span-moment", label: "library-span-label", hint: "library-span-hint", adorner: "library-span-adorner", anchor: "library-span-anchor", anchorHit: "library-span-anchor-hit" }} style={paint} />;
    case "styled-box":
      return (
        <StyledBoxElement
          className={declared || undefined}
          x={element.x}
          y={element.y}
          width={bounds.width}
          height={bounds.height}
          shape={resolveBound(bound?.silhouette, source)}
          background={tokenColour(resolveBound(bound?.fill, source) ?? style.fill) ?? "var(--canvas-node-fill, #3b6ea5)"}
          color={tokenColour(resolveBound(bound?.labelColor, source) ?? style.labelTypography?.color) ?? "var(--canvas-node-label, #ffffff)"}
          name={label}
        />
      );
    case "symbol":
      return (
        <SymbolElement
          x={element.x}
          y={element.y}
          radius={Math.max(2, Math.min(bounds.width, bounds.height) / 2)}
          // Which mark, from the document: a Wardley anchor is a square and a submap a double
          // ring, and the notation says which per element. Same `silhouette` a styled box takes,
          // for the same reason - it is data about the element, not a property of its type.
          variant={symbolVariant(resolveBound(bound?.silhouette, source))}
          label={label}
          labelX={element.labelAt?.x ?? element.x + 12}
          labelY={element.labelAt?.y ?? element.y - 8}
          markClassName={shapeClass}
          outerClassName={declaredClassNames(type, source, "shape-inner") || undefined}
          style={paint}
        />
      );
    case "diamond":
      return polygonShape(bounds, label, paint, [[0.5, 0], [1, 0.5], [0.5, 1], [0, 0.5]], shapeClass);
    case "hexagon":
      return polygonShape(bounds, label, paint, [[0.25, 0], [0.75, 0], [1, 0.5], [0.75, 1], [0.25, 1], [0, 0.5]], shapeClass);
    case "parallelogram":
      return polygonShape(bounds, label, paint, [[0.2, 0], [1, 0], [0.8, 1], [0, 1]], shapeClass);
    case "cylinder":
      return (
        <g>
          <rect className={shapeClass} x={bounds.x} y={bounds.y + 6} width={bounds.width} height={bounds.height - 12} style={paint} />
          <ellipse className={shapeClass} cx={element.x} cy={bounds.y + 6} rx={bounds.width / 2} ry={6} style={paint} />
          <ellipse className={shapeClass} cx={element.x} cy={bounds.y + bounds.height - 6} rx={bounds.width / 2} ry={6} style={paint} />
          {centredText(element, label)}
        </g>
      );
  }
}

function polygonShape(
  bounds: ConnectorBox,
  label: string,
  paint: React.CSSProperties,
  corners: readonly (readonly [number, number])[],
  className = "library-shape",
): ReactNode {
  const points = corners.map(([fx, fy]) => `${bounds.x + fx * bounds.width},${bounds.y + fy * bounds.height}`).join(" ");
  return (
    <g>
      <polygon className={className} points={points} style={paint} />
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

/**
 * The element as the drag currently has it, or unchanged when this drag is not about it.
 *
 * One place, because BOTH ends need it and for different reasons: the moved end's own
 * attachment point, and the other end's `towards` - which aims at the moved element's centre,
 * so an end that stayed put still has to re-aim while its partner travels.
 */
function draggedInto(element: DiagramModelElement | undefined, drag: ElementDragOffset | null): DiagramModelElement | undefined {
  if (element === undefined || drag === null || drag.id !== element.id) {
    return element;
  }

  return { ...element, x: element.x + drag.dx, y: element.y + drag.dy };
}

function connectionEnds(
  connection: DiagramModelConnection,
  elementsById: Map<string, DiagramModelElement>,
  elementTypes: Map<string, ElementTypeDefinition>,
  attachmentPoint: (element: DiagramModelElement, anchor: string | undefined, towards: Point) => Point,
  drag: ElementDragOffset | null = null,
): [Point, Point] | null {
  const source = draggedInto(elementsById.get(connection.sourceId), drag);
  const target = draggedInto(elementsById.get(connection.targetId), drag);
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

/**
 * A value on the declared step, rounding halves AWAY FROM ZERO.
 *
 * `Math.round` is wrong here and wrong in only half the canvas, which is why this is spelled
 * out rather than borrowed: it sends -0.5 to -0, so a drag one half-step above the origin lands
 * a row low while the identical drag below the origin lands correctly. The two modules that
 * snap, both of their backends, and the binding vocabulary's `round: "nearest"` all already say
 * away-from-zero; this is the same rule, owned once.
 */
export function snapToStep(value: number, step: number | undefined): number {
  if (step === undefined || !(step > 0)) {
    return value;
  }

  const exact = value / step;
  const rounded = exact >= 0 ? Math.floor(exact + 0.5) : -Math.floor(-exact + 0.5);
  const snapped = rounded * step;
  // NEGATIVE ZERO IS A REAL VALUE HERE and it must not escape: a small upward drag rounds to
  // `-0`, which serialises into the document as "-0" and compares unequal to 0 under Object.is
  // - so a position that IS the origin reads as a change, and a test asserting 0 fails against
  // correct arithmetic. Found by the guard below doing exactly that.
  return snapped === 0 ? 0 : snapped;
}

/**
 * Where a drag would put an element: clamped to any declared bounds, then snapped to any
 * declared step.
 *
 * ONE FUNCTION BECAUSE THERE ARE TWO CALLERS AND THEY MUST AGREE - the per-frame preview and
 * the release. Snapping only the release would show the user one position and record another;
 * snapping only the preview would show a truth the drop then discards. The user asked for the
 * snap to be visible during the drag, which only works if both ends compute it the same way.
 *
 * Clamp first, then snap: a bound is a hard limit and a step is a preference, so a snap that
 * pushed an element back over a boundary would break the stronger of the two rules.
 */
function dragLanding(
  definition: { dragBounds?: import("./definition/diagramDefinition").ShapeBounds; snap?: import("./definition/diagramDefinition").SnapDeclaration },
  element: DiagramModelElement,
  dx: number,
  dy: number,
): Point {
  const clamped = clampToDragBounds(definition.dragBounds, element, dx, dy);
  return { x: clamped.x, y: snapToStep(clamped.y, definition.snap?.y?.step) };
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
