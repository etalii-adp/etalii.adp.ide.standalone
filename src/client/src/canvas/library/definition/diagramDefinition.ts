import type { Binding, BindingPath, Condition } from "./binding";
import type { BackgroundDeclaration } from "./background";
import type { ActionDeclaration, DeclaredFlag } from "./actions";
import type { ChromeDeclaration } from "./chrome";

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
  /**
   * <b>Draws no body at all</b>, so an element that is only its labels and decorations - a
   * connector stub, a loop badge, a bare annotation - can still be an element type.
   *
   * Sufficiency rows 2, 8, 14 and 25 justify it. Before this, `shape` was required and every
   * built-in drew something, so those four had no expression that was not a custom renderer.
   */
  | "none"
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
  | "cylinder"
  /**
   * A point in time rather than a period: the span's dot form (row 26). A different drawing
   * rather than a class on the same one, which is why it is a shape and not a style.
   */
  | "moment"
  /**
   * A squircle - |x/a|^4 + |y/b|^4 = 1 - which reads as a box with the corners taken off rather
   * than as a rounded rectangle, and so distinguishes a kind of element at a glance.
   */
  | "superellipse"
  /** Narrower at the bottom than the top, for an element that reads as narrowing down. */
  | "trapezoid"
  /**
   * A rectangle closed on the right by a semicircle, so the shape points the way its relations
   * run. Its outline, its text region and its edge point all come from
   * {@link outlineOf} rather than from three separate ideas of where it is.
   */
  | "diode"
  /** A ring inside the ellipse - owl's `doubled` and wardley's submap mark (rows 18, 27, 28). */
  | "double-ellipse"
  /**
   * A banner closed on the right by a point: `(0,0) (w - p, 0) (w, h/2) (w - p, h) (0, h)` with
   * point depth `p = min(h/2, w/2)`. It reads as something moving forward in time, and it is the
   * one shape that can be cut into {@link SegmentDeclaration segments}, each ending in a chevron.
   */
  | "arrow-banner";

/**
 * Every built-in shape, enumerable - <b>what the conformance guard walks</b>.
 *
 * The guard is `declarativeModules.test.ts`, and it reads this list to check that every shape a
 * module NAMES is one the library has: `shape: "rounded-rect"` type-checks nowhere, reads as an
 * ordinary string in a `.tsx` file, and at runtime falls through the switch and draws nothing.
 *
 * <b>This comment used to describe two consumers and have none.</b> It claimed a guard walked
 * this list and a toolbox derived icons from it; the constant had exactly one reference, its own
 * definition. The guard now exists, so that half is true. The toolbox half is gone rather than
 * rewritten - there is no toolbox that derives anything from this, and a comment describing a
 * second imagined consumer is how the first one came to be believed.
 */
