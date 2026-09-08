import type { Binding, Condition } from "./binding";
import type { BackgroundDeclaration } from "./background";
import type { ActionDeclaration, DeclaredFlag } from "./actions";

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
export type AnchorPositions =
  | { kind: "edge" }
  | { kind: "compass"; positions: readonly CompassPosition[] }
  | { kind: "sides"; fractions: readonly SideFraction[] }
  | { kind: "points"; points: readonly NamedAnchorPoint[] };

/**
 * Whether an element's anchors are shown and usable - <b>the half of the anchor declaration
 * that was missing</b>.
 *
 * Requirement 2.4 asks the declaration to say where anchors are and <em>whether they are
 * visible or enabled</em>, and never how they look. `AnchorSet` said only where. And the tree
 * measured: <b>all twenty-nine anchor declarations are `{ kind: "edge" }`</b>, so the positional
 * half has never once been needed, while the half modules actually want had nowhere to go.
 *
 * Bindings, on the same {@link DeclaredFlag} mechanism actions use, because "connectable only
 * when the model says so" is the same question as "enabled only when the model says so" and two
 * mechanisms for it would be one too many.
 */
export interface AnchorEnablement {
  visible?: DeclaredFlag;
  enabled?: DeclaredFlag;
}

export type AnchorSet = AnchorPositions & AnchorEnablement;

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
  /**
   * The fourth of the four the user's guidance named - size, weight, slant, colour - and the
   * only one this did not already carry. Added by the declarative-modules work rather than
   * speculatively: `sparql`'s annotation and `rdf`'s datatype line are both drawn italic today
   * by a class the renderer applies by hand.
   */
  fontStyle?: "normal" | "italic";
  /** A theme token name. */
  color?: string;
}


/**
 * A number in a declaration: written outright, or bound to a model field.
 *
 * Both, rather than only a binding, because most geometry is a constant the author knows -
 * a stub's length, a badge's radius - and forcing those through the model would push authored
 * layout into the backend for no gain.
 */
export type DeclaredNumber = number | Binding;

/**
 * The closed glyph set a decoration draws.
 *
 * <b>Closed on purpose.</b> A decoration vocabulary that could express anything would be a
 * renderer with extra steps, which is the thing this specification exists to remove. Five
 * entries, and each earns its place from a renderer that draws it today.
 */
export type DecorationGlyph = "line" | "path" | "circle" | "rect" | "marker";

/**
 * An ornament attached to an element - drawn relative to it, and nothing more than drawn.
 *
 * <b>No hit-testing, no gesture, no anchor.</b> Anything that needs those is an element type,
 * and that line is what keeps this a small closed set rather than a second element system.
 *
 * The four shapes it must express are measured, not imagined - the five renderers that use no
 * shared component today draw exactly these: a STUB (a line to nowhere with a label, in
 * ansible and helm), a BADGE (a curved marker with a caption, in causal-loop), an ANNOTATION
 * (bare positioned text, in sparql), and a TARGET (a circle with a label, in wardley).
 */
export interface DecorationDeclaration {
  glyph: DecorationGlyph;
  /** Where it starts, relative to the element's centre. */
  from?: { x: DeclaredNumber; y: DeclaredNumber };
  /** Where a `line` ends, relative to the element's centre. */
  to?: { x: DeclaredNumber; y: DeclaredNumber };
  /** A `circle`'s radius, or a `rect`'s corner rounding. */
  radius?: DeclaredNumber;
  width?: DeclaredNumber;
  height?: DeclaredNumber;
  /**
   * A `path`'s `d`, as data.
   *
   * <b>This is the one place a decoration can carry an arbitrary shape, and it is deliberately
   * a BINDING rather than a function.</b> causal-loop's polarity arc is computed today by
   * `loopMarkerPath(x, y, r, clockwise)` in the renderer; as a binding, the same path arrives
   * as a string the module's own backend or model produces. That keeps it data - checkable,
   * serialisable, and unable to call anything - which is the whole distinction this
   * specification rests on. Whether causal-loop's arc is better as a bound `d` or as a named
   * marker is a question for the sufficiency table rather than one to settle here.
   */
  d?: Binding;
  /** A `marker`'s name, from the library's own registry - an arrowhead, a tick. */
  marker?: string;
  /** Optional text drawn with the glyph: the stub's label, the badge's caption. */
  text?: Binding;
  /** Where that text sits, relative to the element's centre. */
  textAt?: { x: DeclaredNumber; y: DeclaredNumber };
  textAnchor?: "start" | "middle" | "end";
  typography?: LabelTypography;
  /** Classes for the glyph, so colour stays in the stylesheet. */
  className?: string;
  /** Drawn only when this holds - a badge that appears for one polarity and not the other. */
  when?: Condition;
}

