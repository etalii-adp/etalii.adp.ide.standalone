/**
 * The diagram definition: the one declarative place that states what a diagram type allows
 * (diagram-library Requirement 4). A module hands one of these to the library's canvas
 * together with a model and event handlers, and the canvas renders and operates from those
 * alone - which elements exist, how they look, what may connect to what, what the toolbox
 * offers, and which layout modes are on the table.
 *
 * Plain data, deliberately: no React and nothing the shell would have to serialize. The
 * definition is configuration a module STATES, read every render, so editing it at runtime
 * reconfigures the canvas without a remount (Requirement 1.3) - disabling dragging, swapping
 * endpoint rules or narrowing the layout modes are all edits to this object. The two escape
 * hatches - a custom shape and a custom route - carry module-supplied functions, because a
 * bespoke silhouette or path cannot be data; they never leave the client, and the validator
 * rejects a ref that names one without supplying it.
 */

/** A point in a shape's own coordinate space, or on the canvas - the context says which. */
export interface ShapePoint {
  x: number;
  y: number;
}

/** The rectangle a shape occupies, in canvas units. */
export interface ShapeBounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * The built-in silhouettes: the seven components that already exist under `canvas/elements/`
 * plus the six the requirements add (Requirement 2.1). A notation needing anything else
 * supplies a {@link CustomShapeRef} and remains a first-class citizen of hit-testing,
 * connecting and anchoring rather than a decorated box.
 */
export type BuiltInShape =
  | "box"
  | "centered-box"
  | "ellipse"
  | "frame"
  | "span"
  | "styled-box"
  | "symbol"
  | "rounded-rectangle"
  | "diamond"
  | "hexagon"
  | "pill"
  | "parallelogram"
  | "cylinder";

/** Every built-in shape, enumerable - what a guard walks and a toolbox derives icons from. */
export const BUILT_IN_SHAPES: readonly BuiltInShape[] = [
  "box",
  "centered-box",
  "ellipse",
  "frame",
  "span",
  "styled-box",
  "symbol",
  "rounded-rectangle",
  "diamond",
  "hexagon",
  "pill",
  "parallelogram",
  "cylinder",
];

/**
 * A module-provided shape. It conforms to the element contract - bounds come from the model
 * or sizing rule as for any element, and the ref supplies what the library cannot know: how
 * to draw it, where its edge is for a connector to land, and which anchors it declares.
 */
/** What the canvas tells a custom renderer about the moment it draws in. */
export interface CustomShapeState {
  selected: boolean;
  /** A drag preview is in flight: the element handed over already sits at the dragged spot. */
  dragging: boolean;
  connectTarget: boolean;
}

export interface CustomShapeRef {
  /** Names the shape, so styles, toolboxes and test assertions can refer to it. */
  customShape: string;
  /**
   * Draws the element. Typed opaquely on purpose: this layer knows no React, and
   * `DiagramCanvas` narrows the renderer to its component signature when mounting.
   */
  render: (element: unknown, state?: CustomShapeState) => unknown;
  /** Where a connector approaching from `towards` touches this shape's edge (Requirement 2.1). */
  edgePoint: (bounds: ShapeBounds, towards: ShapePoint) => ShapePoint;
  /** The shape's own anchors, in its local space; omitted means edge attachment. */
  anchors?: AnchorSet;
}

/**
 * Where connectors may attach to an element type (Requirement 2.4). A type may declare none -
 * `edge` - in which case connectors attach by edge intersection exactly as today's canvases do.
 */
export type AnchorSet =
  | { kind: "edge" }
  | { kind: "compass"; positions: readonly CompassPosition[] }
  | { kind: "sides"; fractions: readonly SideFraction[] }
  | { kind: "points"; points: readonly NamedAnchorPoint[] };

export type CompassPosition = "n" | "ne" | "e" | "se" | "s" | "sw" | "w" | "nw";

/** An anchor a fraction of the way along one side - `{ side: "top", at: 0.25 }`. */
export interface SideFraction {
  side: "top" | "right" | "bottom" | "left";
  /** 0..1 along the side, clockwise. */
  at: number;
  /** Named so an endpoint constraint can allow a subset (Requirement 4.2). */
  name?: string;
}

/** An explicit anchor point in the shape's own space. */
export interface NamedAnchorPoint {
  x: number;
  y: number;
  name?: string;
}

/**
 * Colours name theme tokens, never literals, so the existing light/dark guarantee holds
 * without per-module work (Requirements 2.2, 3.2).
 */