export const BUILT_IN_SHAPES: readonly BuiltInShape[] = [
  "none",
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
  "superellipse",
  "trapezoid",
  "diode",
  "cylinder",
  "moment",
  "double-ellipse",
  "arrow-banner",
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
  | { kind: "points"; points: readonly NamedAnchorPoint[] }
  | AlongAnchors;

/** One side of an element's bounds. */
export type EdgeName = "top" | "bottom" | "left" | "right";

/**
 * Attachment ANYWHERE along the named edges, rather than at a fixed set of points.
 *
 * A connection drawn to such an element records where it landed as an {@link EdgeAttachment} - an
 * edge, optionally a region of it, and a fraction along that - and its end is recomputed from the
 * element's current bounds on every render, so a resize moves it proportionally instead of leaving
 * it behind. `regions: "segments"` cuts the top and bottom edges into the stretches the element's
 * {@link SegmentDeclaration segments} occupy, so an attachment belongs to one segment and moves
 * with that segment's boundaries.
 */
export interface AlongAnchors {
  kind: "along";
  edges: readonly EdgeName[];
  regions?: "segments";
}

/**
 * Where on an element a connection end sits, for an element declaring {@link AlongAnchors}.
 *
 * `at` is a fraction 0..1 of the region's length - of the whole edge when there is no region -
 * measured left to right on the top and bottom edges and top to bottom on the sides. A fraction
 * rather than an offset, because an offset stays where it was when the element grows.
 */
export interface EdgeAttachment {
  edge: EdgeName;
  /** The segment index whose stretch of the edge this is, for `regions: "segments"`. */
  region?: number;
  at: number;
}

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
  /**
   * `false` draws no anchor dot, at rest or on hover. The anchors still take a connection, and a
   * connect gesture still highlights the target and the stretch of edge under the pointer, so the
   * gesture is never blind - only the resting clutter goes.
   */
  visible?: DeclaredFlag;
  enabled?: DeclaredFlag;
  /**
   * Which sides a connector may reach when it attaches by edge rather than to a named anchor.
   *
   * <b>Register entry G20, found by the reference migration rather than by the table</b> - which
   * is the migration doing its job. Three canvases do not want a true edge intersection: they
   * attach on the LEFT or RIGHT side facing the other end, whatever the angle, because the
   * notation reads left-to-right and a connector leaving through the top of a box reads as a
   * different relation. Each spells that in a custom shape's `edgePoint` today, and it is one
   * of the two reasons those shapes exist at all.
   *
   * It lives beside `visible` and `enabled` rather than inside `{ kind: "edge" }` because it
   * governs the FALLBACK, which a type declaring named anchors still uses for the ends that do
   * not name one - dependency-graph declares two side anchors and attaches its targets by edge.
   *
   * Unset means a true edge intersection: what every other module wants, and what the canvas
   * has always done.
   */
  edgeSides?: "all" | "horizontal" | "vertical";
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
  /**
   * Holds a readable size as the diagram is zoomed, between these bounds.
   *
   * Sufficiency row 27: a Wardley map's stage and axis labels stay readable as the map zooms
   * while the boundaries they name do not, because a position is only meaningful against its
   * own axes. The module computes that today from the view width; declared, it is the library
   * that knows the view, which is the right place for it.
   */
  scaleWithView?: { min: number; max: number };
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
  /**
   * What the coordinates below are measured from. Defaults to the element's CENTRE.
   *
   * <b>Found in a browser, not by a test.</b> A fixed ornament - a stub's line, a badge two
   * pixels above the box - is naturally written as an offset from the centre, and that is what
   * every one of these meant when they were only ever fixed numbers. A BOUND coordinate is not:
   * `{ path: "bounds.right" }` resolves to a canvas position, and adding the centre to it puts
   * the ornament twice as far out as it belongs. Azure-pipeline's problem mark sat a whole card
   * away from its card, and every module test passed, because a test that reads the glyph's
   * class and text never asks where it is.
   *
   * `canvas` says the numbers are already absolute, which is what reading `bounds` gives.
   */
  anchor?: "centre" | "canvas";
  /**
   * A collection path: one decoration per entry, its own paths rooted at the ITEM, laid out
   * along {@link step} from {@link from}.
   *
   * <b>Register entry G27, and four rows need it</b> - azure-pipeline's status indicators on a
   * stage and on a job, mindmap's notes/link/folded glyphs, wardley's decorator badges. Every
   * one of them draws a VARIABLE NUMBER of small marks in a row, and a fixed list of
   * declarations cannot say "one per thing the model has". It is the same `each` labels and
   * background already take, for the same reason.
   */
  each?: Binding;
  /**
   * How far apart successive entries sit. Only meaningful with {@link each}.
   *
   * A step rather than a per-item position, because the model does not know where its
   * indicators go - it knows how many there are, and the notation decides the spacing.
   */
  step?: { x: number; y: number };
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
  /**
   * Classes for the glyph, so colour stays in the stylesheet.
   *
   * <b>Bindable since the sufficiency table</b> (register entry G6): a problem mark is
   * `pipeline-problem-mark-{payload.problem.severity}`, and a severity the library does not
   * know cannot be spelled as a fixed string. The library still says nothing about what a
   * problem IS - it draws where the module's model says one is, and colours it by a field.
   */
  className?: string | Binding;
  /**
   * An arrowhead at the end of a `line` or `path`.
   *
   * Sufficiency row 8: causal-loop's polarity arc is an arc WITH an arrowhead, and the sweep
   * without the head reads as a stray curve rather than as a direction of travel.
   */
  markerEnd?: MarkerKind;
  /** The `<title>` a decoration carries - a problem mark's message, an annotation's text. */
  tooltip?: Binding;
  /** `data-*` attributes this ornament carries. See {@link ElementTypeDefinition.data}. */
  data?: DataAttributes;
  /**
   * How this ornament reaches a screen reader.
   *
   * A problem mark is `role="img"` with the problem's message as its name: a glyph that means
   * something to a reader who can see it must mean the same to one who cannot. The element's
   * own {@link ElementTypeDefinition.accessibility} says nothing about its ornaments.
   */
  accessibility?: { role?: string; label?: Binding };
  /** Drawn only when this holds - a badge that appears for one polarity and not the other. */
  when?: Condition;
}