/** Where a label sits relative to its element's shape. Carried over from `LabelRule`. */
export type LabelPlacement = "inside" | "above" | "below" | "beside" | "inset";

/**
 * A named vertical position inside the box, for the stacked-line case.
 *
 * Three names rather than free coordinates because a card's lines should line up between
 * modules; `offset` is there for the notation that genuinely needs its own geometry.
 */
export type LabelSlot = "header" | "body" | "footer";

/** How successive lines of a collection binding stack. */
export interface LabelStack {
  /** Distance between consecutive lines, replacing the hand-written `(index + 1) * ROW_HEIGHT`. */
  lineHeight: number;
  /** Where the first line sits relative to the slot's own baseline. */
  start?: number;
}

/** One declared label. A single-label module writes one of these, which is why migration is mechanical. */
export interface LabelDeclaration {
  /** What it says: a field, a template, or a collection with `each`. */
  text: Binding;
  /** Relative to the shape. Defaults to `inside`, which is what a single centred label is. */
  placement?: LabelPlacement;
  /** A named line inside the box. Ignored when `offset` is given. */
  slot?: LabelSlot;
  /** Explicit position relative to the element's centre, for a notation with its own geometry. */
  offset?: { x: number; y: number };
  /** For a collection binding: how its lines stack. Ignored for a single-value binding. */
  stack?: LabelStack;
  typography?: LabelTypography;
  /**
   * Editable means through the shared inline editor, committing as an event - exactly what
   * `LabelRule.editable` means today. A label with no single authored value beneath it is never
   * marked editable, and a collection line cannot be: there is no one field to write back to.
   */
  editable?: boolean;
  /** Trim to the box with an ellipsis, replacing the `fit(text, width)` card renderers call by hand. */
  truncate?: boolean;
  /** Conditional presence, replacing `badges.length > 0 ? … : null`. */
  when?: Condition;
  /** The `<title>` every card renderer writes by hand. */
  tooltip?: Binding;
  /** Extra classes for the drawn text, so colour stays in the stylesheet where a token is not enough. */
  className?: string;
}

/**
 * How an element's label sits, wraps and edits (Requirement 2.3).
 *
 * <b>Superseded by {@link ElementTypeDefinition.labels}</b>, which expresses as many lines as an
 * element draws rather than exactly one. This stays until every module has migrated, because
 * the library lands before any module is touched and thirteen modules still declare it; a
 * single-label module's migration is one `LabelRule` becoming one entry in `labels`. When a
 * type declares both, `labels` wins and this is ignored - the two never compose, because a
 * reader should never have to work out which line came from which mechanism.
 */
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
  /** @deprecated Superseded by {@link labels}; kept until every module has migrated. */
  label?: LabelRule;
  /**
   * Every line this element draws, declared - the addition that lets a module stop hand-rolling
   * `<text>`. A single-label type writes one entry; `owl-card`'s three-part shape writes three,
   * one of them bound to a model collection. Present, this replaces {@link label} entirely.
   */
  labels?: readonly LabelDeclaration[];
  /**
   * Ornaments this type draws beside its shape - the stubs, badges, annotations and targets
   * that five renderers draw with no shared component at all. Drawn, never interactive.
   */
  decorations?: readonly DecorationDeclaration[];
  /** Actions this type offers, beyond the ones the whole diagram declares. */
  actions?: readonly ActionDeclaration[];
  anchors: AnchorSet;
  sizing: SizingRule;
  /** Overrides the canvas-wide {@link DraggingPolicy} for this type (Requirement 5.2). */
  draggable?: boolean;
  /** Whether delete gestures reach this type at all (Requirement 5.3). */
  deletable?: boolean;
  /**
   * Paints this type's elements beneath the connections: an opaque container whose members'
   * edges must stay visible over it - azure-pipeline's stage cards. Off, connections draw
   * under every element as they always have.
   */
  beneathConnections?: boolean;
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