export interface ElementStyle {
  /** A theme token name for the fill. */
  fill?: string;
  /** A theme token name for the stroke. */
  stroke?: string;
  strokeWidth?: number;
  dash?: readonly number[];
  cornerRadius?: number;
  labelTypography?: LabelTypography;
}

export interface LabelTypography {
  fontSize?: number;
  fontWeight?: "normal" | "bold";
  /** A theme token name. */
  color?: string;
}

/** How an element's label sits, wraps and edits (Requirement 2.3). */
export interface LabelRule {
  placement: "inside" | "above" | "below" | "beside" | "inset";
  /**
   * The inset placement's own offsets, for a composite card whose first line is the name: the
   * line's top offset from the box's top, the line's height, and the horizontal inset on both
   * sides. The c4 card is the shape this exists for; an editor covering the whole card would
   * sit over three lines of text to edit one of them.
   */
  insetTop?: number;
  insetHeight?: number;
  insetX?: number;
  wrap?: boolean;
  truncate?: boolean;
  /**
   * Editable means: through the shared `InlineLabelEditor`, committing as an event, and
   * nothing else. A label with no single authored value beneath it is never marked editable
   * (Requirement 6.2).
   */
  editable?: boolean;
}

/** Where a connection's label sits along its route (Requirement 3.3). */
export interface RouteLabelRule {
  placement: "source" | "midpoint" | "target";
  offset?: number;
  editable?: boolean;
}

/** Where an element type's size comes from (Requirement 2.5). */
export type SizingRule = "model" | "content" | "user";

/** One kind of thing a diagram draws boxes for. */
export interface ElementTypeDefinition {
  id: string;
  shape: BuiltInShape | CustomShapeRef;
  style?: ElementStyle;
  label?: LabelRule;
  anchors: AnchorSet;
  sizing: SizingRule;
  /** Overrides the canvas-wide {@link DraggingPolicy} for this type (Requirement 5.2). */
  draggable?: boolean;
  /** Whether delete gestures reach this type at all (Requirement 5.3). */
  deletable?: boolean;
}

/**
 * The built-in routes (Requirement 3.1). These absorb the four existing connector families:
 * `straight` covers the straight components, `cubic-bezier` covers bezier and fixed-bezier,
 * the interactive family is `cubic-bezier` with `adjustable: true` on its relation type, and
 * `arc` covers causal-loop's chord-bowed links. A chordless path - causal-loop's self-loop -
 * is a {@link CustomRouteRef}, admitted first-class rather than forced into a built-in.
 */
export type BuiltInRoute =
  | "straight"
  | "polyline"
  | "orthogonal"
  | "arc"
  | "quadratic-bezier"
  | "cubic-bezier"
  | "spline";

/** Every built-in route, enumerable - what the route guard walks (Requirement 10.1). */
export const BUILT_IN_ROUTES: readonly BuiltInRoute[] = [
  "straight",
  "polyline",
  "orthogonal",
  "arc",
  "quadratic-bezier",
  "cubic-bezier",
  "spline",
];

export type RouteKind = BuiltInRoute | CustomRouteRef;

/** A module-supplied path builder, for geometry no built-in draws. */
export interface CustomRouteRef {
  /** Names the route, so definitions and test assertions can refer to it. */
  customRoute: string;
  /**
   * Builds the SVG path between the resolved endpoints. The same path serves rendering, the
   * connect-gesture preview and hit-testing, so the three cannot disagree (Requirement 3.4).
   */
  path: (from: ShapePoint, to: ShapePoint, waypoints: readonly ShapePoint[]) => string;
}

/** Start and end decorations (Requirement 3.2). */
export type MarkerKind = "none" | "arrow" | "open-arrow" | "diamond" | "circle" | CustomMarkerRef;

/** A module-supplied marker: an SVG path in a 10x10 viewBox, as the existing arrowheads use. */
export interface CustomMarkerRef {
  customMarker: string;
  path: string;
}

export interface ConnectionStyle {
  /** A theme token name. */
  stroke?: string;
  strokeWidth?: number;
  dash?: readonly number[];
  startMarker?: MarkerKind;
  endMarker?: MarkerKind;
  /** Corner rounding where the route is `orthogonal`. */
  cornerRadius?: number;
}

/**
 * Which element types one end of a relation may attach to, and on which anchors
 * (Requirement 4.2). This is the enforced part: a connect gesture only completes where a
 * constraint allows it, and the refusal happens at gesture time (Requirement 4.3).
 */
export interface EndpointConstraint {
  /** Element type ids this end may attach to. */
  elementTypes: readonly string[];
  /**
   * `"any"` accepts every anchor the type declares, `"edge"` attaches by edge intersection,
   * and a list names an allowed subset of anchor names. Omitted means `"any"`.
   */
  anchors?: "any" | "edge" | readonly string[];
}