/**
 * Where a label sits relative to its element's shape. Carried over from `LabelRule`.
 *
 * `before` is the mirror of `beside`: the text ENDS 8 units left of the element's left edge,
 * vertically centred and end-anchored, so a row of elements starting at different places still
 * reads as a column of names each hugging its own element.
 */
export type LabelPlacement = "inside" | "above" | "below" | "beside" | "before" | "inset";

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
  /**
   * Where the first line sits relative to the slot's own baseline.
   *
   * <b>Bindable, because the RDF family's cards decide it from their own contents</b>: an
   * `owl-card`'s rows begin below its badge line when it has badges and at the header when it
   * does not, and a SHACL shape's rows begin below however many targets it declares. Both are
   * numbers the module already computes to lay itself out; a fixed start would put every card
   * with a badge line one row out, silently.
   */
  start?: DeclaredNumber;
}

/** One declared label. A single-label module writes one of these, which is why migration is mechanical. */
export interface LabelDeclaration {
  /**
   * Lay this label out as WRAPPED text inside the shape's text region rather than as one line.
   *
   * The region comes from `textRegionOf`, so a trapezoid's text stays off its slanted edge and a
   * diode's off its curved end - measuring against the bounding box is what puts text outside a
   * shape that is not a rectangle. Breaks at spaces and at explicit newlines, at the library's
   * own character-width estimate, stacked at the typography's line height; text that does not fit
   * ends its last visible line with an ellipsis and keeps the whole text as the label's tooltip.
   */
  wrap?: boolean;
  /** What it says: a field, a template, or a collection with `each`. */
  text: Binding;
  /** Relative to the shape. Defaults to `inside`, which is what a single centred label is. */
  placement?: LabelPlacement;
  /** A named line inside the box. Ignored when `offset` is given. */
  slot?: LabelSlot;
  /** Explicit position relative to {@link anchorTo}, for a notation with its own geometry. */
  offset?: { x: number; y: number };
  /**
   * What an `offset` is measured from. Defaults to the element's centre.
   *
   * <b>Sufficiency row 5 needs `top`</b>: a C4 card's three lines are pinned 22, 38 and 58
   * pixels below the box's TOP, so they stay put as the card grows taller - measured from the
   * centre they would drift apart on every differently-sized element, which a fixed offset
   * cannot express and a slot fraction gets wrong in the other direction.
   */
  anchorTo?: "centre" | "top" | "bottom";
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
  /**
   * Extra classes for the drawn text, so colour stays in the stylesheet where a token is not
   * enough. For a collection, a bound class is resolved against the ENTRY - a SHACL row carries
   * `shacl-row-sparql` or not depending on what that row is.
   */
  className?: string | Binding;
  /**
   * Which end of the text sits at its position. Defaults to the placement's own alignment.
   *
   * Sufficiency row 20: shacl's badges and cardinalities are right-aligned to the card's edge,
   * which slots - vertical fractions - have no way to say.
   */
  align?: "start" | "middle" | "end";
  /** How far from the aligned edge the line sits. Only meaningful with {@link align}. */
  insetX?: number;
  /**
   * Where the inline editor opens over this line, measured from the box's top.
   *
   * <b>The port of `LabelRule.insetTop` and `insetHeight`</b>, which existed for exactly this:
   * an editor over a composite card must cover the ONE line it edits, not the card. Stated
   * rather than derived from the typography, because the drawn size lives in the stylesheet and
   * a declaration that guessed at it would be a second copy of a number nobody would remember
   * to keep in step.
   */
  editorBox?: { top: number; height: number };
  /**
   * Further columns on the SAME line, each with its own inset and alignment.
   *
   * <b>Sufficiency row 20 is the only reason this exists</b>, and it is a real one: a shacl
   * constraint row is a path on the left, a summary in the middle and a cardinality on the
   * right, per item of a collection. Declaring three labels over the same collection would
   * resolve it three times and, worse, let the three drift out of step if one had a condition.
   * A column is text plus where it sits; it is not a nested label, and deliberately carries no
   * `when`, no collection of its own and no editability.
   */
  columns?: readonly LabelColumn[];
}