/** The two end boxes a drawn connection runs between - for a route that anchors on boxes. */
export interface RouteEnds {
  source: ShapeBounds;
  target: ShapeBounds;
}

/** A module-supplied path builder, for geometry no built-in draws. */
export interface CustomRouteRef {
  /** Names the route, so definitions and test assertions can refer to it. */
  customRoute: string;
  /**
   * Builds the SVG path between the resolved endpoints. The same path serves rendering, the
   * connect-gesture preview and hit-testing, so the three cannot disagree (Requirement 3.4).
   * A drawn connection also receives its endpoint BOUNDS - causal-loop's arc picks its own
   * anchors from the boxes and bows to the side of travel, which points alone cannot say;
   * the connect preview has no target box yet and passes none.
   */
  path: (from: ShapePoint, to: ShapePoint, waypoints: readonly ShapePoint[], ends?: RouteEnds) => string;
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
   * Adornment drawn inside the connection's group, after its line - polarity signs, delay
   * strokes, anything the notation rides on a line - so the shared `.canvas-selected` cascade
   * colours it with the line it describes. Handed the resolved route so it can place itself
   * along the real geometry.
   */
  adorn?: (route: { from: ShapePoint; to: ShapePoint; waypoints: readonly ShapePoint[]; ends?: RouteEnds }, connection: unknown) => unknown;
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

/**
 * <b>A third function-valued escape hatch, and one Requirement 2.9 does not name.</b>
 *
 * Requirement 2.9 originally listed `CustomShapeRef` and `CustomRouteRef`; it now states the
 * property instead - no function-valued escape hatch reachable from a module client - because a
 * list of two admits a third silently, which is this specification's own subject appearing
 * inside its own requirements.
 *
 * `DiagramBackgroundRef` is that third: a module-supplied `render` the library calls, for the
 * backdrop. <b>It has exactly one user - `wardley-map`</b>. I first reported two; sparql
 * declares no background at all, and its `sparql-region` is a custom SHAPE drawn as an element
 * rather than a backdrop. So this dies with wardley's migration at task 14, not with a second
 * module's. It stays reachable until then because removing a contract an unmigrated module
 * still uses would break it; the declared form beside it is what replaces it.
 */
export type DiagramBackground = DiagramBackgroundRef | BackgroundDeclaration;

/** Whether a background is the old callable form rather than the declared one. */
export function isBackgroundRef(background: DiagramBackground): background is DiagramBackgroundRef {
  return "render" in background;
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
  background?: DiagramBackground;
  /**
   * Every action this diagram type offers, with what invokes each and whether it is enabled.
   *
   * The library derives its shortcut key set from these and dispatches an action id, so a
   * module never writes a key list and never manufactures a key event to name an action.
   */
  actions?: readonly ActionDeclaration[];
  /**
   * Draw a relation by dragging with the RIGHT button from an element's body to another - the
   * gesture a causal loop diagram links with, where the arrows are the whole point and reaching
   * for a small anchor handle would be in the way. Left-button anchor drags still connect where a
   * module renders anchors; this only adds the right-button-from-anywhere path, and only where a
   * module asks for it. A right press that does not move stays the context menu.
   */
  connectOnRightDrag?: boolean;
  /**
   * A hard edge for element drags, where the notation's space has one: the dragged element's
   * CENTRE is clamped inside this box, in the preview under the pointer and in the raised
   * `element-moved` position alike - a wardley component must not be draggable off the map
   * while the pointer is still down. Omitted, drags roam free.
   */
  dragBounds?: ShapeBounds;
}