/** The counts a notation caps, where it caps them. */
export interface Cardinality {
  /** At most this many of this relation leaving one source element. */
  maxFromSource?: number;
  /** At most this many of this relation arriving at one target element. */
  maxIntoTarget?: number;
}

/** One kind of line a diagram draws. */
export interface RelationTypeDefinition {
  id: string;
  route: RouteKind;
  style?: ConnectionStyle;
  label?: RouteLabelRule;
  endpoints: {
    source: EndpointConstraint;
    target: EndpointConstraint;
    allowSelf: boolean;
    cardinality?: Cardinality;
  };
  /** Whether waypoints or control points may be dragged (Requirement 3.5). */
  adjustable?: boolean;
  /**
   * Extra class names for the pieces, so a module's stylesheet keeps dressing what it always
   * dressed: the connection group, its visible line, and its fat hit twin.
   */
  className?: string;
  lineClassName?: string;
  hitClassName?: string;
  /**
   * What a connect gesture released over empty canvas means. The default, `ignore`, is the
   * library's enforcement rule: nothing was allowed there, nothing is raised. `complete`
   * declares the release itself meaningful - the timeline's create-and-relate gesture - and
   * raises `connection-released-on-empty` with the drop point, for the module to answer.
   * Recorded as a schema extension in the tasks document per Requirement 9.4.
   */
  emptyRelease?: "ignore" | "complete";
}

/**
 * The toolbox derives from the element types by default - icon, title, drag payload - and a
 * definition may suppress or add items explicitly (Requirement 4.4). The declared toolbox
 * feeds the existing `useRegisterDiagramToolbox` seam unchanged.
 */
export interface ToolboxDefinition {
  /** Element type ids that contribute no toolbox item, though the type itself stays legal. */
  suppress?: readonly string[];
  /** Items added beside the derived ones. */
  add?: readonly ToolboxItemDefinition[];
}

export interface ToolboxItemDefinition {
  id: string;
  title: string;
  icon?: string;
  /** What the drop carries, translated into an add event's element type. */
  payload: string;
}

/** The layout modes a definition may allow (Requirement 8.1). */
export type LayoutMode = "manual" | "horizontal-flow" | "vertical-flow" | "tree" | "layered-graph";

export interface LayoutDefinition {
  /**
   * The allowed modes; the first is the default. A definition allowing exactly one presents
   * no switching surface at all (Requirement 8.2), and one allowing zero is rejected by the
   * validator - a canvas that can lay out no way is a definition bug, not a runtime state.
   */
  modes: readonly LayoutMode[];
  /**
   * What a drag means while an automatic mode is active and dragging is still permitted
   * (Requirement 8.4): repin the element to manual placement, or displace it temporarily
   * until the next layout pass reclaims it.
   */
  dragUnderAutomaticLayout?: "repin-to-manual" | "reclaimed-displacement";
  /** The direction a `tree` mode grows in, where that mode is allowed. */
  treeDirection?: "left-to-right" | "right-to-left" | "top-down" | "bottom-up";
}

/** Whether pointer drags move elements at all, before per-type overrides (Requirement 5.2). */
export type DraggingPolicy = "enabled" | "disabled";

/**
 * A background the canvas draws behind every element - an axis, a grid, a labelled space
 * (the mechanism wardley's evolution axis and sparql's regions use; Requirement 9.2). The
 * renderer is handed the current view in canvas units and draws in them, so panning and
 * zooming carry the background with the elements.
 */
export interface DiagramBackgroundRef {
  background: string;
  render: (view: ShapeBounds) => unknown;
}

/** The whole statement of what a diagram type allows (Requirement 4.1). */
export interface DiagramDefinition {
  elementTypes: readonly ElementTypeDefinition[];
  relationTypes: readonly RelationTypeDefinition[];
  toolbox?: ToolboxDefinition;
  layout: LayoutDefinition;
  dragging: DraggingPolicy;
  /**
   * The intrinsic space, where the notation defines one - wardley's 0..1 by 0..1. Fit shows
   * exactly this extent rather than a box derived from the elements, because the space is
   * definitional, not derived (Requirement 9.2).
   */
  extent?: ShapeBounds;
  background?: DiagramBackgroundRef;
  /**
   * A hard edge for element drags, where the notation's space has one: the dragged element's
   * CENTRE is clamped inside this box, in the preview under the pointer and in the raised
   * `element-moved` position alike - a wardley component must not be draggable off the map
   * while the pointer is still down. Omitted, drags roam free.
   */
  dragBounds?: ShapeBounds;
}