/** One further column on a label's line. See {@link LabelDeclaration.columns}. */
export interface LabelColumn {
  text: Binding;
  /** Horizontal inset from the box's left edge, or from its right when aligned `end`. */
  insetX: number;
  align?: "start" | "middle" | "end";
  /** Bound classes resolve against the entry, as the line's own do. */
  className?: string | Binding;
  truncate?: boolean;
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

/**
 * A shape chosen per element, from a field of the model.
 *
 * <b>Rows 5, 18, 27 and 28 justify it.</b> A wardley mark is a square when it is an anchor, a
 * double circle when it is a submap and a circle otherwise; a c4 element carries its shape name
 * in the document. Those are one element type drawing itself differently per element - not four
 * types, which would fragment the notation's own vocabulary, and not a custom renderer.
 *
 * <b>Closed by construction</b>: the cases map to built-in shapes only. A selection that could
 * name a `CustomShapeRef` would be the escape hatch reachable through data, which is worse than
 * the escape hatch reachable through code, because a guard reading the source would not see it.
 */
export interface ShapeSelection {
  path: BindingPath;
  cases: Readonly<Record<string, BuiltInShape>>;
  /** What an unresolved or unlisted value draws. Required: an element always draws something. */
  fallback: BuiltInShape;
}

export type ShapeKind = BuiltInShape | ShapeSelection | CustomShapeRef;

/**
 * Whether a shape is a module-supplied renderer, as against a selection over built-ins.
 *
 * Both are objects, so `typeof shape !== "string"` no longer answers the question it used to -
 * and every caller that asked it meant "is this a custom renderer?". Named here so the three
 * places that ask cannot drift apart.
 */
export function isCustomShape(shape: ShapeKind): shape is CustomShapeRef {
  return typeof shape !== "string" && "customShape" in shape;
}

/**
 * What a drag would land on, drawn before the button is released.
 *
 * <b>This is register entry G12, and it is the one the sufficiency table could not settle.</b>
 * The table's ruling was that the DECISION stays an event handler and the DRAWING becomes
 * declared - and the drawing turned out to need something no module can supply: the candidate
 * changes every frame of a drag, so it is neither a model field nor anything a payload can
 * carry, and the library is the only thing that knows it.
 *
 * So the library computes it, from a rule the module DECLARES rather than one it hard-codes:
 * the topmost element whose box holds the dragged element's centre, excluding the dragged
 * element itself and anything inside its own branch. `parentPath` is what makes "its own
 * branch" meaning anything - without it a tree would offer a node its own child as a parent.
 *
 * <b>The drop itself is still the module's</b>: the library draws the proposal and raises
 * nothing. What a drop MEANS - a re-parent, a refusal, a backend call - is answered in
 * `onElementMoved`, exactly as before.
 */
export interface DropTargetDeclaration {
  /** How the model says which element is inside which - `payload.parentId`. */
  parentPath: BindingPath;
  /** The outline drawn over the candidate. */
  ring?: { className?: string; data?: DataAttributes };
  /** The branch the drop would create, drawn between the candidate and the dragged element. */
  preview?: { className?: string; data?: DataAttributes };
  /** A group around both, for a module that styles or tests them together. */
  group?: { className?: string; data?: DataAttributes };
}

/** `data-*` attributes by name, each stated outright or read from the model. */
export type DataAttributes = Readonly<Record<string, string | Binding>>;

/** One class an element carries, stated or bound, optionally conditioned. */
export interface ClassDeclaration {
  className: string | Binding;
  when?: Condition;
  /**
   * What the class lands on: the SHAPE's own body, or the group wrapping shape, labels and
   * decorations together.
   *
   * <b>Both are needed and a module means different things by them</b>, which the single-label
   * migrations made unmissable: `dotnet-dependency-element-project` selects the whole element -
   * its box, its two lines of text - while `dotnet-dependency-node` is the box's own fill and
   * stroke. Collapsing the two would put a descendant selector's anchor inside the thing it is
   * supposed to contain, and a test looking for `rect` INSIDE `.dotnet-dependency-element` would
   * find nothing.
   *
   * Defaults to `shape`, which is where a class most often bites.
   *
   * `shape-inner` is the second part of a shape that has one - the ring inside a
   * `double-ellipse`. OWL's notation names it separately (`owl-shape` outside,
   * `owl-shape-inner` within), and the library must not invent that name by suffixing: a class
   * a module did not write is a class its stylesheet does not have.
   */
  on?: "element" | "shape" | "shape-inner";
}

/** Paint read from the model. Theme token names, exactly as {@link ElementStyle} takes them. */
export interface BoundElementStyle {
  fill?: Binding;
  stroke?: Binding;
  labelColor?: Binding;
  /**
   * Which silhouette a `styled-box` draws - `"Person"`, `"Cylinder"`, `"Box"`, `"RoundedBox"`.
   *
   * <b>Sufficiency row 5.</b> A C4 document sets its elements' shapes as well as their colours,
   * so the silhouette is data about the element rather than a property of its type - which is
   * exactly what `boundStyle` is for, and why this sits here rather than beside `shape`.
   */
  silhouette?: Binding;
}

/** One kind of thing a diagram draws boxes for. */
export interface ElementTypeDefinition {
  id: string;
  shape: ShapeKind;
  style?: ElementStyle;
  /**
   * Paint taken from the model rather than fixed in the declaration.
   *
   * Sufficiency row 5: a c4 element carries its own background and text colour in the document,
   * because the notation lets an author set them per element. `style` names theme tokens chosen
   * once for the type, which cannot express that. Where both are given, this wins for the
   * fields it names and `style` supplies the rest.
   */
  boundStyle?: BoundElementStyle;
  /**
   * Classes this type's elements carry, beyond the library's own.
   *
   * <b>Every one of the twenty-eight sufficiency rows needed this</b> (register entry G1), and
   * it is the largest single thing the table found: a built-in shape hard-coded `library-shape`
   * and offered no hook at all, so a migrated element would have lost its entire visual
   * identity - its kind colour, its dashed unresolved state, its selection ring. A vocabulary
   * that can draw the right geometry in the wrong colours has not replaced a renderer.
   *
   * A class may be stated outright, bound to a field - `helm-node-{payload.kind}` through a
   * template - or conditioned, including on the canvas's own {@link InteractionState}.
   */
  classNames?: readonly ClassDeclaration[];
  /**
   * `data-*` attributes, stated or bound - `{ testid: "stage-Build", expanded: … }`.
   *
   * <b>The one non-visual thing a declaration carries, and it is here because the modules' own
   * suites are this migration's acceptance</b> (Requirement 8.1). Canvases hang test ids and
   * state flags on what they render - `stage-Build`, `data-expanded`, `indicator-manual` - and
   * their tests read them. A vocabulary that could not reproduce them would force exactly the
   * tests being relied on as the safety net to be rewritten first, which would leave the
   * migration proving nothing.
   *
   * ONE mechanism rather than a field per attribute: `data-expanded` arrived a day after
   * `data-testid`, and a third would have arrived after that.
   */
  data?: DataAttributes;
  /**
   * The `<title>` this type's elements carry as a whole.
   *
   * Rows 1, 12, 13, 17 and 21: a tooltip is the element's, not any one line's, and five
   * renderers write one by hand today - `kind name`, with `— hosts: …` appended when known,
   * which is a parts binding rather than a template for the reason given there.
   */
  tooltip?: Binding;
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
  /**
   * How an element reaches a screen reader and the keyboard.
   *
   * <b>Register entry G23, and the same three rows as G22</b> - ansible-structure,
   * dotnet-dependency-graph and helm-charts each set `role`, `tabIndex` and an `aria-label` on
   * the element they render. Left undeclared, migrating those three would quietly drop an
   * element out of the tab order and off the accessibility tree: a regression no test in this
   * repository would have reported, which is exactly why it belongs in the declaration rather
   * than in whatever a renderer remembered to pass.
   */
  accessibility?: {
    role?: string;
    /** In the tab order, as `tabIndex={0}` puts it. */
    focusable?: boolean;
    /** The accessible name. Usually the same binding as {@link tooltip}, and separate because they are. */
    label?: Binding;
  };
  anchors: AnchorSet;
  sizing: SizingRule;

  /**
   * Which edges a `sizing: "user"` type lets the reader drag. <b>Omitted means `"width"`</b>,
   * which is what every user-sizable type did before this existed - so a type that declares
   * nothing is unaffected, and the timeline's spans keep exactly the two handles they had.
   *
   * `"both"` adds the top and bottom edges, for an element whose height is its own content
   * rather than a shared constant - a comment sized to the text somebody wrote in it. Read only
   * when `sizing` is `"user"`: a content-sized or model-sized element has no edge to offer,
   * because its size is not the reader's to choose.
   */
  resize?: "width" | "both";
  /** Overrides the canvas-wide {@link DraggingPolicy} for this type (Requirement 5.2). */
  draggable?: boolean;
  /** Whether delete gestures reach this type at all (Requirement 5.3). */
  deletable?: boolean;
  /**
   * Cuts an `arrow-banner` into consecutive segments along its width. See {@link SegmentDeclaration}.
   * Ignored on any other shape.
   */
  segments?: SegmentDeclaration;
  /**
   * Whether an element of this type can be the selection. **Omitted means selectable** - every
   * type selects unless its definition says otherwise, so "not selectable" is only ever a value
   * somebody declared and never the absence of one (centralized-selection Requirement 2.3).
   *
   * `false` makes a press on the element behave exactly as a press on the background - the
   * selection clears - and a pushed selection naming it highlights nothing. That is c4's inert
   * boundary: a region the reader reads, not a thing the reader picks.
   */
  selectable?: boolean;
  /**
   * Paints this type's elements beneath the connections: an opaque container whose members'
   * edges must stay visible over it - azure-pipeline's stage cards. Off, connections draw
   * under every element as they always have.
   */
  beneathConnections?: boolean;
}

/**
 * The built-in routes (Requirement 3.1). These absorbed the four connector families that were
 * once separate components (removed once nothing drew them):
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
  /**
   * The edge each end is attached along, for an end attached with `{ kind: "along" }` anchors.
   * A curved route leaves and arrives square to that edge, so its arrowhead meets the border at
   * a right angle rather than at whatever slant the two ends' offset gives.
   */
  sourceEdge?: EdgeName;
  targetEdge?: EdgeName;
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
  /**
   * At most ONE connection of this relation between a pair of elements: `ordered` counts only
   * the same direction, so A -> B and B -> A may both exist; `unordered` counts either.
   *
   * The count reads the MODEL, not what is drawn, so a connection hidden by
   * {@link RelationTypeDefinition.hideWhenAttachmentHidden} or by a filter still blocks its
   * duplicate - hidden is a view, and the document still holds it.
   */
  perPair?: "ordered" | "unordered";
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
   * Whether an end attached ALONG an edge (`{ kind: "along" }` anchors) can be dragged along that
   * edge once the connection is selected. The selected connection shows a round handle on each
   * such end; dragging it slides the end along the same edge, across segments, and the release
   * raises `connection-end-moved` with the new attachment.
   */
  movableEnds?: boolean;
  /**
   * Whether a connection of this type can be the selection. **Omitted means selectable**, as for
   * element types: an unselectable relation is always a recorded decision about the notation,
   * never an omission (centralized-selection Requirements 2.3, 2.4).
   *
   * `false` makes a press on the line behave as a press on the background, and a pushed
   * selection naming it highlights nothing - mindmap's and wardley-map's lines, which select
   * nothing today by design.
   */
  selectable?: boolean;
  /**
   * Adornment drawn inside the connection's group, after its line - polarity signs, delay
   * strokes, anything the notation rides on a line. Handed the resolved route so it can place
   * itself along the real geometry, and <b>whether the connection is highlighted</b>, so it takes
   * the selected colour with the line it describes: the highlight is painted inline now, and an
   * inline paint reaches only what the library draws itself (centralized-selection Requirement 5.3).
   */
  adorn?: (
    route: { from: ShapePoint; to: ShapePoint; waypoints: readonly ShapePoint[]; ends?: RouteEnds; highlighted?: boolean },
    connection: unknown,
  ) => unknown;
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
  /**
   * Hide a connection whose source or target attachment names a segment region its element is
   * not drawing - an index at or beyond the element's segment count. Hidden means not drawn, not
   * hit-tested and not selectable; the connection stays in the model untouched, so raising the
   * count draws it again where it was.
   */
  hideWhenAttachmentHidden?: boolean;
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
 * The backdrop a diagram type draws beneath everything.
 *
 * <b>THE THIRD ESCAPE HATCH IS GONE FROM HERE, AND THAT IS WHAT MAKES REQUIREMENT 2.9 TRUE.</b>
 *
 * `DiagramBackgroundRef` was a module-supplied `render` the library called - a function-valued
 * escape hatch that Requirement 2.9's original wording did not name, because it listed
 * `CustomShapeRef` and `CustomRouteRef` rather than stating the property. A list of two admits
 * a third silently, which is this specification's own subject appearing inside its own
 * requirements; the requirement now states the property, and this type is data.
 *
 * It had exactly one user, `wardley-map`, and it died with that module's migration: twenty-one
 * of the twenty-four raw-SVG lines it drew were evolution bands and axes, and every one of them
 * is a declaration now.
 */
export type DiagramBackground = BackgroundDeclaration;

/** The whole statement of what a diagram type allows (Requirement 4.1). */
/**
 * Where a dragged element is allowed to come to rest, per axis.
 *
 * <b>The element's LEADING EDGE comes to rest on a line - its top for y, its left for x - and
 * the lines lie at `origin + k × step`.</b> An edge rather than the centre because a row, and a
 * date, is where an element BEGINS: both modules that snap draw a row's element with its top on
 * the row and a period with its left on its begin. Snapping the centre instead put every dragged
 * element half its own height above its row, and the drop - which the backend places by the row
 * - moved it that half again once confirmed: the snap seen during the drag was not the snap on
 * the drop, which is exactly what the user reported. The preview and the release both land
 * through this one rule, so what the drag shows is what the drop sends.
 *
 * <b>`step` and `origin` may be bound per element</b>, for a lattice that is not the same for
 * every element: the timeline snaps a date-only element to whole days - the start of a day,
 * where its backend lands a date-only begin - while an element with a time of day moves freely.
 * An axis whose step resolves to nothing, or to a non-positive number, does not snap that element.
 *
 * <b>This names a rule the tree already agreed on in five places rather than inventing one.</b>
 * `TimelineCanvas`'s and `DependencyGraphCanvas`'s `nearestRow` are byte-identical bodies
 * differing only in which height they close over; `TimelineRows.ToNearestRow` and
 * `DependencyGraphRows.ToNearestRow` are the same arithmetic on the backend; and the binding
 * vocabulary's `round: "nearest"` is the same rule a third time. The library owns it now, so a
 * sixth copy is never written.
 *
 * <b>Halves round AWAY FROM ZERO, and that is a correctness obligation rather than a
 * preference.</b> `Math.round` sends −0.5 to −0, which puts a drag one row out ABOVE the origin
 * and nowhere else — a rounding rule that is right in the common half of the canvas and wrong
 * in the other is worse than no rounding at all. The binding vocabulary's own comment records
 * the same finding; this is the drag-time half of it.
 *
 * <b>Per axis, and not a grid.</b> The dependency graph snaps y only and leaves x free; the
 * timeline snaps rows on y and, for a date-only element, whole days on x - a per-element lattice,
 * which a grid could not say.
 */
export interface SnapDeclaration {
  /** Rows: the element's top rests on a line - a timeline or dependency-graph row. */
  y?: SnapAxis;
  /** Columns: the element's left rests on a line - a timeline's whole days, for a date-only element. */
  x?: SnapAxis;
}

/** One axis's resting lines, `origin + k × step`, in canvas units. */
export interface SnapAxis {
  /** The distance between lines. Bound, it may differ per element; resolving to nothing, it does not snap. */
  step: DeclaredNumber;
  /** Where one line lies. Omitted, a line lies at 0. */
  origin?: DeclaredNumber;
}

/**
 * An element cut into consecutive segments along its width, each its own fill, tooltip and stretch
 * of the top and bottom edges.
 *
 * <b>The library draws what it is given.</b> Where the boundaries lie is the module's arithmetic,
 * bound from the model, so the drawing and the document can never disagree about it; the library
 * only falls back to an even spread when the model gives none, or gives a list it cannot use.
 *
 * Only the first `count` segments are drawn, over the element's FULL width, so the last drawn
 * segment is the one that ends in the point.
 */
export interface SegmentDeclaration {
  /** How many segments are drawn, 1 to {@link max}. */
  count: DeclaredNumber;
  /** How many segments an element of this type can have. */
  max: number;
  /**
   * A path to an array of fractions of the width, one per inner boundary between drawn segments
   * (`count - 1` of them), strictly increasing within 0..1. Missing or unusable, the drawn
   * segments share the width evenly.
   */
  boundaries?: BindingPath;
  /** One class per segment index, for its fill. */
  classNames?: readonly string[];
  /** One tooltip per segment index. Over the point, the last drawn segment's shows. */
  tooltips?: readonly string[];
  /** How two segments meet: a right-pointing chevron, or a straight line. Defaults to `chevron`. */
  divider?: "chevron" | "line";
  /**
   * Give each inner boundary a horizontal drag handle over its divider. A drag raises
   * `segment-boundary-moved` with the snapped position, clamped so no segment becomes narrower
   * than one step of the definition's `snap.x` (one unit where it declares none).
   */
  draggableBoundaries?: boolean;
}

/**
 * A filter box over the canvas: elements whose tag list does not match the typed expression are
 * not drawn, and neither is any connection touching them.
 *
 * The expression is `a and (b or c)` - `and` binding tighter than `or`, case-insensitive, a tag
 * being any run of non-space characters other than the keywords and parentheses. It is view
 * state, held by the canvas and never sent anywhere, and it survives every model update.
 */
export interface FilterDeclaration {
  /** A path to the element's tags: an array of strings. */
  field: BindingPath;
  /** The filter box's placeholder. */
  label: string;
}

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
  /** Where a dragged element is allowed to come to rest. See {@link SnapDeclaration}. */
  snap?: SnapDeclaration;
  /** What a drag would land on, drawn while it is in flight. See {@link DropTargetDeclaration}. */
  dropTarget?: DropTargetDeclaration;
  background?: DiagramBackground;
  /**
   * What the canvas shows AROUND the diagram: loading, unavailable, a title, a legend, and
   * view-fixed rulers. Eleven percent of every module client is this today, hand-written, and
   * two modules showing the same state show it differently.
   */
  chrome?: ChromeDeclaration;
  /**
   * Every action this diagram type offers, with what invokes each and whether it is enabled.
   *
   * The library derives its shortcut key set from these and dispatches an action id, so a
   * module never writes a key list and never manufactures a key event to name an action.
   */
  actions?: readonly ActionDeclaration[];
  /**
   * Whether a right-click on the EMPTY canvas opens the shared menu for the point clicked.
   * **Omitted means no background menu** - a background right-click then does nothing, as it
   * always has; a canvas offers one only by declaring it (centralized-selection, design A,
   * "The background menu").
   *
   * Declared, the library turns the pointer into canvas coordinates the way a toolbox drop is,
   * pushes the placement `new:x,y` through its own context-menu selection, and opens the shared
   * menu on the backend's answer - so the diagram's own actions (arrange, add here) are offered
   * where the user clicked. A right-drag that drew a relation is still not a menu.
   */
  backgroundMenu?: boolean;
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
  /**
   * Sets of relation types within which a directed cycle is not admitted. See {@link AcyclicRule}.
   *
   * Omitted, every relation may close a cycle, which is what a causal loop diagram is made of -
   * so this is declared by the notations that forbid one, and by no others.
   */
  acyclic?: readonly AcyclicRule[];
  /** A filter box over the canvas. See {@link FilterDeclaration}. Omitted, the canvas shows none. */
  filter?: FilterDeclaration;
}

/**
 * One set of relation types that must stay acyclic among themselves.
 *
 * <b>A set rather than a flag, because acyclicity is a property of a set of edges and not of a
 * diagram.</b> A decomposition's "contains" relations must form a tree while its annotations,
 * drawn between the same elements, may run any way they like - and a per-relation boolean cannot
 * say that a path alternating between two relation types is still a cycle, which is precisely
 * what a set does say.
 *
 * Several sets are allowed and are independent: a relation belonging to two of them is refused if
 * either would close.
 */
export interface AcyclicRule {
  /** The relation type ids the rule covers. A path leaves the set at the first edge outside it. */
  relationTypes: readonly string[];
}
