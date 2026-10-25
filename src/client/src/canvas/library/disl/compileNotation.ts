import type { ActionDeclaration, ActionInvocation } from "../definition/actions";
import type { Binding, Condition } from "../definition/binding";
import type { AxisDeclaration, BandDeclaration, BackgroundDeclaration } from "../definition/background";
import type { CompartmentDeclaration } from "../definition/compartments";
import type { CalendarStep, ChromeBandDeclaration, ChromeLegendDeclaration, ChromeNoticeDeclaration, ChromeTextDeclaration, RulerDeclaration, RulerRung, SwitchDeclaration } from "../definition/chrome";
import type {
  Cardinality,
  AnchorSet,
  BoundElementStyle,
  BuiltInShape,
  ClassDeclaration,
  CompassPosition,
  CustomRouteRef,
  DataAttributes,
  DecorationDeclaration,
  DiagramDefinition,
  ElementTypeDefinition,
  EndpointConstraint,
  FilterDeclaration,
  LabelDeclaration,
  LabelTypography,
  LabelUnderline,
  LayoutDefinition,
  LayoutMode,
  LinkDeclaration,
  MarkerKind,
  StubDeclaration,
  RelationTypeDefinition,
  RouteLabelRule,
  SegmentDeclaration,
  ShapeBounds,
  SideFraction,
  SnapAxis,
  SnapDeclaration,
  StrokeLadder,
  StrokeRung,
  ToolboxDropDeclaration,
} from "../definition/diagramDefinition";
import { ArcBow } from "./bowedArc";
import { bowedArcAdornment, bowedArcRoute, type ArcBarsDeclaration, type ArcMarkDeclaration, type BowedArcDeclaration } from "./bowedArcLine";
import { ribbonAdornment, type RibbonDeclaration } from "./ribbonLine";
import { BEFORE_GAP } from "../definition/labels";
import {
  typeList,
  type Bindable,
  type DislAxis,
  type DislAxisRange,
  type DislBadge,
  type DislBadgeLayout,
  type DislCompartment,
  type DislContextMenu,
  type DislCoordinateSystem,
  type DislCustomShape,
  type DislDocument,
  type DislEdgeLabel,
  type DislEdgeNotation,
  type DislIcon,
  type DislLabel,
  type DislMidMarker,
  type DislNodeNotation,
  type DislRelation,
  type DislStub,
  type DislViewpoint,
} from "./disTypes";
import { libraryMarkerOf, libraryRouteOf, libraryShapeOf, shapeNameOf, type CustomShapeBinding } from "./shapeCatalog";
import { drawingArea, type DrawingArea } from "./drawingArea";

/**
 * A DISL notation compiled into the library's {@link DiagramDefinition}: DISL 0.3, and of DISL 0.4
 * the lists inside a node, its badges, a force layout's rings and a kept switch.
 *
 * <b>One file, two readers.</b> A diagram module bundles its `.dis` (`src/diagrams/<module>/definition/`),
 * the backend runs it, and the client imports the same bytes with Vite's `?raw` and compiles them
 * here at module load. There is no generated file, so nothing can go stale between the two tiers.
 *
 * <b>Structural, and refusing what it reads.</b> The notation's nodes, edges, anchors, labels, sizes,
 * placements, the coordinates' snapping and ruler, the canvas's filter and legend, the context menus'
 * shortcuts and the viewpoints are mapped onto the library's vocabulary key by key. A value it reads
 * and cannot map - a shape, a marker, a label position, a CEL term - is refused with an error naming
 * it. Keys it never reads (styles, conditions, a viewpoint's styling) are not checked here: the
 * modules' compiled-definition tests hold the result to the hand-written drawing instead.
 *
 * <b>What stays code - {@link NotationBindings}.</b> The library has no CEL, so a CEL label becomes a
 * template over the payload the backend computes, through the module's table of expression-to-path
 * entries; class names, which tie the drawing to the module's stylesheet; and what a custom shape is
 * drawn as. Colours stay in CSS: a module's test proves its theme tokens equal the custom properties.
 */

/** What a module states beside its specification, because the library cannot read it from there. */
export interface NotationBindings {
  /**
   * The specification's `x-` block holding today's wire ids, shaped as `x-abm` is: `types` (DISL
   * name to wire id; a type it does not list is its name in lower case) and `actions` (an entry's
   * operation, else its kind, to the action id; `"<Type>/<key>"` overrides that for one type).
   * A module whose wire ids are the specification's own names states none: a type is then its name
   * in lower case and an action its operation, else its kind - as the backend's derivation has them.
   */
  wireIds?: `x-${string}`;
  /**
   * Where the canvas's type ids come from. By default a type is its wire id, as the backend sends
   * it. `"specification"` names each type by the specification, in lower case, while actions and
   * tools keep their wire ids: for a module whose wire block gives several types one wire type and
   * which tells them apart itself as it builds the canvas's model. Two types with one id are refused.
   */
  typeIds?: "specification";
  /**
   * The payload path the backend computes for each CEL expression the notation binds, keyed by the
   * expression as written. `self.<attribute>` needs no entry: it is `payload.<attribute>`.
   */
  celPaths: Readonly<Record<string, string>>;
  /** The classes an element type's group and shape carry, which the module's stylesheet paints. */
  classNames: (type: string, node: DislNodeNotation) => readonly ClassDeclaration[];
  /** A label's class: a name, or a binding where the class follows the payload. */
  labelClassName?: (type: string, label: DislLabel) => string | Binding | undefined;
  /**
   * A line the library draws under a label, where DISL's text decoration cannot say its style: DISL
   * has `underline` and no wavy form, and a stylesheet's wavy underline is drawn straight on SVG text.
   */
  labelUnderline?: (type: string, label: DislLabel) => LabelUnderline | undefined;
  /**
   * The path of each CEL term a list's row binds, within the row, keyed by the term as written.
   * `item.<attribute>` needs no entry: it is `<attribute>`.
   */
  rowPaths?: Readonly<Record<string, string>>;
  /** The class of a badge the library draws as a mark, where the stylesheet paints it. */
  badgeClassName?: (type: string, badge: DislBadge) => string | undefined;
  /** The `data-*` attributes a badge carries, which the module's tests find it by. */
  badgeData?: (type: string, badge: DislBadge) => DataAttributes | undefined;
  /** The `data-*` attributes an element type's group carries, which the module's tests find it by. */
  elementData?: (type: string, node: DislNodeNotation) => DataAttributes | undefined;
  /**
   * Ornaments drawn after the ones the specification states, for what is the host's rather than the
   * specification's to say: where a finding of the problems service is marked on an element.
   */
  elementDecorations?: (type: string, node: DislNodeNotation) => readonly DecorationDeclaration[];
  /** A label's inline editor box, where it should not cover the label's own line. */
  labelEditorBox?: (type: string, label: DislLabel) => { top: number; height: number } | undefined;
  /** A relation's class. */
  relationClassName?: (relation: string) => string | undefined;
  /** A relation's line's own class, where the stylesheet paints the line rather than the whole relation. */
  relationLineClassName?: (relation: string) => string | undefined;
  /** A relation's wide, invisible hit line's class, where the stylesheet dresses it. */
  relationHitClassName?: (relation: string) => string | undefined;
  /** An endpoint's anchor rule, where the module states one. */
  endpointAnchors?: (relation: string, end: "source" | "target") => EndpointConstraint["anchors"] | undefined;
  /** What each custom shape of the specification is drawn as. */
  customShapes?: Readonly<Record<string, CustomShapeBinding>>;
  /** The payload paths of each element's own snap origin. */
  snapOrigins?: { x: string; y: string };
  /**
   * An axis's resting lines where they are the module's arithmetic rather than a step of the axis: a
   * timeline's day, whose width in canvas units follows the scale the canvas froze, rides each element's
   * payload. Given for an axis, it replaces what the snapping rule would state.
   */
  snapAxes?: Partial<Record<"x" | "y", SnapAxis>>;
  /**
   * The value a drag shows, where the default system's snapping asks for one (`feedback.showValue`, DISL
   * §5.6): what the value is - a time, a row - is the axes' arithmetic over the live bounds, which no
   * template over the payload carries. It is added to every type that moves.
   */
  dragHint?: LabelDeclaration;
  /**
   * Whether the module draws its axes' rulers itself, as the timeline does beside the canvas: the
   * compiler then leaves the specification's rulers to it rather than refusing one it cannot map.
   */
  ownRulers?: boolean;
  /** The class a legend swatch takes for one enum value. */
  legendSwatchClass?: (enumName: string, value: string) => string;
  /** Library declarations the specification states only under a module's `x-` key, which DISL has no construct for. */
  extras?: (spec: DislDocument) => Partial<DiagramDefinition>;
  /**
   * The condition each CEL `visible` of a label stands for, keyed by the expression as written, as
   * {@link celPaths} does for a value: the library has no CEL, so the payload carries what it tests.
   */
  celConditions?: Readonly<Record<string, Condition>>;
  /**
   * What a DISL built-in shape is drawn as, where a module has stated it: one this library has no drawing
   * of (`roundedRect`) as a shared shape of its own family, such as the dependency graphs' `span`; or one
   * it draws otherwise in the module's family, such as the timeline's `diamond` as its `moment` marker.
   */
  builtInShapes?: Readonly<Record<string, BuiltInShape>>;
  /** Whether every element is in the tab order, which DISL leaves to the runtime (§6.15: keyboard navigation). */
  focusable?: boolean;
  /**
   * The role every element is announced with, which DISL leaves to the runtime (§6.15). Given, or with
   * {@link focusable}, a node that states no `accessibility` is named as DISL's default names it: its
   * type's label and its label attribute.
   */
  accessibleRole?: string;
  /** The class a relation's stub is painted by (DISL §6.10 *Stubs*), standing for the stub's `style`. */
  stubClassName?: (relation: string, stub: DislStub) => string | undefined;
  /**
   * The route each edge is drawn with where the library's built-in route for its DISL routing is not
   * the drawing: a bezier's exact reach and its way back round, which DISL states only in outline.
   */
  customRoutes?: Readonly<Record<string, CustomRouteRef>>;
  /**
   * The edges drawn as a ribbon, a filled band along their route as thick as their style's stroke width,
   * measured vertically, in their stroke's colour, with the class each names. DISL strokes every edge, and
   * a stroke is thick across a curve rather than down it; the route is the edge's custom route.
   */
  ribbons?: Readonly<Record<string, { className?: string }>>;
  /**
   * An action as the module dispatches it, given the compiled declaration and the shortcut its menu
   * entry writes (its `KeyboardEvent.key`, without the modifiers, which a `backendKey` cannot carry): where a module's backend resolves an action by its key
   * (`backendKey`), or a second key invokes it, which DISL's one `shortcut` per entry cannot say.
   */
  action?: (action: ActionDeclaration, entry: { shortcut?: string }) => ActionDeclaration;
  /** The class of a node's icon, which the module's stylesheet paints. */
  iconClassName?: (type: string, icon: DislIcon) => string | undefined;
  /** The classes of a relation's bars across its line: the group's and each bar's. */
  midMarkerClassNames?: (relation: string) => { group?: string; marker?: string };
  /** The class of a mark written beside a relation's line. */
  edgeLabelClassName?: (relation: string, label: string) => string | undefined;
  /** The class that paints each width a relation's computed stroke width gives. */
  strokeWidthClassName?: (relation: string, width: number) => string | undefined;
  /**
   * The class prefix that paints a colour chosen by index - `token('<family>' + string(self.<a> % n))`
   * in a node's style, a palette slot's hue - keyed by the token's family. The library has no CEL, so
   * it wraps the index into its slot and writes the prefix and the slot as one class, and the
   * stylesheet paints the slot, mixed as the specification says.
   */
  indexedColourClassName?: (type: string, token: string) => string | undefined;
  /**
   * Whether the module answers the toolbox's drops itself. Given, the compiler states no `toolbox.drops`
   * and every drop reaches the module as `element-dropped`; for a specification whose drops land on the
   * canvas, or are refused by a computed sentence, which the backend evaluates and the library cannot.
   */
  toolboxDrops?: "module";
  /**
   * The colours and the silhouette a `styled-box` takes from each element, where they are the document's
   * rather than its type's: a C4 author sets an element's shape and colours, and the backend resolves
   * them from its theme and the document, so the payload carries what the type's style only defaults.
   */
  boundStyle?: (type: string, node: DislNodeNotation) => BoundElementStyle | undefined;
  /**
   * Whether a label cut or wrapped at its node's width is fitted at the size its style draws it: by its
   * style's font size, inside the node style's horizontal padding, a wrapped label's lines its font size
   * times its line height apart (DISL §6.12 `overflow`, `wrap`; §6.6 `font`, `padding`). Without it the
   * library fits a label by its own measures, as it does for every specification that states none.
   */
  labelFit?: "style";
  /**
   * Whether a node's stroke and fill, and its labels' size, weight, slant and colour, are drawn from the
   * notation's styles (DISL §6.6 `stroke`, `fill`, `font`) rather than left to the module's stylesheet. A
   * stroke of no width is not drawn; a token is its custom property, `--` and its name with dashes for dots.
   */
  stylesFromNotation?: boolean;
  /**
   * The canvas's title and key (DISL §6.13 `canvas.title`, `canvas.legend`) as the module's stylesheet and
   * tests know them. A computed key's CEL runs over the elements drawn, which the backend evaluates, so its
   * entries are the list the model's background holds under `entries`, each read by the key's own CEL
   * through {@link rowPaths}.
   */
  canvasChrome?: {
    title?: { className?: string; data?: Readonly<Record<string, string>> };
    /** The header band's class, and each row's by its id (DISL §6.13 `canvas.header`). */
    header?: { className?: string; rowClassName?: (row: string) => string | undefined; data?: Readonly<Record<string, string>> };
    /**
     * Per notice id (`budget:<id>` for a budget's), its class and when it shows. A budget's notice shows
     * while the budget cuts what is drawn, which only the module's backend knows: the condition over the
     * model's background that says so is the module's to state.
     */
    notices?: Readonly<Record<string, { className?: string; when?: Condition; data?: Readonly<Record<string, string>> }>>;
    legend?: { entries: string; className?: string; titleClassName?: string; entryClassName?: string; swatchClassName?: string; data?: Readonly<Record<string, string>> };
  };
  /**
   * How a bounded canvas's axes are drawn along its edges (DISL §5.3 `ranges`, `endLabels`, `label`): the
   * backdrop's class, an axis's, a range's band's by its axis, the range and its place among that axis's
   * ranges, and the type the titles, end labels and range labels are set in. DISL paints a range by its
   * style, which the module's stylesheet stands for here, and states no size for those labels.
   */
  axisChrome?: {
    className?: string;
    axisClassName?: string;
    rangeClassName?: (axis: string, range: DislAxisRange, index: number) => string | undefined;
    typography?: LabelTypography;
  };
}

export interface CompileOptions {
  /** The diagram attributes the notation binds (`{ attribute: "unit" }`), when not their default. */
  diagram?: Readonly<Record<string, string>>;
  /**
   * The pane the canvas is drawn in, for a canvas stretched to it (DISL §6.13 `fit: "stretch"`): the
   * drawing area follows the pane's proportions, so a module compiles again when they change.
   */
  pane?: { width: number; height: number };
}

interface WireIds {
  types?: Readonly<Record<string, string>>;
  actions?: Readonly<Record<string, string>>;
  /** A toolbox entry's ids, keyed by the tool's id: `drop` is what its drag carries. */
  tools?: Readonly<Record<string, { id?: string; drop?: string }>>;
}

/** How many months each DISL time unit spans, for a month-indexed axis. */
const UNIT_MONTHS: Readonly<Record<string, number>> = { month: 1, quarter: 3, year: 12, decade: 120, century: 1200, millennium: 12000 };

/** The library's calendar rung for a DISL ruler level: a century is ten decades, a millennium a hundred. */
const RUNG_STEPS: Readonly<Record<string, { calendar: CalendarStep; count?: number }>> = {
  month: { calendar: "month" },
  quarter: { calendar: "quarter" },
  year: { calendar: "year" },
  decade: { calendar: "decade" },
  century: { calendar: "decade", count: 10 },
  millennium: { calendar: "decade", count: 100 },
};

/** DISL's key names (§7.3) that are not their `KeyboardEvent.key`. */
const KEY_VALUES: Readonly<Record<string, string>> = {
  Space: " ",
  Up: "ArrowUp",
  Down: "ArrowDown",
  Left: "ArrowLeft",
  Right: "ArrowRight",
  Esc: "Escape",
};

/** The modifiers a DISL shortcut writes before its key, as the backend's `DerivedShortcut.Parse` reads them. */
const MODIFIERS: Readonly<Record<string, "ctrl" | "shift" | "alt" | "meta">> = {
  ctrl: "ctrl", control: "ctrl", shift: "shift", alt: "alt", option: "alt", meta: "meta", cmd: "meta", command: "meta",
};

type ShortcutInvocation = Extract<ActionInvocation, { kind: "shortcut" }>;

/**
 * A shortcut as the library matches it, in the shape a `KeyboardEvent` reports the press: `Alt+Up` is
 * the key `ArrowUp` with `alt` held. Kept whole, `"Alt+Up"` was a key no keypress ever has.
 */
function shortcutOf(text: string): ShortcutInvocation {
  const parts = text.split("+");
  const shortcut: ShortcutInvocation = { kind: "shortcut", key: "" };
  let used = 0;
  for (; used < parts.length - 1; used++) {
    const modifier = MODIFIERS[parts[used].trim().toLowerCase()];
    if (modifier === undefined) {
      break;
    }
    shortcut[modifier] = true;
  }
  const key = parts.slice(used).join("+");
  shortcut.key = KEY_VALUES[key] ?? key;
  return shortcut;
}

/** Whether two shortcuts are the same press. */
function samePress(a: ShortcutInvocation, b: ShortcutInvocation): boolean {
  return a.key === b.key && !a.ctrl === !b.ctrl && !a.shift === !b.shift && !a.alt === !b.alt && !a.meta === !b.meta;
}

/** The compass positions as fractions of the bounds. */
const COMPASS: Readonly<Record<CompassPosition, readonly [number, number]>> = {
  n: [0.5, 0], ne: [1, 0], e: [1, 0.5], se: [1, 1], s: [0.5, 1], sw: [0, 1], w: [0, 0.5], nw: [0, 0],
};

/** Where the first line of text inside a node sits beneath its top, and half of it above its bottom. */
const LABEL_LINE = 20;

/** A list's own measures, which DISL leaves to the host: the gap above it, its lines and its insets. */
const LIST_GAP = 12;
const LIST_HEADING = 20;
const LIST_ROW = 18;
const LIST_BOTTOM = 8;
const LIST_INSET = 10;
const LIST_INDENT = 12;

/** A badge's measures: its distance from the corner, a mark's radius, and a link symbol's size and top. */
const BADGE_INSET = 9;
const BADGE_RADIUS = 3.5;
const BADGE_TOP = 5;
const LINK_SIZE = 11;

/** What a relation takes from its supertype where it does not state it (DISL 4.7). */
/**
 * A relation end's `max` (DISL §4.9) as the library's limit at that end - `maxFromSource` for the source,
 * `maxIntoTarget` for the target - or nothing for an end that states none.
 */
function endLimit(end: unknown, limit: "maxFromSource" | "maxIntoTarget"): { maxFromSource?: number; maxIntoTarget?: number } {
  return isObject(end) && typeof end.max === "number" ? { [limit]: end.max } : {};
}

const INHERITED_RELATION_KEYS = ["source", "target", "directed", "allowSelfLoops", "allowParallel", "acyclic"] as const;

/** The three relation flags a subtype may only narrow (DISL 0.6, §4.7), with their §4.9 defaults. */
const RELATION_FLAGS = { directed: true, allowSelfLoops: false, allowParallel: true } as const;

/** A type's direct supertypes, from an `extends` that names one or a list (DISL §4.7). */
function supertypesOf(extended: string | readonly string[] | undefined): readonly string[] {
  return extended === undefined ? [] : typeof extended === "string" ? [extended] : extended;
}

/**
 * A type and its supertypes, most specific first: the C3 linearisation (DISL §4.7). A supertype the types do not
 * declare is left out, and so is one that would close a cycle; supertypes that cannot be put in one order are refused.
 */
function linearisation(name: string, types: Readonly<Record<string, { extends?: string | readonly string[] }>>, visiting: readonly string[] = []): string[] {
  const parents = supertypesOf(types[name]?.extends).filter((parent) => types[parent] !== undefined && parent !== name && !visiting.includes(parent));
  const sequences = [...parents.map((parent) => linearisation(parent, types, [...visiting, name])), [...parents]];
  const result = [name];
  for (;;) {
    const open = sequences.filter((sequence) => sequence.length > 0);
    if (open.length === 0) {
      return result;
    }
    const head = open.map((sequence) => sequence[0]!).find((candidate) => !open.some((sequence) => sequence.indexOf(candidate) > 0));
    if (head === undefined) {
      return fail(`"${name}" has supertypes that cannot be put in one order (C3, DISL §4.7).`);
    }
    result.push(head);
    for (const sequence of open) {
      if (sequence[0] === head) {
        sequence.shift();
      }
    }
  }
}

/** Whether a filter is a switch whose value is kept with the diagram (DISL 0.4 §6.13.1). */
function isKeptSwitch(filter: { control?: string; persist?: boolean }): boolean {
  return filter.control === "switch" && filter.persist === true;
}

function fail(message: string): never {
  throw new Error(`compileNotation: ${message}`);
}

function isObject(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * Splits a CEL expression at its top-level `+`, outside parentheses and string literals - the one
 * operator a label that concatenates text uses.
 */
function concatenationTerms(expression: string): string[] {
  const terms: string[] = [];
  let depth = 0;
  let quote: string | null = null;
  let start = 0;
  for (let index = 0; index < expression.length; index++) {
    const character = expression[index]!;
    if (quote !== null) {
      if (character === "\\") {
        fail(`the CEL "${expression}" escapes a character in a string, which no template can carry.`);
      }
      if (character === quote) {
        quote = null;
      }
    } else if (character === "'" || character === "\"") {
      quote = character;
    } else if (character === "(" || character === "[") {
      depth++;
    } else if (character === ")" || character === "]") {
      depth--;
    } else if (character === "+" && depth === 0) {
      terms.push(expression.slice(start, index).trim());
      start = index + 1;
    }
  }

  terms.push(expression.slice(start).trim());
  return terms;
}

/** The icons the library draws, by their DISL name, and the glyph each is drawn as. */
const ICONS: Readonly<Record<string, DecorationDeclaration["glyph"]>> = { "mdi-sync": "circular-arrow" };

/** An icon's size where the specification states none, and the gap between a body-less node's icon and its point. */
const ICON_SIZE = 24;
const ICON_GAP = 2;

/** An expression without the parentheses that enclose all of it. */
function unwrapped(expression: string): string {
  let text = expression.trim();
  while (text.startsWith("(") && text.endsWith(")")) {
    let depth = 0;
    let encloses = true;
    for (let index = 0; index < text.length; index++) {
      depth += text[index] === "(" ? 1 : text[index] === ")" ? -1 : 0;
      if (depth === 0 && index < text.length - 1) {
        encloses = false;
        break;
      }
    }
    if (!encloses) {
      break;
    }
    text = text.slice(1, -1).trim();
  }
  return text;
}

/** A stroke width ladder read from its CEL: nested `test ? width : rest`, ending in a width. See `Compiler.strokeLadder`. */
function strokeLadderOf(expression: string, attributes: Set<string>, where: string): { rungs: StrokeRung[]; otherwise: number } {
  const text = unwrapped(expression);
  if (/^-?\d+(\.\d+)?$/.test(text)) {
    return { rungs: [], otherwise: Number(text) };
  }

  // The `?` at the top level, and the `:` that closes it, counting the ternaries nested in its first branch.
  let depth = 0;
  let question = -1;
  let colon = -1;
  let open = 0;
  for (let index = 0; index < text.length; index++) {
    const character = text[index];
    depth += character === "(" ? 1 : character === ")" ? -1 : 0;
    if (depth !== 0) {
      continue;
    }
    if (character === "?") {
      if (question < 0) {
        question = index;
      } else {
        open++;
      }
    } else if (character === ":" && question >= 0) {
      if (open === 0) {
        colon = index;
        break;
      }
      open--;
    }
  }

  if (question < 0 || colon < 0) {
    return fail(`${where} "${expression}" is no ladder of thresholds the library can read.`);
  }

  const test = text.slice(0, question).trim();
  const then = strokeLadderOf(text.slice(question + 1, colon), attributes, where);
  const rest = strokeLadderOf(text.slice(colon + 1), attributes, where);
  if (then.rungs.length > 0) {
    fail(`${where} "${expression}" nests a choice in a width; the library reads a ladder whose steps are widths.`);
  }

  const absent = /^!has\(self\.([A-Za-z_][A-Za-z0-9_]*)\)$/.exec(test);
  const compared = /^self\.([A-Za-z_][A-Za-z0-9_]*)\s*(>=|<=|>|<)\s*(-?\d+(?:\.\d+)?)$/.exec(test);
  const tests: Readonly<Record<string, StrokeRung["test"]>> = { ">=": "atLeast", "<=": "atMost", ">": "above", "<": "below" };
  const rung: StrokeRung | undefined = absent !== null
    ? { test: "absent", width: then.otherwise }
    : compared !== null
      ? { test: tests[compared[2]!]!, value: Number(compared[3]), width: then.otherwise }
      : undefined;
  if (rung === undefined) {
    return fail(`${where} "${expression}" tests "${test}"; the library reads a ladder's tests as !has(self.a) or self.a against a number.`);
  }
  attributes.add((absent ?? compared)![1]!);

  return { rungs: [rung, ...rest.rungs], otherwise: rest.otherwise };
}

/** The compiler's state for one specification and one set of bindings. */
class Compiler {
  private readonly wire: WireIds;
  private readonly nodeNames: readonly string[];
  private readonly system: DislCoordinateSystem | undefined;
  private readonly systemName: string | undefined;
  private readonly defaultViewpoint: [string, DislViewpoint] | undefined;
  private readonly area: DrawingArea | undefined;

  constructor(
    private readonly spec: DislDocument,
    private readonly bindings: NotationBindings,
    private readonly options: CompileOptions,
  ) {
    const wire = bindings.wireIds === undefined ? {} : spec[bindings.wireIds];
    if (!isObject(wire)) {
      fail(`the specification has no "${bindings.wireIds}" block of wire ids.`);
    }

    this.wire = wire as WireIds;
    this.nodeNames = Object.keys(spec.notation.nodes);
    const viewpoints = Object.entries(spec.viewpoints ?? {});
    this.defaultViewpoint = viewpoints.find(([, viewpoint]) => viewpoint.default === true) ?? viewpoints[0];
    this.systemName = this.defaultViewpoint?.[1].coordinateSystem ?? spec.coordinates?.default;
    this.system = this.systemNamed(this.systemName);
    try {
      this.area = drawingArea(spec, options.pane, this.systemName);
    } catch (error) {
      fail((error as Error).message);
    }
  }

  compile(): DiagramDefinition {
    const spec = this.spec;
    this.distinctIds("node types", this.nodeNames);
    this.distinctIds("edges", Object.keys(spec.notation.edges ?? {}));
    const elementTypes = this.nodeNames.map((name) => this.elementType(name, spec.notation.nodes[name]!));
    const relationTypes = Object.entries(spec.notation.edges ?? {}).map(([name]) => this.relationType(name));
    const acyclic = this.acyclicRules();
    const actions = this.actions();
    const snap = this.snap();
    const rulers = this.rulers(this.system);
    const filter = this.filter();
    const switches = this.switches();
    const empty = this.emptyCanvas();
    const title = this.canvasTitle();
    const header = this.canvasHeader();
    const notices = this.notices();
    const legend = this.computedLegend();
    const chrome = {
      ...(rulers.length > 0 ? { rulers } : {}),
      ...(switches.length > 0 ? { switches } : {}),
      ...(empty !== undefined ? { empty } : {}),
      ...(title !== undefined ? { title } : {}),
      ...(header !== undefined ? { header } : {}),
      ...(notices.length > 0 ? { notices } : {}),
      ...(legend !== undefined ? { legend } : {}),
    };
    const backgroundMenu = this.backgroundMenu(this.defaultViewpoint?.[0]);
    const dragging = this.dragging(elementTypes);
    const background = this.axisBackground();
    const dragBounds = this.dragBounds();
    const drops = this.bindings.toolboxDrops === "module" ? [] : this.toolboxDrops();

    const definition: DiagramDefinition = {
      elementTypes,
      relationTypes,
      ...(acyclic.length > 0 ? { acyclic } : {}),
      ...(actions.length > 0 ? { actions } : {}),
      ...(snap !== undefined ? { snap } : {}),
      ...(Object.keys(chrome).length > 0 ? { chrome } : {}),
      ...(filter !== undefined ? { filter } : {}),
      layout: this.layout(rulers, backgroundMenu, dragging),
      dragging,
      ...(backgroundMenu ? { backgroundMenu } : {}),
      ...(drops.length > 0 ? { toolbox: { drops } } : {}),
      ...(this.connectsOnRightDrag() ? { connectOnRightDrag: true } : {}),
      ...(this.area !== undefined ? { extent: this.area.extent } : {}),
      ...(dragBounds !== undefined ? { dragBounds } : {}),
      ...(background !== undefined ? { background } : {}),
    };

    return { ...definition, ...this.bindings.extras?.(spec) };
  }

  /** The default system's x and y axes, by name where they are named. */
  private systemAxes(): { name: string; axis: DislAxis; across: boolean }[] {
    const system = this.system;
    if (system === undefined) {
      return [];
    }
    return (["x", "y"] as const).flatMap((end) => {
      const axis = system[end];
      return axis === undefined ? [] : [{ name: typeof axis === "string" ? axis : end, axis: this.axis(axis), across: end === "x" }];
    });
  }

  /**
   * A bounded canvas's axes along its edges (DISL §5.3): each axis's named ranges as bands across the
   * extent, from its `from` to its `to`, with a rule at a range's start where it states a separator; and
   * each axis with a title or end labels drawn along its edge, the vertical one first, its labels by the
   * end of the domain they stand at. Ranges need the bounds to be drawn in; an axis along no bounds is the
   * rulers' to draw, and is left to them.
   */
  private axisBackground(): BackgroundDeclaration | undefined {
    const chrome = this.bindings.axisChrome;
    const area = this.area;
    const typography = chrome?.typography;
    const bands: BandDeclaration[] = [];
    for (const { name, axis, across } of this.systemAxes()) {
      const ranges = axis.ranges ?? [];
      if (ranges.length > 0 && area === undefined) {
        fail(`axis "${name}" names ranges, and the canvas states no bounds to draw them in.`);
      }
      ranges.forEach((range, index) => {
        if (range.labelPosition !== undefined && range.labelPosition !== "center") {
          fail(`range "${range.id}" of axis "${name}" puts its label at the ${range.labelPosition}; the library centres a range's label in its band.`);
        }
        const at = (value: number) =>
          across ? (area!.toCanvas(value, 0).x - area!.extent.x) / area!.extent.width : (area!.toCanvas(0, value).y - area!.extent.y) / area!.extent.height;
        const [start, end] = [at(range.from), at(range.to)].sort((one, other) => one - other) as [number, number];
        const className = chrome?.rangeClassName?.(name, range, index);
        bands.push({
          orientation: across ? "vertical" : "horizontal",
          start,
          end,
          ...(range.label !== undefined ? { label: this.literal(range.label, `range "${range.id}" of axis "${name}" label`) } : {}),
          ...(range.separator !== undefined ? { edge: true } : {}),
          ...(className !== undefined ? { className } : {}),
          ...(typography !== undefined ? { typography } : {}),
        });
      });
    }

    const axes: AxisDeclaration[] = area === undefined ? [] : [...this.systemAxes()].reverse().flatMap(({ name, axis, across }) => {
      if (axis.label === undefined && axis.endLabels === undefined) {
        return [];
      }
      // The end the axis starts from - the left, or the top - is its minimum unless it runs the other way.
      const flipped = across ? axis.reversed === true : (axis.reversed === true) !== (this.system?.orientation === "y-up");
      const [first, last] = flipped ? [axis.endLabels?.max, axis.endLabels?.min] : [axis.endLabels?.min, axis.endLabels?.max];
      const where = `axis "${name}"`;
      return [{
        orientation: across ? "horizontal" as const : "vertical" as const,
        ...(axis.label !== undefined ? { title: this.literal(axis.label, `${where} label`) } : {}),
        ...(first !== undefined ? { startLabel: this.literal(first, `${where} end label`) } : {}),
        ...(last !== undefined ? { endLabel: this.literal(last, `${where} end label`) } : {}),
        ...(chrome?.axisClassName !== undefined ? { className: chrome.axisClassName } : {}),
        ...(typography !== undefined ? { typography } : {}),
      }];
    });

    if (bands.length === 0 && axes.length === 0) {
      return undefined;
    }
    return {
      ...(chrome?.className !== undefined ? { className: chrome.className } : {}),
      ...(bands.length > 0 ? { bands } : {}),
      ...(axes.length > 0 ? { axes } : {}),
    };
  }

  /**
   * Where a drag stops (DISL §5.3 `outOfRange.drag: "clamp"`): at the bounds, which the axes span from their
   * minimum to their maximum, where both axes clamp a drag. One axis alone is refused: the library stops a
   * drag at a box.
   */
  private dragBounds(): ShapeBounds | undefined {
    const axes = this.systemAxes();
    const clamps = axes.map(({ axis }) => axis.outOfRange?.drag === "clamp");
    if (!clamps.some(Boolean)) {
      return undefined;
    }
    if (axes.length < 2 || !clamps.every(Boolean)) {
      const [clamped, free] = clamps[0] ? ["x", "y"] : ["y", "x"];
      fail(`the coordinate system "${this.systemName}" clamps a drag on its ${clamped} axis and not on its ${free} axis; the library clamps a drag at the bounds on both or neither.`);
    }
    if (this.area === undefined) {
      fail(`the coordinate system "${this.systemName}" clamps a drag at its axes' bounds, and the canvas states no bounds to clamp it in.`);
    }
    return this.area.area;
  }

  /**
   * The toolbox entries that mean something only on an element (DISL 0.3 §7.2 `drop.targets`): each
   * operation target, its `on` read as the concrete types it stands for, and `elsewhere` as the
   * refusal or the silence the library applies before anything is sent. What the library cannot run
   * there - another kind of target, a drop on the canvas, a create or a menu elsewhere, a computed
   * sentence - is refused rather than left to a module.
   */
  private toolboxDrops(): ToolboxDropDeclaration[] {
    const tools = (this.spec.toolbox?.groups ?? []).flatMap((group) => group.tools).filter((tool) => (tool.drop?.targets ?? []).length > 0 && tool.drop?.place === undefined);
    return tools.map((tool): ToolboxDropDeclaration => {
      const drop = tool.drop!;
      const targets = drop.targets!.map((target) => {
        if (target.kind !== "operation" || target.operation === undefined) {
          fail(`tool "${tool.id}" drops a "${target.kind}" target; the library runs only an operation on what a drop lands on.`);
        }
        if (target.on.includes("canvas")) {
          fail(`tool "${tool.id}" drops on the canvas; the library runs a drop's target on an element only.`);
        }
        return { elementTypes: target.on.flatMap((name) => this.concreteTypes(name)).map((name) => this.typeId(name)), actionId: this.wireAction(target.operation, `the drop operation "${target.operation}" of tool "${tool.id}"`) };
      });
      const elsewhere = drop.elsewhere ?? "create";
      if (elsewhere !== "refuse" && elsewhere !== "ignore") {
        fail(`tool "${tool.id}" does "${elsewhere}" with a drop elsewhere; the library refuses or ignores one.`);
      }
      if (elsewhere === "refuse" && typeof drop.refusal !== "string") {
        fail(`tool "${tool.id}" refuses a drop elsewhere with a sentence that is not text; the library shows a stated sentence only.`);
      }
      const payload = this.wire.tools?.[tool.id]?.drop ?? (this.bindings.wireIds === undefined ? tool.id : fail(`tool "${tool.id}" has no drop id in ${this.bindings.wireIds}.tools.`));
      return { payload, targets, ...(elsewhere === "refuse" ? { refusal: drop.refusal as string } : {}) };
    });
  }

  /** An operation's wire id: its entry in the wire block's actions, or its own name where the module states no block. */
  private wireAction(operation: string, what: string): string {
    return this.wire.actions?.[operation] ?? (this.bindings.wireIds === undefined ? operation : fail(`${what} has no wire id in ${this.bindings.wireIds}.actions.`));
  }

  /**
   * The canvas's sentence for a document with nothing to draw (DISL §6.13 `canvas.empty`), as its
   * chrome. Its `when` is CEL over the diagram, which the library cannot evaluate: the module draws
   * the sentence once its document is read and holds nothing, which is that condition's ordinary case.
   */
  private emptyCanvas(): { text: { template: string } } | undefined {
    const empty = this.spec.notation.canvas?.empty;
    if (empty === undefined) {
      return undefined;
    }

    if (/[{}]/.test(empty.text)) {
      fail(`the canvas's empty sentence "${empty.text}" holds a brace, which the library would read as a {placeholder}.`);
    }
    return { text: { template: empty.text } };
  }

  /**
   * The canvas's title (DISL §6.13 `canvas.title`), drawn above the drawing surface and bound against the
   * model's background, which carries the diagram's attributes. The library draws it and never edits it,
   * so an editable title is refused.
   */
  private canvasTitle(): ChromeTextDeclaration | undefined {
    const title = this.spec.notation.canvas?.title;
    if (title === undefined) {
      return undefined;
    }

    if (title.editable === true) {
      fail("the canvas's title is editable; the library draws a title and edits none.");
    }
    const stated = this.bindings.canvasChrome?.title;
    return {
      text: this.binding(title.text, "the canvas's title"),
      ...(stated?.className !== undefined ? { className: stated.className } : {}),
      ...(stated?.data !== undefined ? { data: stated.data } : {}),
    };
  }

  /**
   * The canvas's header band (DISL §6.13 `canvas.header`): its rows, each a label bound against the
   * model's background, which carries the diagram's attributes, and drawn while its `visible` holds. The
   * library draws a band and edits none, so an editable row is refused.
   */
  private canvasHeader(): ChromeBandDeclaration | undefined {
    const header = this.spec.notation.canvas?.header;
    if (header === undefined) {
      return undefined;
    }

    const stated = this.bindings.canvasChrome?.header;
    const rows = header.rows.map((row, index): ChromeTextDeclaration => {
      const where = `the canvas's header row "${row.id ?? index}"`;
      if (row.editable === true || row.editable === "inline" || row.editable === "multiline") {
        fail(`${where} is editable; the library draws a header and edits none.`);
      }
      const when = this.labelVisibility(row.visible, where);
      const className = row.id === undefined ? undefined : stated?.rowClassName?.(row.id);
      return {
        text: typeof row.text === "string" ? this.literal(row.text, where) : this.binding(row.text, where),
        ...(when !== undefined ? { when } : {}),
        ...(className !== undefined ? { className } : {}),
      };
    });
    const when = header.visible === undefined || header.visible === true ? undefined : this.labelVisibility(header.visible as DislLabel["visible"], "the canvas's header");
    return {
      rows,
      ...(when !== undefined ? { when } : {}),
      ...(stated?.className !== undefined ? { className: stated.className } : {}),
      ...(stated?.data !== undefined ? { data: stated.data } : {}),
    };
  }

  /**
   * The canvas's notices (DISL §6.13): each budget's that cuts what is drawn (§3.2.1) and has a notice,
   * replaced by a declared notice of the id `budget:<id>`. When a budget's notice shows is the module's
   * condition ({@link NotationBindings.canvasChrome}), since only its backend knows that the budget cut.
   * A declared notice of any other id is refused: the library draws a budget's notice and no other.
   */
  private notices(): ChromeNoticeDeclaration[] {
    const declared = new Map((this.spec.notation.canvas?.notices ?? []).map((notice) => [notice.id, notice]));
    for (const id of declared.keys()) {
      if (!id.startsWith("budget:")) {
        fail(`the canvas's notice "${id}" is no budget's; the library draws a budget's notice and no other.`);
      }
    }

    return Object.entries(this.spec.language.limits?.budgets ?? {}).flatMap(([name, budget]): ChromeNoticeDeclaration[] => {
      const id = `budget:${name}`;
      // A budget truncates unless it says otherwise, or it measures with CEL (DISL §3.2.1).
      const truncates = budget.truncate ?? !(isObject(budget.measure) && typeof budget.measure.cel === "string");
      const notice = declared.get(id) ?? (budget.notice === false ? undefined : budget.notice === true || budget.notice === undefined ? {} : budget.notice);
      if (!truncates || notice === undefined) {
        return [];
      }

      const where = `the canvas's notice "${id}"`;
      if (notice.text === undefined) {
        fail(`${where} states no text; the library has no sentence of its own for a budget.`);
      }
      if (notice.visible !== undefined) {
        fail(`${where} narrows when it shows; the library shows a budget's notice on the module's condition alone.`);
      }
      const stated = this.bindings.canvasChrome?.notices?.[id];
      if (stated?.when === undefined) {
        fail(`${where} shows while the budget cuts what is drawn; the library needs the module's condition for that in canvasChrome.notices.`);
      }
      return [{
        id,
        text: typeof notice.text === "string" ? this.literal(notice.text, where) : this.binding(notice.text, where),
        severity: notice.severity ?? "info",
        position: notice.position ?? "top",
        when: stated.when,
        ...(stated.className !== undefined ? { className: stated.className } : {}),
        ...(stated.data !== undefined ? { data: stated.data } : {}),
      }];
    });
  }

  /**
   * The canvas's computed key (DISL §6.13 `canvas.legend.computed`), drawn below the drawing surface: one
   * entry per item of the list the module names, its caption and its swatch's colour each the path within
   * the item that the key's CEL stands for ({@link NotationBindings.rowPaths}). A key drawn anywhere but
   * below the surface is refused, as is one the module gives no list for.
   */
  private computedLegend(): ChromeLegendDeclaration | undefined {
    const legend = this.spec.notation.canvas?.legend;
    if (legend?.computed === undefined || legend.visible === false) {
      return undefined;
    }

    if ((legend.position ?? "outside-bottom") !== "outside-bottom") {
      fail(`the canvas's key is drawn "${legend.position}"; the library draws a computed key below the drawing surface.`);
    }
    const stated = this.bindings.canvasChrome?.legend ?? fail("the canvas's key is computed over what is drawn; the library needs the list of entries the module names in canvasChrome.legend.");
    const fill = legend.computed.swatch?.fill;
    return {
      entries: { path: stated.entries, each: { path: this.rowPath(legend.computed.label, "the canvas's key label") } },
      ...(fill !== undefined ? { swatchColour: { path: this.rowPath(fill, "the canvas's key swatch") } } : {}),
      ...(legend.title !== undefined ? { title: legend.title } : {}),
      ...(stated.className !== undefined ? { className: stated.className } : {}),
      ...(stated.titleClassName !== undefined ? { titleClassName: stated.titleClassName } : {}),
      ...(stated.entryClassName !== undefined ? { entryClassName: stated.entryClassName } : {}),
      ...(stated.swatchClassName !== undefined ? { swatchClassName: stated.swatchClassName } : {}),
      ...(stated.data !== undefined ? { data: stated.data } : {}),
    };
  }

  /**
   * Whether a connection is drawn with the right button from a node's body: the edges' `connect.pointer`
   * (DISL 0.3 §6.10). The library has one switch for the whole canvas, so every edge that states a pointer
   * must state that one; any other pointer is refused.
   */
  private connectsOnRightDrag(): boolean {
    const pointers = Object.entries(this.spec.notation.edges ?? {}).flatMap(([name, edge]) => (edge.connect?.pointer === undefined ? [] : [[name, edge.connect.pointer] as const]));
    for (const [name, pointer] of pointers) {
      if (pointer.button !== "secondary" || pointer.start !== "body" || (pointer.modifiers ?? []).length > 0) {
        fail(`edge "${name}" connects with the pointer ${JSON.stringify({ button: pointer.button, start: pointer.start, modifiers: pointer.modifiers })}; the library connects only on a right drag from the body.`);
      }
    }
    return pointers.length > 0;
  }

  // ---- names --------------------------------------------------------------------------------

  private typeId(name: string): string {
    return (this.bindings.typeIds === "specification" ? undefined : this.wire.types?.[name]) ?? name.toLowerCase();
  }

  /** Refuses two of a kind of type under one id, which the canvas tells them apart by. */
  private distinctIds(kind: string, names: readonly string[]): void {
    const seen = new Map<string, string>();
    for (const name of names) {
      const id = this.typeId(name);
      const first = seen.get(id);
      if (first !== undefined) {
        fail(`${kind} "${first}" and "${name}" are both "${id}" on the canvas, which tells its types apart by id; a module that builds its own model names them by the specification (typeIds).`);
      }
      seen.set(id, name);
    }
  }

  /** The concrete node types a metamodel type name stands for: itself, or its descendants with a notation. */
  private concreteTypes(name: string): readonly string[] {
    return this.nodeNames.filter((node) => this.isA(node, name));
  }

  private isA(type: string, ancestor: string): boolean {
    for (let current: string | undefined = type; current !== undefined; current = this.spec.metamodel.types[current]?.extends) {
      if (current === ancestor) {
        return true;
      }
    }

    return false;
  }

  /** Whether a type, or a type it extends, may hold children (DISL §4.6 containment). */
  private holdsChildren(type: string): boolean {
    for (let current: string | undefined = type; current !== undefined; current = this.spec.metamodel.types[current]?.extends) {
      if (this.spec.metamodel.types[current]?.children !== undefined) {
        return true;
      }
    }

    return false;
  }

  // ---- bindings -----------------------------------------------------------------------------

  /** A Bindable text or number as the library's binding: an attribute's payload path, or a CEL's template. */
  private binding(value: unknown, where: string): Binding {
    if (isObject(value) && typeof value.attribute === "string") {
      return { path: `payload.${value.attribute}` };
    }

    if (isObject(value) && typeof value.cel === "string") {
      return this.celBinding(value.cel, where);
    }

    return fail(`${where} binds ${JSON.stringify(value)}, which is neither an attribute nor CEL.`);
  }

  /**
   * A CEL expression as a path or template over the payload: the module's `celPaths` for the whole
   * expression, or string literals, the module's `celPaths` and `self.<attribute>`, joined by `+`. Anything else is refused, naming the expression, because
   * the library cannot evaluate it and a guess would draw something the specification never said.
   */
  private celBinding(expression: string, where: string): Binding {
    // The whole expression first: one the module names is a value the payload carries, whatever
    // its terms, and splitting it at a `+` inside a choice would bind half of each branch.
    const whole = this.bindings.celPaths[expression];
    if (whole !== undefined) {
      return { path: whole };
    }

    // A count that declines its noun: `string(self.n) + (self.n == 1 ? ' one' : ' other')`.
    const count = /^string\(self\.([A-Za-z_][A-Za-z0-9_]*)\)\s*\+\s*\(\s*self\.\1\s*==\s*1\s*\?\s*' ([^'{}\\]+)'\s*:\s*' ([^'{}\\]+)'\s*\)$/.exec(expression.trim());
    if (count !== null) {
      return { path: `payload.${count[1]}`, plural: { one: count[2]!, other: count[3]! } };
    }

    const parts = concatenationTerms(expression).map((term): { text: string } | { path: string } => {
      const literal = /^'([^'{}]*)'$/.exec(term);
      if (literal !== null) {
        return { text: literal[1]! };
      }

      // The module's table first, as for the whole expression: a payload that carries one value for
      // what the specification reads from several attributes names it there.
      const path = this.bindings.celPaths[term];
      if (path !== undefined) {
        return { path };
      }

      const attribute = /^self\.([A-Za-z_][A-Za-z0-9_]*)$/.exec(term);
      if (attribute !== null) {
        return { path: `payload.${attribute[1]}` };
      }

      return fail(`${where}: the CEL term "${term}" (in "${expression}") has no payload path in the module's celPaths.`);
    });

    const only = parts[0]!;
    if (parts.length === 1 && "path" in only) {
      return { path: only.path };
    }

    return { template: parts.map((part) => ("path" in part ? `{${part.path}}` : part.text)).join("") };
  }

  // ---- element types ------------------------------------------------------------------------

  private elementType(name: string, node: DislNodeNotation): ElementTypeDefinition {
    const where = `node "${name}"`;
    const { shape, segments, cornerRadius } = this.shape(name, node);
    const bodyless = shape === "none";
    const classNames = [...this.bindings.classNames(name, node), ...this.indexedColours(name, node)];
    const hint = this.showsDragValue() && this.movable(node) ? [this.dragHint()] : [];
    // A `text` shape is only its text (Appendix B.2): placed by an edge, its text starts or ends there.
    const placedEdge = node.placement?.anchor === "left" ? "start" : node.placement?.anchor === "right" ? "end" : undefined;
    const textEdge = shapeNameOf(node.shape).name === "text" ? placedEdge : undefined;
    const labels = [...(node.labels ?? []).map((label) => this.label(name, label, bodyless, textEdge)), ...hint];
    const compartments = this.compartments(name, node);
    const { links, decorations: badges } = this.badges(name, node);
    const decorations = [...this.icon(name, node, bodyless), ...badges, ...(this.bindings.elementDecorations?.(name, node) ?? [])];
    const data = this.bindings.elementData?.(name, node);
    const centreOf = this.centreOf(name, node);
    const { sizing, resize } = this.sizing(node);
    const editOnDrop = (this.spec.toolbox?.groups ?? []).some((group) => group.tools.some((tool) => tool.creates === name && tool.after === "editLabel"));
    const boundStyle = this.bindings.boundStyle?.(name, node);
    const style = { ...(cornerRadius !== undefined ? { cornerRadius } : {}), ...this.notationStyle(node) };
    const dragAxis = this.dragAxis(node);

    return {
      id: this.typeId(name),
      shape,
      ...(Object.keys(style).length > 0 ? { style } : {}),
      ...(boundStyle !== undefined ? { boundStyle } : {}),
      ...(classNames.length > 0 ? { classNames } : {}),
      ...(data !== undefined ? { data } : {}),
      ...(labels.length > 0 ? { labels } : {}),
      ...(decorations.length > 0 ? { decorations } : {}),
      ...(compartments.length > 0 ? { compartments } : {}),
      ...(links.length > 0 ? { links } : {}),
      ...(node.tooltip !== undefined ? { tooltip: this.binding(node.tooltip, `${where} tooltip`) } : {}),
      ...this.accessibilityOf(name, node, where),
      anchors: this.anchors(name, node),
      sizing,
      ...(resize !== undefined ? { resize } : {}),
      ...(this.movable(node) ? {} : { draggable: false }),
      ...(dragAxis !== undefined ? { dragAxis } : {}),
      ...(node.deletable === false ? { deletable: false } : {}),
      // Not picked (DISL §6.9): a press on it is a press on what lies under it.
      ...(node.selectable === false ? { selectable: false } : {}),
      // The background layer (DISL 6.16) is beneath the connections, so a container's arrows stay over it.
      ...(node.layer === "background" ? { beneathConnections: true } : {}),
      ...(editOnDrop ? { editOnDrop } : {}),
      ...(segments !== undefined ? { segments } : {}),
      ...(centreOf !== undefined ? { centreOf } : {}),
    };
  }

  /**
   * A node's colours chosen by index (DISL §6.6: a token named from an index wrapped by `%`), each as
   * the class its family's slot is painted by: the module's prefix and the index modulo the count, as
   * the binding's `number.modulo` wraps it. One class per family, however many of the style's colours
   * read it - a hue and its tint are one slot.
   */
  private indexedColours(name: string, node: DislNodeNotation): ClassDeclaration[] {
    const found = new Map<string, ClassDeclaration>();
    for (const expression of celTextsIn(this.styleOf(node.style))) {
      for (const match of expression.matchAll(/token\('([^']+)' \+ string\(self\.([A-Za-z_][A-Za-z0-9_]*) % (\d+)\)\)/g)) {
        const [, token, attribute, count] = match as unknown as [string, string, string, string];
        const key = `${token}|${attribute}|${count}`;
        if (found.has(key)) {
          continue;
        }

        const prefix = this.bindings.indexedColourClassName?.(name, token);
        if (prefix === undefined) {
          fail(`node "${name}" chooses its colour from "${token}" by an index; the library paints a colour chosen by index by the class the module names for the token's family in indexedColourClassName.`);
        }
        found.set(key, {
          className: { parts: [{ template: prefix }, { path: `payload.${attribute}`, number: { modulo: Number(count) } }], join: "" },
          on: "shape",
          when: { path: `payload.${attribute}`, is: "present" },
        });
      }
    }

    return [...found.values()];
  }

  /** A style as it is drawn: a named style of the notation or one written in place, its `extends` merged beneath it. */
  private styleOf(style: unknown, seen: readonly string[] = []): Readonly<Record<string, unknown>> | undefined {
    if (typeof style === "string") {
      if (seen.includes(style)) {
        fail(`the style "${style}" extends itself.`);
      }
      return this.styleOf(this.spec.notation.styles?.[style], [...seen, style]);
    }

    if (!isObject(style)) {
      return undefined;
    }

    const { extends: bases, ...own } = style;
    const inherited = (Array.isArray(bases) ? bases : bases === undefined ? [] : [bases])
      .map((base) => this.styleOf(base, seen))
      .reduce<Readonly<Record<string, unknown>> | undefined>((merged, base) => (merged === undefined ? base : mergedPerKey(merged, base)), undefined);
    return inherited === undefined ? own : mergedPerKey(inherited, own);
  }

  /**
   * A node's icon (DISL §6.9), drawn by the library's own glyph for its name. A body-less node has no
   * box to put it at the top of: it stands above the node's point, its lowest edge {@link ICON_GAP}
   * above it, with the node's last line of text on the point itself. `flip: 'horizontal'` mirrors it
   * while the condition it is chosen by holds. Its colour is a style, which the stylesheet paints.
   */
  private icon(name: string, node: DislNodeNotation, bodyless: boolean): DecorationDeclaration[] {
    const icon = node.icon;
    if (icon === undefined) {
      return [];
    }

    const where = `node "${name}" icon "${icon.icon}"`;
    const glyph = ICONS[icon.icon] ?? fail(`${where} is no icon the library draws (it draws ${Object.keys(ICONS).join(", ")}).`);
    if (!bodyless || (icon.position ?? "top") !== "top") {
      fail(`${where} sits "${String(icon.position ?? "top")}" on a node ${bodyless ? "" : "with a body "}; the library draws an icon above a body-less node's point.`);
    }

    const radius = (icon.size ?? ICON_SIZE) / 2;
    const mirrored = icon.flip === undefined ? undefined : this.flipCondition(icon.flip, where);
    const className = this.bindings.iconClassName?.(name, icon);
    return [{
      glyph,
      from: { x: 0, y: -(radius + ICON_GAP) },
      radius,
      markerEnd: "arrow",
      ...(className !== undefined ? { className } : {}),
      ...(mirrored !== undefined ? { mirrored } : {}),
    }];
  }

  /** The condition an icon's `flip` mirrors it under: `<condition> ? 'horizontal' : 'none'`. */
  private flipCondition(flip: unknown, where: string): Condition | undefined {
    if (flip === "none") {
      return undefined;
    }

    const expression = isObject(flip) && typeof flip.cel === "string" ? flip.cel.trim() : undefined;
    const ternary = expression === undefined ? null : /^(.+?)\s*\?\s*'horizontal'\s*:\s*'none'$/.exec(expression);
    if (ternary === null) {
      return fail(`${where} flips by ${JSON.stringify(flip)}; the library mirrors an icon left to right while one condition holds.`);
    }

    return this.condition(ternary[1]!.trim(), `${where} flip`);
  }

  /**
   * Where a node with no position of its own is drawn (DISL §6.9 placement): the mean of the centres of
   * the members one attribute lists, `self.<members>.map(m, m.view.bounds.center()[0]).avg()` and its
   * `[1]` - the members the payload lists under the attribute's name. A CEL the module's celPaths name
   * `element.x` and `element.y` is the position the backend computed, which the model carries. Any other
   * CEL is refused; a placement by attribute or by the layout is not this one's.
   */
  private centreOf(name: string, node: DislNodeNotation): string | undefined {
    const { x, y } = node.placement ?? {};
    // An attribute or the layout places a node as the other placements do; only a CEL is read here.
    const computed = (value: unknown) => isObject(value) && typeof value.cel === "string";
    if (!computed(x) && !computed(y)) {
      return undefined;
    }

    // A position the backend computes arrives as the element's own, where the module's celPaths say so.
    // So does a coordinate an attribute holds, which the model carries as the element's own as well.
    const byAttribute = (value: unknown) => isObject(value) && typeof value.attribute === "string";
    const own = (value: unknown, axis: "x" | "y") => byAttribute(value) || (isObject(value) && typeof value.cel === "string" && this.bindings.celPaths[value.cel] === `element.${axis}`);
    if (own(x, "x") && own(y, "y")) {
      return undefined;
    }
    for (const [value, other, axis] of [[x, y, "x"], [y, x, "y"]] as const) {
      if (byAttribute(other) && !own(value, axis)) {
        fail(`node "${name}" places its ${axis} at "${String((value as { cel?: unknown }).cel)}", which has no entry element.${axis} in the module's celPaths; beside an attribute the library draws a coordinate the backend computes.`);
      }
    }

    const memberList = (value: unknown, index: number): string | null => {
      const expression = isObject(value) && typeof value.cel === "string" ? value.cel.replace(/\s+/g, "") : "";
      const match = /^self\.([A-Za-z_][A-Za-z0-9_]*)\.map\(([A-Za-z_][A-Za-z0-9_]*),\2\.view\.bounds\.center\(\)\[(\d)\]\)\.avg\(\)$/.exec(expression);
      return match !== null && Number(match[3]) === index ? match[1]! : null;
    };
    const across = memberList(x, 0);
    if (across === null || across !== memberList(y, 1)) {
      return fail(`node "${name}" is placed at ${JSON.stringify({ x, y })}; the library places a node at the centre of the members one attribute lists.`);
    }

    return `payload.${across}`;
  }

  /**
   * A CEL condition as the library's: the module's `celConditions`, else `self.<attribute>` (true),
   * `!self.<attribute>` (false) or `self.<attribute> == '<text>'`. Anything else is refused, naming it.
   */
  private condition(expression: string, where: string): Condition {
    const bound = this.bindings.celConditions?.[expression];
    if (bound !== undefined) {
      return bound;
    }

    const flag = /^(!?)self\.([A-Za-z_][A-Za-z0-9_]*)$/.exec(expression);
    if (flag !== null) {
      return { path: `payload.${flag[2]}`, is: flag[1] === "!" ? "false" : "true" };
    }

    const equals = /^self\.([A-Za-z_][A-Za-z0-9_]*)\s*==\s*'([^'\\]*)'$/.exec(expression);
    if (equals !== null) {
      return { path: `payload.${equals[1]}`, equals: equals[2]! };
    }

    return fail(`${where}: the condition "${expression}" has no entry in the module's celConditions.`);
  }

  /** A `visible` as a condition, or none where it is always drawn. */
  private visibility(visible: boolean | string | { cel: string } | { attribute: string } | undefined, where: string): Condition | undefined {
    if (visible === undefined || visible === true) {
      return undefined;
    }
    if (isObject(visible) && "attribute" in visible) {
      // A bool attribute of the element itself: shown while it is true (DISL §6.9).
      return { path: `payload.${visible.attribute}`, is: "true" };
    }

    const expression = typeof visible === "string" ? visible : isObject(visible) ? visible.cel : undefined;
    return expression === undefined ? fail(`${where} is never visible.`) : this.condition(expression.trim(), `${where} visible`);
  }

  private shape(name: string, node: DislNodeNotation): { shape: BuiltInShape; segments?: SegmentDeclaration; cornerRadius?: number } {
    const { name: shapeName, params } = shapeNameOf(node.shape);
    const custom: DislCustomShape | undefined = this.spec.notation.shapes?.[shapeName];
    const drawnAs = custom === undefined ? this.bindings.builtInShapes?.[shapeName] : undefined;
    if (drawnAs !== undefined) {
      // A rounded rectangle is drawn with the corner the reference asks for (DISL Appendix B.2, `radius`).
      const radius = params.radius;
      return drawnAs === "rounded-rectangle" && typeof radius === "number" ? { shape: drawnAs, cornerRadius: radius } : { shape: drawnAs };
    }

    const builtIn = libraryShapeOf(node.shape);
    if (builtIn !== undefined) {
      return { shape: builtIn };
    }

    const binding = this.bindings.customShapes?.[shapeName];
    if (custom === undefined || binding === undefined) {
      return fail(`node "${name}" is drawn as "${shapeName}", which is no built-in the library draws and no custom shape the module binds.`);
    }

    const segments = binding.segments?.({ shape: custom, params, bind: (value) => this.binding(value, `node "${name}" shape parameter`) });
    return segments !== undefined ? { shape: binding.shape, segments } : { shape: binding.shape };
  }

  private label(type: string, label: DislLabel, bodyless = false, textEdge?: "start" | "end"): LabelDeclaration {
    const where = `node "${type}" label "${label.id}"`;
    // A label's text is a Bindable: a literal string is shown as it is written (DISL §2.5b).
    const declaration: Record<string, unknown> = { text: typeof label.text === "string" ? this.literal(label.text, where) : this.binding(label.text, where) };

    const position = label.position;
    if (position === undefined && textEdge !== undefined) {
      // The text of a text shape placed by its edge, from that edge: the library's aligned line, with no inset.
      declaration.anchorTo = "centre";
      declaration.align = textEdge;
      declaration.insetX = 0;
    } else if (bodyless && position === "bottom") {
      // A body-less node has no box to write at the bottom of: its last line is on its point, beneath its icon.
    } else if (position === "outside-left") {
      if (label.distance !== undefined && label.distance !== BEFORE_GAP) {
        fail(`${where} sits ${label.distance} before its node; the library's before label sits ${BEFORE_GAP}.`);
      }
      declaration.placement = "before";
    } else if (position === "outside-right") {
      if (label.distance !== undefined) {
        fail(`${where} sits ${label.distance} beside its node; the library's beside label sits at its own gap.`);
      }
      declaration.placement = "beside";
    } else if (position === "outside-top" || position === "outside-bottom") {
      // Centred over or under the node, at the library's own gap from its edge.
      const side = position === "outside-top" ? "above" : "below";
      if (label.distance !== undefined) {
        fail(`${where} sits ${label.distance} ${side} its node; the library's ${side} label sits at its own gap.`);
      }
      declaration.placement = side;
    } else if (isObject(position)) {
      const [ax, ay] = position.anchor as readonly [number, number];
      const anchorTo = ay === 0 ? "top" : ay === 0.5 ? "centre" : ay === 1 ? "bottom" : undefined;
      // A label anchored at the left or right edge is the library's aligned line: its end at the edge,
      // inset by the offset's distance inward, which is what DISL's `align` at that anchor draws.
      const edge = ax === 0 ? "start" : ax === 1 ? "end" : undefined;
      if (anchorTo === undefined || (ax !== 0.5 && edge === undefined)) {
        fail(`${where} anchors at ${JSON.stringify(position.anchor)}; the library anchors a label at the top, centre or bottom of the middle or of an edge.`);
      }
      // A line aligned away from its edge runs out beyond it: the library's aligned line from the opposite
      // edge, inset across the node, which only a node of a stated width can say.
      const across = edge === undefined ? undefined : edge === "start" ? "end" : "start";
      const width = across !== undefined && position.align === across ? this.statedWidth(type) : undefined;
      if (edge !== undefined && position.align !== edge && width === undefined) {
        fail(`${where} anchors at its ${edge === "start" ? "left" : "right"} edge ${position.align === undefined ? "with no align" : `aligned "${position.align}"`}; the library aligns such a line "${edge}", or "${across}" beyond the edge of a node of a stated width.`);
      }
      declaration.anchorTo = anchorTo;
      const stated = position.offset;
      // An offset the element carries (DISL §6.9 Point, bindable): its [dx, dy] read from the payload at draw time.
      const bound = isObject(stated) && !Array.isArray(stated) ? (stated as { cel?: string; attribute?: string }) : undefined;
      if (bound !== undefined && edge !== undefined) {
        fail(`${where} anchors at its ${edge === "start" ? "left" : "right"} edge at an offset its element binds; the library binds the offset of a label anchored in the middle.`);
      }
      const offset = bound === undefined ? (stated as readonly [number, number] | undefined) : undefined;
      if (bound !== undefined) {
        declaration.offsetBinding = typeof bound.attribute === "string" ? { path: `payload.${bound.attribute}` } : { path: this.bindings.celPaths[bound.cel ?? ""] ?? fail(`${where} is offset by "${String(bound.cel)}", which has no payload path in the module's celPaths.`) };
      }
      if (edge === undefined && (position.align === "start" || position.align === "end")) {
        // Aligned from its point rather than centred on it: the text starts, or ends, at the anchor moved by the offset.
        declaration.textAnchor = position.align;
      }
      if (edge !== undefined && width !== undefined) {
        declaration.align = across;
        declaration.insetX = width + (edge === "end" ? (offset?.[0] ?? 0) : -(offset?.[0] ?? 0));
        declaration.offset = { x: 0, y: offset?.[1] ?? 0 };
      } else if (edge !== undefined) {
        declaration.align = edge;
        declaration.insetX = edge === "start" ? (offset?.[0] ?? 0) : -(offset?.[0] ?? 0);
        declaration.offset = { x: 0, y: offset?.[1] ?? 0 };
      } else if (offset !== undefined) {
        declaration.offset = { x: offset[0], y: offset[1] };
      }
    } else if (position === "top" || position === "bottom") {
      // Inside, on the first or the last line of the node (DISL 0.4 §6.9).
      declaration.anchorTo = position;
      declaration.offset = { x: 0, y: position === "top" ? LABEL_LINE : -LABEL_LINE / 2 };
    } else if (typeof position === "string" && /^(top|bottom)-(left|right)$/.test(position)) {
      // Inside, at a corner: on the first or the last line, aligned to that side at the library's inset.
      const [vertical, side] = position.split("-") as ["top" | "bottom", "left" | "right"];
      declaration.anchorTo = vertical;
      declaration.align = side === "left" ? "start" : "end";
      declaration.offset = { x: 0, y: vertical === "top" ? LABEL_LINE : -LABEL_LINE / 2 };
    } else if (position !== undefined && position !== "center") {
      fail(`${where} is positioned "${String(position)}", which the library has no placement for.`);
    }

    if (label.editable === true || label.editable === "inline" || label.editable === "multiline") {
      declaration.editable = true;
    }
    if (label.wrap === "word") {
      declaration.wrap = true;
    } else if (label.overflow === "ellipsis") {
      declaration.truncate = true;
    }
    if (this.bindings.labelFit === "style" && (declaration.wrap === true || declaration.truncate === true)) {
      Object.assign(declaration, this.styledFit(type, label, where));
    }
    if (label.tooltip !== undefined) {
      declaration.tooltip = this.binding(label.tooltip, `${where} tooltip`);
    }
    const typography = this.typography(type, label, where);
    if (typography !== undefined) {
      declaration.typography = typography;
    }
    const when = this.labelVisibility(label.visible, where);
    if (when !== undefined) {
      declaration.when = when;
    }

    const className = this.bindings.labelClassName?.(type, label);
    if (className !== undefined) {
      declaration.className = className;
    }
    const underline = this.bindings.labelUnderline?.(type, label);
    if (underline !== undefined) {
      declaration.underline = underline;
    }
    const editorBox = this.bindings.labelEditorBox?.(type, label);
    if (editorBox !== undefined) {
      declaration.editorBox = editorBox;
    }

    return declaration as unknown as LabelDeclaration;
  }

  /** A node's width where it states one that nothing changes: a number, or the first of a fixed size. */
  private statedWidth(type: string): number | undefined {
    const size = this.spec.notation.nodes[type]?.size;
    if (size?.fixed !== undefined) {
      return size.fixed[0];
    }

    return typeof size?.width === "number" && (size.resizable === undefined || size.resizable === false || size.resizable === "vertical") ? size.width : undefined;
  }

  /**
   * A cut or wrapped label's fit at the size its style draws it ({@link NotationBindings.labelFit}): its
   * style's font size, inside its node style's horizontal padding (`padding: [vertical, horizontal]`, or
   * one number for both), and a wrapped label's lines its font size times its style's line height apart.
   * A style that states no font size is refused, because the fit would be a guess.
   */
  private styledFit(type: string, label: DislLabel, where: string): Pick<LabelDeclaration, "fit" | "stack"> {
    const font = this.styleOf(label.style)?.font;
    const fontSize = isObject(font) && typeof font.size === "number" ? font.size : fail(`${where} is fitted to its node by its style, which states no font size.`);
    const padding = this.styleOf(this.spec.notation.nodes[type]?.style)?.padding;
    const inset = typeof padding === "number" ? padding : Array.isArray(padding) && typeof padding[1] === "number" ? padding[1] : 0;
    const lineHeight = isObject(font) && typeof font.lineHeight === "number" ? font.lineHeight : undefined;
    return {
      fit: { fontSize, inset },
      ...(label.wrap === "word" && lineHeight !== undefined ? { stack: { lineHeight: Math.round(fontSize * lineHeight * 100) / 100 } } : {}),
    };
  }

  /**
   * A label's `visible` as the library's `when`: a text the label binds being non-empty
   * (`<term> != ''`, the term a payload path as {@link celBinding} reads it). Any other condition is
   * refused, naming it; a label without one, or visible `true`, is always drawn.
   */
  private labelVisibility(visible: DislLabel["visible"], where: string): Condition | undefined {
    if (visible === undefined || visible === true) {
      return undefined;
    }

    const expression = typeof visible === "string" ? visible : isObject(visible) && typeof visible.cel === "string" ? visible.cel : undefined;
    const bound = expression === undefined ? undefined : this.bindings.celConditions?.[expression];
    if (bound !== undefined) {
      return bound;
    }

    const term = expression === undefined ? null : /^(.+?)\s*!=\s*''$/.exec(expression.trim());
    const binding = term === null ? undefined : this.celBinding(term[1]!.trim(), `${where} visible`);
    if (binding === undefined || !("path" in binding)) {
      return fail(`${where} is visible when ${JSON.stringify(visible)}; it has no condition in the module's celConditions, and the library draws a label otherwise only when a payload text is non-empty.`);
    }

    return { path: binding.path, is: "non-empty" };
  }

  /**
   * The node's accessibility: what it states, else - where the module asks for a role or a tab order -
   * DISL's default name (§6.15), the type's label and its label attribute, which a type inherits. A node
   * that states its role and no name is given that default name too, since the default is the name's.
   */
  private accessibilityOf(name: string, node: DislNodeNotation, where: string): Pick<ElementTypeDefinition, "accessibility"> {
    if (node.accessibility !== undefined) {
      const stated = this.accessibility(node, where);
      return { accessibility: node.accessibility.name !== undefined ? stated : { ...stated, label: this.defaultName(name, where) } };
    }

    if (this.bindings.accessibleRole === undefined && this.bindings.focusable !== true) {
      return {};
    }

    return {
      accessibility: {
        ...(this.bindings.accessibleRole !== undefined ? { role: this.bindings.accessibleRole } : {}),
        ...(this.bindings.focusable === true ? { focusable: true } : {}),
        label: this.defaultName(name, where),
      },
    } as Pick<ElementTypeDefinition, "accessibility">;
  }

  /** DISL's default accessible name (§6.15): the type's label and its label attribute, which a type inherits. */
  private defaultName(name: string, where: string): NonNullable<NonNullable<ElementTypeDefinition["accessibility"]>["label"]> {
    const label = this.spec.metamodel.types[name]?.label ?? name;
    let attribute: string | undefined;
    for (let current: string | undefined = name; current !== undefined && attribute === undefined; current = this.spec.metamodel.types[current]?.extends) {
      attribute = this.spec.metamodel.types[current]?.labelAttribute;
    }
    if (label.includes("'") || label.includes("{")) {
      fail(`${where}: the type label "${label}" cannot be written into an accessible name.`);
    }

    return attribute === undefined ? { template: label } : this.celBinding(`'${label} ' + self.${attribute}`, `${where} accessible name`);
  }

  private accessibility(node: DislNodeNotation, where: string): NonNullable<ElementTypeDefinition["accessibility"]> {
    const { role, name } = node.accessibility!;
    return {
      ...(role !== undefined ? { role } : {}),
      ...(this.bindings.focusable === true ? { focusable: true } : {}),
      ...(name !== undefined ? { label: this.binding(name, `${where} accessible name`) } : {}),
    } as NonNullable<ElementTypeDefinition["accessibility"]>;
  }

  /**
   * Where a line may attach. DISL never draws an anchor as a handle, so the library's handles are
   * hidden wherever its kind would draw them (`compass`, `along`); `edge` draws none to hide.
   */
  private anchors(name: string, node: DislNodeNotation): AnchorSet {
    if (node.connectable === false) {
      return { kind: "edge", enabled: false, visible: false };
    }

    const anchors = node.anchors;
    if (anchors?.mode === "fixed") {
      const points = anchors.points ?? [];
      const atCompass = points.every((point) => {
        const fraction = COMPASS[point.id as CompassPosition];
        return fraction !== undefined && fraction[0] === point.x && fraction[1] === point.y;
      });
      if (!atCompass && points.some((point) => COMPASS[point.id as CompassPosition] !== undefined)) {
        const misplaced = points.find((point) => COMPASS[point.id as CompassPosition] !== undefined)!;
        fail(`node "${name}" anchor "${misplaced.id}" at (${misplaced.x}, ${misplaced.y}) is no compass position the library has.`);
      }
      if (!atCompass) {
        return this.namedSideAnchors(name, points);
      }
      const positions = points.map((point) => point.id as CompassPosition);
      const drawnFrom = anchors.drawnFrom;
      return { kind: "compass", positions, ...(drawnFrom === "outline" ? { attachDrawnBy: "edge" as const } : {}), visible: false };
    }

    if (anchors?.mode === "outline") {
      // Anywhere on the outline, where a line from the other end's centre crosses it: the library's edge rule.
      return { kind: "edge" };
    }

    if (anchors?.mode === "sides" && anchors.spread === true) {
      // Spread along each side: an end sits anywhere along the edge, where the model attaches it.
      return { kind: "along", edges: [...(anchors.sides ?? ["top", "right", "bottom", "left"])], visible: false };
    }

    if (anchors?.mode === "sides") {
      const sides = [...(anchors.sides ?? ["top", "right", "bottom", "left"])].sort().join(",");
      const edgeSides = sides === "bottom,top" ? "vertical" : sides === "left,right" ? "horizontal" : sides === "bottom,left,right,top" ? undefined : fail(`node "${name}" attaches on ${sides}, which the library's edge anchors cannot narrow to.`);
      return edgeSides !== undefined ? { kind: "edge", edgeSides } : { kind: "edge" };
    }

    if (anchors !== undefined) {
      return fail(`node "${name}" anchors in mode "${String(anchors.mode)}", which this compiler does not map.`);
    }

    // No anchors of its own: an edge that attaches to a part of this node says where - anywhere along
    // the part's stretch of the sides its `side` attribute may name.
    const sides = new Set<string>();
    for (const [edgeName, edge] of Object.entries(this.spec.notation.edges ?? {})) {
      const relation = this.relation(edgeName);
      for (const end of ["source", "target"] as const) {
        const anchoring = edge.anchoring?.[end];
        if (anchoring?.mode !== "part" || !typeList(relation[end]).some((type) => this.isA(name, type))) {
          continue;
        }
        const side = anchoring.side;
        const sideAttribute = isObject(side) && "attribute" in side ? side.attribute : fail(`edge "${edgeName}" ${end} side must be bound to an attribute to say which sides of "${name}" it attaches to.`);
        const attribute = relation.attributes?.[sideAttribute];
        const values = attribute === undefined ? undefined : this.spec.metamodel.enums?.[attribute.type]?.values;
        Object.keys(values ?? fail(`edge "${edgeName}" ${end} side attribute "${sideAttribute}" is no enum of sides.`)).forEach((value) => sides.add(value));
      }
    }

    if (sides.size === 0) {
      // Nothing stated and nothing implied: DISL's default, a line attaching anywhere on the outline.
      return { kind: "edge", visible: false };
    }

    return { kind: "along", edges: [...sides] as ("top" | "bottom" | "left" | "right")[], regions: "segments", visible: false };
  }

  /**
   * Fixed anchors named for what they are rather than where (a timeline's `begin` and `end`): each a
   * named fraction of the side it lies on, so an edge's `connect.from` can name it. A line's other end
   * attaches only on the sides these lie on - the left and right of a notation read left to right.
   */
  private namedSideAnchors(name: string, points: readonly { id: string; x: number; y: number }[]): AnchorSet {
    const fractions = points.map((point): SideFraction => {
      // `at` runs clockwise round the bounds, as the library measures it.
      const side = point.x === 0 ? "left" : point.x === 1 ? "right" : point.y === 0 ? "top" : point.y === 1 ? "bottom" : undefined;
      if (side === undefined) {
        return fail(`node "${name}" anchor "${point.id}" at (${point.x}, ${point.y}) lies on no side of its bounds.`);
      }
      const at = side === "left" ? 1 - point.y : side === "right" ? point.y : side === "top" ? point.x : 1 - point.x;
      return { side, at, name: point.id };
    });
    const sides = new Set(fractions.map((fraction) => fraction.side));
    const horizontal = [...sides].every((side) => side === "left" || side === "right");
    const vertical = [...sides].every((side) => side === "top" || side === "bottom");
    return { kind: "sides", fractions, ...(horizontal ? { edgeSides: "horizontal" as const } : vertical ? { edgeSides: "vertical" as const } : {}) };
  }

  /**
   * `user` when the node may be resized (its size says so and its placement does not forbid it),
   * else `model`; `resize: "both"` when its size is resizable both ways - a horizontal-only size is
   * the library's default.
   */
  private sizing(node: DislNodeNotation): { sizing: ElementTypeDefinition["sizing"]; resize?: "both" } {
    const resizable = node.size?.resizable ?? false;
    if (resizable === "vertical") {
      fail("a node resizable only vertically has no library equivalent.");
    }

    const placement = node.placement?.resizable;
    const placementAllows = placement === undefined || placement === true || (isObject(placement) && (placement.x === true || placement.y === true));
    return {
      sizing: resizable !== false && placementAllows ? "user" : "model",
      ...(resizable === true ? { resize: "both" as const } : {}),
    };
  }

  /** The one axis a node moves along, where its placement is movable along one and not the other. */
  private dragAxis(node: DislNodeNotation): "x" | "y" | undefined {
    const movable = node.placement?.movable;
    if (!isObject(movable) || (movable.x === true) === (movable.y === true)) {
      return undefined;
    }

    return movable.x === true ? "x" : "y";
  }

  /**
   * A node's stroke and fill from its style, where the module asks for them ({@link NotationBindings.stylesFromNotation}):
   * a stroke of no width is not drawn, and a token is its custom property. A CEL colour is the element's
   * {@link NotationBindings.boundStyle}'s, which the payload carries.
   */
  private notationStyle(node: DislNodeNotation): { stroke?: string; strokeWidth?: number; fill?: string } {
    if (this.bindings.stylesFromNotation !== true) {
      return {};
    }

    const style = this.styleOf(node.style);
    const stroke = isObject(style?.stroke) ? style.stroke : undefined;
    const fill = style?.fill;
    const strokeColour = isObject(stroke?.color) && typeof stroke.color.token === "string" ? tokenProperty(stroke.color.token) : undefined;
    return {
      ...(stroke?.width === 0 ? { stroke: "transparent" } : {
        ...(strokeColour !== undefined ? { stroke: strokeColour } : {}),
        ...(typeof stroke?.width === "number" ? { strokeWidth: stroke.width } : {}),
      }),
      ...(isObject(fill) && typeof fill.token === "string" ? { fill: tokenProperty(fill.token) } : {}),
    };
  }

  /**
   * A label's size, weight, slant and colour from its style's `font`, where the module asks for them
   * ({@link NotationBindings.stylesFromNotation}). A CEL colour is the element's bound label colour, and
   * refused where the module binds none.
   */
  private typography(type: string, label: DislLabel, where: string): LabelTypography | undefined {
    if (this.bindings.stylesFromNotation !== true) {
      return undefined;
    }

    const font = this.styleOf(label.style)?.font;
    if (!isObject(font)) {
      return undefined;
    }

    const colour = font.color;
    if (isObject(colour) && typeof colour.cel === "string" && this.bindings.boundStyle?.(type, this.spec.notation.nodes[type]!)?.labelColor === undefined) {
      fail(`${where} is coloured by "${colour.cel}", which no bound label colour of the module carries.`);
    }

    const weight = font.weight;
    const typography: LabelTypography = {
      ...(typeof font.size === "number" ? { fontSize: font.size } : {}),
      ...(weight === "bold" || (typeof weight === "number" && weight >= 600) ? { fontWeight: "bold" as const } : {}),
      ...(font.style === "italic" ? { fontStyle: "italic" as const } : {}),
      ...(isObject(colour) && typeof colour.token === "string" ? { color: tokenProperty(colour.token) } : {}),
    };
    return Object.keys(typography).length > 0 ? typography : undefined;
  }

  private movable(node: DislNodeNotation): boolean {
    const movable = node.placement?.movable;
    return movable === undefined || movable === true || (isObject(movable) && (movable.x === true || movable.y === true));
  }

  // ---- relation types -----------------------------------------------------------------------

  /**
   * A relation with what it does not state itself taken from its supertypes along their C3 linearisation, the
   * nearest type that states it winning (DISL 0.6, §4.7): its ends and whether it is directed, loops, runs in
   * parallel or is acyclic. Its own `derived`, `abstract` and label are its own. A relation type that widens a flag,
   * or whose supertypes differ in one it does not state, is refused, as the backend refuses it.
   */
  private relation(name: string): DislRelation {
    const relations = this.spec.metamodel.relations ?? {};
    const reference = relations[name] === undefined ? this.referenceRelation(name) : undefined;
    if (reference !== undefined) {
      return reference;
    }
    const own = relations[name] ?? fail(`edge "${name}" has no relation in the metamodel.`);
    this.checkRelationFlags(name);
    const merged: Record<string, unknown> = { ...own };
    for (const current of linearisation(name, relations).slice(1)) {
      const supertype = relations[current];
      for (const key of INHERITED_RELATION_KEYS) {
        if (merged[key] === undefined && supertype?.[key] !== undefined) {
          merged[key] = supertype[key];
        }
      }
    }
    return merged as unknown as DislRelation;
  }

  /**
   * The edge a `Type.attribute` key draws for a reference attribute (DISL §4.5, §6.10): from the node that
   * holds the attribute to the node it names, one per holder. None for a key of no such shape, which is then
   * a relation type's name; a reference attribute whose type is no node type is refused.
   */
  private referenceRelation(name: string): DislRelation | undefined {
    const key = /^([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)$/.exec(name);
    const types = this.spec.metamodel.types;
    if (key === null || types[key[1]!] === undefined) {
      return undefined;
    }

    const [, holder, attribute] = key as unknown as [string, string, string];
    const declared = linearisation(holder, types).map((type) => types[type]?.attributes?.[attribute]).find((each) => each !== undefined);
    if (declared === undefined) {
      return fail(`edge "${name}" draws the attribute "${attribute}", which "${holder}" does not have.`);
    }
    if (types[declared.type] === undefined) {
      return fail(`edge "${name}" draws the attribute "${attribute}" of "${holder}", which names no node type.`);
    }

    return { source: holder, target: declared.type, directed: true };
  }

  /** A relation type's flag as DISL 0.6 reads it (§4.7): the nearest stating type of its linearisation, else the default. */
  private relationFlag(name: string, flag: keyof typeof RELATION_FLAGS): boolean {
    const relations = this.spec.metamodel.relations ?? {};
    const stated = linearisation(name, relations).map((type) => relations[type]?.[flag]).find((value) => value !== undefined);
    return stated ?? RELATION_FLAGS[flag];
  }

  /** Refuses a relation type that widens a flag of a direct supertype, or inherits two values of one it does not state (DISL 0.6, §4.7). */
  private checkRelationFlags(name: string): void {
    const relations = this.spec.metamodel.relations ?? {};
    const parents = supertypesOf(relations[name]?.extends).filter((parent) => relations[parent] !== undefined);
    for (const flag of Object.keys(RELATION_FLAGS) as (keyof typeof RELATION_FLAGS)[]) {
      const stated = relations[name]?.[flag];
      for (const parent of parents) {
        const value = this.relationFlag(parent, flag);
        if (flag === "directed" && stated !== undefined && stated !== value) {
          fail(`relation "${name}" states ${flag} ${String(stated)}, which differs from the ${String(value)} of its supertype "${parent}" (DISL §4.7).`);
        }
        if (flag !== "directed" && stated === true && !value) {
          fail(`relation "${name}" states ${flag} true, which widens the false of its supertype "${parent}" (DISL §4.7).`);
        }
      }
      if (stated === undefined && new Set(parents.map((parent) => this.relationFlag(parent, flag))).size > 1) {
        fail(`relation "${name}" inherits ${flag} differently from ${parents.map((parent) => `"${parent}"`).join(" and ")}, and does not state it (DISL §4.7).`);
      }
    }
  }

  private relationType(name: string): RelationTypeDefinition {
    const edge = this.spec.notation.edges![name]!;
    const relation = this.relation(name);
    const routing = edge.line?.routing ?? "straight";
    const target = edge.targetMarker ?? (relation.directed === false ? "none" : "arrowFilled");
    const source = edge.sourceMarker ?? "none";
    const ends = [edge.anchoring?.source, edge.anchoring?.target];
    const className = this.bindings.relationClassName?.(name);
    const lineClassName = this.bindings.relationLineClassName?.(name);
    const hitClassName = this.bindings.relationHitClassName?.(name);
    const customRoute = this.bindings.customRoutes?.[name];
    const along = this.alongTheLine(name, edge, routing);
    const label = this.edgeLabel(name, edge);
    const strokeLadder = this.strokeLadder(name, edge);
    const stub = this.stub(name, edge);
    const { dash, dashWhen } = this.dash(name, edge);
    const ribbon = this.ribbon(name, edge, customRoute);
    if (ribbon !== undefined && along !== undefined) {
      fail(`edge "${name}" is a ribbon and a bowed arc; the library draws one or the other.`);
    }

    // Only a type that may hold children can be the parent end of a relation derived from containment.
    const derivedFromParent = isObject(relation.derived) && relation.derived.source === "item.parent";
    const sourceTypes = typeList(relation.source).flatMap((type) => this.concreteTypes(type)).filter((type) => !derivedFromParent || this.holdsChildren(type));
    const targetTypes = typeList(relation.target).flatMap((type) => this.concreteTypes(type));
    const endpoint = (types: readonly string[], end: "source" | "target"): EndpointConstraint => {
      // An end anchored on the outline attaches where the line crosses it: the library's edge rule.
      const anchors = this.bindings.endpointAnchors?.(name, end) ?? this.startingAnchors(edge, types, end) ?? (edge.anchoring?.[end]?.mode === "outline" ? "edge" as const : undefined);
      return { elementTypes: types.map((type) => this.typeId(type)), ...(anchors !== undefined ? { anchors } : {}) };
    };

    // A stored relation that allows no parallel instances is one per pair: per direction when it
    // is directed. A derived relation's count follows from its derivation and is not declared.
    const perPair = relation.derived === undefined && relation.allowParallel === false ? (relation.directed === false ? "unordered" : "ordered") : undefined;
    const held = this.heldOnce(relation);
    const limits = { ...endLimit(relation.source, "maxFromSource"), ...endLimit(relation.target, "maxIntoTarget") };
    const cardinality: Cardinality = { ...(perPair !== undefined ? { perPair } : {}), ...held, ...limits };

    return {
      id: this.typeId(name),
      route: customRoute ?? (along !== undefined ? bowedArcRoute(along) : libraryRouteOf(routing)),
      style: { ...(source !== "none" ? { startMarker: this.marker(source) } : {}), endMarker: this.marker(target), ...(dash !== undefined ? { dash } : {}) },
      ...(dashWhen !== undefined ? { dashWhen } : {}),
      ...(along !== undefined && ((along.bars?.length ?? 0) > 0 || (along.marks?.length ?? 0) > 0) ? { adorn: bowedArcAdornment(along) } : {}),
      ...(ribbon !== undefined ? { adorn: ribbonAdornment(ribbon, customRoute!) } : {}),
      ...(strokeLadder !== undefined ? { strokeLadder } : {}),
      ...(stub !== undefined ? { stub } : {}),
      ...(className !== undefined ? { className } : {}),
      ...(lineClassName !== undefined ? { lineClassName } : {}),
      ...(hitClassName !== undefined ? { hitClassName } : {}),
      ...(label !== undefined ? { label } : {}),
      ...(edge.selectable === false ? { selectable: false } : {}),
      ...(ends.some((end) => end?.movable === true) ? { movableEnds: true } : {}),
      ...(ends.some((end) => end?.mode === "part") ? { hideWhenAttachmentHidden: true } : {}),
      endpoints: {
        source: endpoint(sourceTypes, "source"),
        target: endpoint(targetTypes, "target"),
        allowSelf: relation.allowSelfLoops === true,
        ...(Object.keys(cardinality).length > 0 ? { cardinality } : {}),
      },
      ...(this.createsOnEmptyRelease(name, edge) ? { emptyRelease: "complete" as const } : {}),
    };
  }

  /**
   * An edge the module names in {@link NotationBindings.ribbons}: its style's stroke width the band's
   * thickness and its stroke colour the band's fill, each bound to the payload as a CEL term the
   * module's celPaths name, and its selected state's casing
   * width the outline it is drawn with while highlighted.
   */
  private ribbon(name: string, edge: DislEdgeNotation, route: CustomRouteRef | undefined): RibbonDeclaration | undefined {
    const ribbon = this.bindings.ribbons?.[name];
    if (ribbon === undefined) {
      return undefined;
    }

    const where = `edge "${name}" is a ribbon`;
    if (route === undefined) {
      return fail(`${where}, which the library sweeps along a custom route the module binds; it binds none.`);
    }

    const stroke = this.styleOf(edge.style)?.stroke;
    const width = isObject(stroke) ? stroke.width : undefined;
    const thickness = width === undefined ? undefined : this.binding(width, `${where}: its thickness`);
    if (thickness === undefined || !("path" in thickness)) {
      return fail(`${where}: its style's stroke width ${JSON.stringify(width)} is no payload path to read a thickness from.`);
    }

    const colour = isObject(stroke) ? stroke.color : undefined;
    const fill = colour === undefined ? undefined : this.binding(colour, `${where}: its colour`);
    if (fill !== undefined && !("path" in fill)) {
      return fail(`${where}: its style's stroke colour ${JSON.stringify(colour)} is no payload path to read a paint from.`);
    }

    const casing = this.styleOf(edge.states?.selected)?.stroke;
    const highlightWidth = isObject(casing) && isObject(casing.casing) && typeof casing.casing.width === "number" ? casing.casing.width : undefined;
    return {
      thickness: { path: thickness.path },
      ...(fill !== undefined ? { fill: { path: fill.path } } : {}),
      ...(ribbon.className !== undefined ? { className: ribbon.className } : {}),
      ...(highlightWidth !== undefined ? { highlightWidth } : {}),
    };
  }

  /**
   * A line's dash (DISL §6.6 `stroke.dash`): the list its type states, or a choice `cond ? [..] : [..]`
   * by an attribute of the relation (`self.<attribute>`) or a condition the module names in
   * celConditions, drawn per connection by its payload. An empty list is a solid line. Any other CEL
   * is refused, naming it: the library has no CEL, and a guess would draw a dash nobody stated.
   */
  private dash(name: string, edge: DislEdgeNotation): { dash?: readonly number[]; dashWhen?: { when: Condition; dash: readonly number[] } } {
    const stroke = edge.line?.stroke;
    const value = isObject(stroke) ? stroke.dash : undefined;
    if (value === undefined) {
      return {};
    }

    const where = `edge "${name}": its line's dash`;
    if (isObject(value) && typeof value.cel === "string") {
      const choice = /^\s*(.+?)\s*\?\s*\[([^\]]*)\]\s*:\s*\[([^\]]*)\]\s*$/.exec(value.cel);
      const test = choice?.[1];
      const own = test === undefined ? null : /^self\.([A-Za-z_][A-Za-z0-9_]*)$/.exec(test);
      const when: Condition | undefined = own !== null && own !== undefined ? { path: `payload.${own[1]}`, is: "true" } : test === undefined ? undefined : this.bindings.celConditions?.[test];
      if (choice === null || when === undefined) {
        return fail(`${where} "${value.cel}" is not a choice between two lists by an attribute of the relation or a condition the module's celConditions names.`);
      }

      const then = dashList(choice[2]!, where);
      const otherwise = dashList(choice[3]!, where);
      return { ...(otherwise.length > 0 ? { dash: otherwise } : {}), dashWhen: { when, dash: then } };
    }

    if (Array.isArray(value) && value.every((entry) => typeof entry === "number")) {
      return value.length > 0 ? { dash: value as readonly number[] } : {};
    }

    return fail(`${where} is ${JSON.stringify(value)}; the library draws a dash from a list of numbers or a choice between two.`);
  }

  /**
   * How many of a drawable derived relation one element may have (DISL 0.4 §4.11.4). A relation derived
   * from a key on the item that names its other end is one per item: the item is the end that holds it,
   * and an end holds one key. Only a relation a gesture can draw (`edits.connect`) is bounded here, since
   * the bound exists to refuse the gesture before it is made. A 0.3 specification keeps what it had: there
   * a drawn line onto an element that has one already moves it, which its module answers. A 0.5 one is a
   * 0.4 one here, since 0.5 changes nothing of 0.4 but adds the person shape.
   */
  private heldOnce(relation: DislRelation): { maxFromSource: 1 } | { maxIntoTarget: 1 } | undefined {
    const derived = relation.derived;
    if (this.spec["disl"] === "0.3" || !isObject(derived) || !isObject(derived.edits) || typeof derived.edits.connect !== "string") {
      return undefined;
    }

    const reference = /^item\.[A-Za-z_][A-Za-z0-9_]*$/;
    if (derived.target === "item" && typeof derived.source === "string" && reference.test(derived.source)) {
      return { maxIntoTarget: 1 };
    }
    if (derived.source === "item" && typeof derived.target === "string" && reference.test(derived.target)) {
      return { maxFromSource: 1 };
    }

    return undefined;
  }

  /**
   * The canvas's cycle rules (DISL §4.9 `acyclic`): one per relation type that states it and has no acyclic
   * supertype to cover it, over that type and every subtype the notation draws, so `acyclic` on an abstract
   * type forbids a loop through any mix of its subtypes, as the backend's `std.acyclic` does.
   */
  private acyclicRules(): { relationTypes: string[] }[] {
    const relations = this.spec.metamodel.relations ?? {};
    const supertypes = (name: string): string[] => linearisation(name, relations).slice(1);
    const drawn = Object.keys(this.spec.notation.edges ?? {});
    return Object.entries(relations)
      .filter(([name, relation]) => relation.acyclic === true && !supertypes(name).some((supertype) => relations[supertype]?.acyclic === true))
      .map(([name, relation]) => {
        const members = drawn.filter((edge) => edge === name || supertypes(edge).includes(name));
        return relation.abstract === true || members.includes(name) ? members : [name, ...members];
      })
      .filter((members) => members.length > 0)
      .map((members) => ({ relationTypes: members.map((member) => this.typeId(member)) }));
  }

  /** A DISL marker as the library draws it. */
  private marker(marker: string): MarkerKind {
    return libraryMarkerOf(marker);
  }

  /**
   * A curved line (DISL §6.10 `curved`) as a bowed arc, with what the notation rides along it: the
   * variants that bow it the other way, its loop to its own node, the bars of its `midMarkers` and
   * the marks of its labels placed at a fraction. Those two are drawn against the arc's own geometry,
   * so a line of any other routing that states one is refused.
   */
  private alongTheLine(name: string, edge: DislEdgeNotation, routing: string): BowedArcDeclaration | undefined {
    const where = `edge "${name}"`;
    const midMarkers = edge.midMarkers ?? [];
    const marks = (edge.labels ?? []).filter((label) => typeof label.at === "number");
    if (routing !== "curved") {
      if (midMarkers.length > 0 || marks.length > 0) {
        fail(`${where} draws ${midMarkers.length > 0 ? "markers" : "labels at a fraction"} along a "${routing}" line; the library draws them along a curved one only.`);
      }
      return undefined;
    }

    const line = (edge.line ?? {}) as Readonly<Record<string, unknown>>;
    // DISL leaves a curve's default bow to the host: the library's is a fifth of the chord.
    const bow = typeof line.curvature === "number" ? line.curvature : ArcBow;
    const variants = (edge.variants ?? []).map((variant, index) => this.bowVariant(variant, `${where} variant ${index + 1}`));
    const selfLoop = edge.selfLoop;
    if (selfLoop !== undefined && ((selfLoop.shape ?? "arc") !== "arc" || (selfLoop.side ?? "top") !== "top")) {
      fail(`${where} loops to its own node as ${JSON.stringify(selfLoop)}; the library draws that loop as an arc above the node.`);
    }

    const bars = this.bars(name, midMarkers);
    const drawn = marks.map((label) => this.mark(name, label));
    return {
      bow,
      ...(variants.length > 0 ? { variants } : {}),
      ...(selfLoop?.size !== undefined ? { selfLoopRadius: selfLoop.size } : {}),
      ...(bars.length > 0 ? { bars } : {}),
      ...(drawn.length > 0 ? { marks: drawn } : {}),
    };
  }

  /** A variant that bows a curved line otherwise while its condition holds; any other change is refused. */
  private bowVariant(variant: unknown, where: string): { when: Condition; bow: number } {
    const shape = isObject(variant) ? variant : {};
    const line = isObject(shape.line) ? shape.line : {};
    const others = [...Object.keys(shape).filter((key) => !["when", "doc", "line"].includes(key)), ...Object.keys(line).filter((key) => !["routing", "curvature", "bendpoints"].includes(key))];
    if (typeof shape.when !== "string" || typeof line.curvature !== "number" || (line.routing ?? "curved") !== "curved" || others.length > 0) {
      return fail(`${where} changes ${JSON.stringify(variant)}; the library draws a variant of a curved line as the same curve bowed otherwise.`);
    }

    return { when: this.condition(shape.when.trim(), where), bow: line.curvature };
  }

  /**
   * The bars of a line's `midMarkers` (DISL §6.10): each a `bar` of a width, at a fraction, oriented by
   * the line. Neighbours of one width shown under one condition are drawn as one group - the strokes of
   * a delay mark. Any other marker is refused.
   */
  private bars(name: string, markers: readonly DislMidMarker[]): ArcBarsDeclaration[] {
    const classes = this.bindings.midMarkerClassNames?.(name) ?? {};
    const groups: ArcBarsDeclaration[] = [];
    let key: string | undefined;
    for (const marker of markers) {
      const type = typeof marker.marker === "string" ? marker.marker : marker.marker.type;
      const width = typeof marker.marker === "string" ? undefined : marker.marker.params?.width;
      if (type !== "bar" || typeof width !== "number" || (marker.orient ?? "auto") !== "auto") {
        fail(`edge "${name}" draws the marker ${JSON.stringify(marker.marker)} oriented "${String(marker.orient)}"; the library draws a bar of a width across the line.`);
      }

      const when = this.visibility(marker.visible, `edge "${name}" marker at ${marker.at}`);
      const next = JSON.stringify([width, when]);
      if (next !== key) {
        groups.push({ at: [], length: width, ...(when !== undefined ? { when } : {}), ...(classes.group !== undefined ? { className: classes.group } : {}), ...(classes.marker !== undefined ? { barClassName: classes.marker } : {}) });
        key = next;
      }
      (groups[groups.length - 1]!.at as number[]).push(marker.at);
    }

    return groups;
  }

  /** A label at a fraction of a curved line, beside it on the left or the right of travel: a mark the line carries. */
  private mark(name: string, label: DislEdgeLabel): ArcMarkDeclaration {
    const where = `edge "${name}" label "${label.id}"`;
    const side = label.side ?? "left";
    if ((side !== "left" && side !== "right") || (label.orientation ?? "horizontal") !== "horizontal" || (label.editable !== undefined && label.editable !== false)) {
      fail(`${where} sits ${JSON.stringify({ side: label.side, orientation: label.orientation, editable: label.editable })}; the library writes a mark beside a curved line, upright, on its left or right, and never edits one.`);
    }

    const className = this.bindings.edgeLabelClassName?.(name, label.id);
    return {
      text: this.binding(label.text, where) as ArcMarkDeclaration["text"],
      at: label.at as number,
      side,
      distance: label.distance ?? 0,
      ...(className !== undefined ? { className } : {}),
    };
  }

  /**
   * A line's computed stroke width (DISL §6.10 `line.stroke.width` as CEL) as a ladder of thresholds on
   * one attribute: `!has(self.a) ? w : (self.a >= x ? w1 : (self.a <= y ? w2 : w3))`, nested as deep
   * as it likes, each test `!has(self.a)` or `self.a` against a number. Each width it gives is painted
   * by the class the module names for it. A fixed width is a style, which the stylesheet paints.
   */
  private strokeLadder(name: string, edge: DislEdgeNotation): StrokeLadder | undefined {
    const stroke = isObject(edge.line?.stroke) ? edge.line.stroke : undefined;
    const width = stroke?.width;
    if (!isObject(width)) {
      return undefined;
    }

    const where = `edge "${name}" stroke width`;
    const expression = typeof width.cel === "string" ? width.cel : fail(`${where} binds ${JSON.stringify(width)}; the library reads a computed width as CEL.`);
    const attributes = new Set<string>();
    const ladder = strokeLadderOf(expression, attributes, where);
    if (attributes.size !== 1) {
      fail(`${where} "${expression}" chooses by ${attributes.size} attributes; the library chooses a width by thresholds on one.`);
    }

    const widths = new Set([...ladder.rungs.map((rung) => rung.width), ladder.otherwise]);
    const classNames = Object.fromEntries([...widths].map((each) => [String(each), this.bindings.strokeWidthClassName?.(name, each) ?? fail(`${where} gives ${each}, which the module's strokeWidthClassName paints with no class.`)]));
    return { path: `payload.${[...attributes][0]!}`, rungs: ladder.rungs, otherwise: ladder.otherwise, classNames };
  }

  // ---- lists and badges (DISL 0.4) ----------------------------------------------------------

  /**
   * A CEL term a row binds, as a path within the row: `item.<attribute>`, or the module's
   * `rowPaths`. Anything else is refused, naming it.
   */
  private rowPath(value: unknown, where: string): string {
    const expression = isObject(value) && typeof value.cel === "string" ? value.cel.trim() : undefined;
    if (expression === undefined) {
      return fail(`${where} binds ${JSON.stringify(value)}; a row binds CEL over its item.`);
    }

    const bound = this.bindings.rowPaths?.[expression];
    if (bound !== undefined) {
      return bound;
    }

    const attribute = /^item\.([A-Za-z_][A-Za-z0-9_]*)$/.exec(expression);
    return attribute !== null ? attribute[1]! : fail(`${where}: the CEL "${expression}" has no path within a row in the module's rowPaths.`);
  }

  /**
   * A node's lists (DISL 0.4 §6.9). Its children in a slot are the payload's list of that name; a CEL
   * list is the payload path the module's `celPaths` gives it. Grouped by an enum attribute of the
   * items, the groups are the enum's values in its order under their labels. Which headings are folded
   * is the payload's `collapsed`: the backend applies the definition's defaults and what the reader set.
   * A list with no title, no groups and nothing to fold it by is drawn as its rows alone.
   */
  private compartments(name: string, node: DislNodeNotation): CompartmentDeclaration[] {
    const declared = node.compartments ?? [];
    if (declared.length === 0) {
      return [];
    }

    // Beneath the lowest line of text the node's labels write.
    const lines = (node.labels ?? []).map((label) => {
      const position = label.position;
      return isObject(position) ? ((position.offset as readonly [number, number] | undefined)?.[1] ?? 0) : position === "top" ? LABEL_LINE : 0;
    });
    const top = Math.max(LABEL_LINE, ...lines) + LIST_GAP;

    return declared.map((compartment: DislCompartment): CompartmentDeclaration => {
      const where = `node "${name}" list "${compartment.id}"`;
      const items = compartment.items;
      const rows = "cel" in items
        ? this.bindings.celPaths[items.cel] ?? fail(`${where}: the CEL list "${items.cel}" has no payload path in the module's celPaths.`)
        : `payload.${items.slot}`;
      const text = compartment.itemText === undefined ? fail(`${where} states no itemText.`) : this.rowPath(compartment.itemText, `${where} itemText`);
      const order = compartment.itemOrder;
      const plain = compartment.title === undefined && compartment.groupBy === undefined && compartment.collapsible !== true;

      return {
        id: compartment.id,
        rows,
        rowId: "id",
        text: { path: text },
        ...(compartment.itemLink !== undefined ? { link: this.rowPath(compartment.itemLink, `${where} itemLink`) } : {}),
        ...(order !== undefined ? { orderBy: { path: this.rowPath({ cel: order.by }, `${where} itemOrder`), direction: order.direction ?? "ascending" } } : {}),
        ...(compartment.groupBy !== undefined ? { groupBy: this.groups(compartment, where) } : {}),
        ...(compartment.title !== undefined ? { title: compartment.title } : {}),
        ...(plain ? { heading: "none" as const } : {}),
        collapsed: "payload.collapsed",
        top,
        headingHeight: LIST_HEADING,
        rowHeight: LIST_ROW,
        bottom: LIST_BOTTOM,
        insetX: LIST_INSET,
        rowIndent: plain ? 0 : LIST_INDENT,
      };
    });
  }

  /** A list's groups: the values of the enum the items' attribute has, in the enum's order. */
  private groups(compartment: DislCompartment, where: string): NonNullable<CompartmentDeclaration["groupBy"]> {
    const items = compartment.items;
    const attribute = compartment.groupBy!.attribute;
    const type = "children" in items ? this.spec.metamodel.types[items.children[0] ?? ""]?.attributes?.[attribute]?.type : undefined;
    const values = type === undefined ? undefined : this.spec.metamodel.enums?.[type]?.values;
    if (values === undefined) {
      return fail(`${where} groups by "${attribute}", which is no enum attribute of its items.`);
    }

    return {
      path: attribute,
      groups: Object.entries(values).map(([value, member]) => ({ value, title: member.label ?? value })),
      otherTitle: "Other",
    };
  }

  /**
   * A node's badges (DISL 0.4 §6.9). One whose press opens a link - its operation's one action is
   * `open` of an attribute - is the library's link symbol for that attribute, drawn where there is a
   * link and nowhere else. One that runs nothing is a mark, shown while its condition holds. Any other
   * badge is refused, naming it.
   */
  private badges(name: string, node: DislNodeNotation): { links: LinkDeclaration[]; decorations: DecorationDeclaration[] } {
    const links: LinkDeclaration[] = [];
    const decorations: DecorationDeclaration[] = [];
    for (const badge of node.badges ?? []) {
      const where = `node "${name}" badge "${badge.id}"`;
      if (badge.pack === true) {
        decorations.push(this.packedBadge(name, node.badgeLayout, badge, where));
        continue;
      }

      const line = this.lineBadge(name, badge, where);
      if (line !== undefined) {
        decorations.push(line);
        continue;
      }

      const right = badge.position === "top-right";
      if (!right && badge.position !== "top-left") {
        fail(`${where} sits "${String(badge.position)}"; the library places a badge at the top left or the top right.`);
      }

      if (badge.onClick !== undefined) {
        const operation = this.spec.behavior?.operations?.[badge.onClick];
        const actions = isObject(operation) && Array.isArray(operation.actions) ? (operation.actions as readonly unknown[]) : [];
        const opened = actions.length === 1 && isObject(actions[0]) && typeof actions[0].open === "string" ? /^self\.([A-Za-z_][A-Za-z0-9_]*)$/.exec(actions[0].open) : null;
        if (opened === null || !right) {
          fail(`${where} runs "${badge.onClick}"; the library draws a pressed badge only as a link symbol at the top right, opening one attribute.`);
        }
        links.push({ id: badge.id, link: `payload.${opened[1]}`, at: { right: BADGE_INSET + LINK_SIZE, top: BADGE_TOP } });
        continue;
      }

      const visible = typeof badge.visible === "string" ? badge.visible : isObject(badge.visible) && "cel" in badge.visible ? badge.visible.cel : undefined;
      const when =
        isObject(badge.visible) && "attribute" in badge.visible
          ? this.visibility(badge.visible, where)
          : visible === undefined
            ? undefined
            : this.bindings.celConditions?.[visible] ?? fail(`${where} shows when "${visible}", which has no condition in the module's celConditions.`);
      const className = this.bindings.badgeClassName?.(name, badge);
      const tooltip = badge.tooltip === undefined ? undefined : typeof badge.tooltip === "string" ? { template: badge.tooltip } : this.binding(badge.tooltip, `${where} tooltip`);
      if (badge.text !== undefined) {
        // A mark with text is that text, at its corner moved by its offset (DISL §6.9), on the corner's side.
        const [dx, dy] = badge.offset ?? [0, 0];
        decorations.push({
          glyph: "marker",
          anchor: "canvas",
          from: { x: { path: right ? "bounds.right" : "bounds.left", number: { plus: dx } }, y: { path: "bounds.top", number: { plus: dy } } },
          text: typeof badge.text === "string" ? this.literal(badge.text, `${where} text`) : this.binding(badge.text, `${where} text`),
          textAnchor: right ? "end" : "start",
          ...(className !== undefined ? { className } : {}),
          ...(tooltip !== undefined ? { tooltip, accessibility: { role: "img", label: tooltip } } : {}),
          ...(when !== undefined ? { when } : {}),
        });
        continue;
      }

      const said = typeof badge.tooltip === "string" ? badge.tooltip.split(".")[0]! : badge.id;
      decorations.push({
        glyph: "circle",
        anchor: "canvas",
        from: {
          x: right ? { path: "bounds.right", number: { plus: -BADGE_INSET } } : { path: "bounds.left", number: { plus: BADGE_INSET } },
          y: { path: "bounds.top", number: { plus: BADGE_INSET } },
        },
        radius: BADGE_RADIUS,
        ...(className !== undefined ? { className } : {}),
        ...(tooltip !== undefined ? { tooltip } : {}),
        accessibility: { role: "img", label: { template: said } },
        ...(when !== undefined ? { when } : {}),
      });
    }

    return { links, decorations };
  }

  /**
   * A badge drawn as a `line` at a side of its node (DISL §6.9): centred on that side's middle, moved by
   * its offset, and `size` long along its orientation; none for a badge that is no line at a side.
   */
  private lineBadge(name: string, badge: DislBadge, where: string): DecorationDeclaration | undefined {
    const side = typeof badge.position === "string" && /^(left|right|top|bottom)$/.test(badge.position) ? badge.position : undefined;
    const shape = badge.shape;
    if (side === undefined || shape === undefined || (typeof shape === "string" ? shape : shape.type) !== "line") {
      return undefined;
    }

    const orientation = typeof shape === "string" ? undefined : shape.params?.orientation;
    if (orientation !== "horizontal" && orientation !== "vertical") {
      return fail(`${where} is a line along ${JSON.stringify(orientation)}; the library draws a line badge horizontal or vertical.`);
    }
    if (typeof badge.size !== "number") {
      return fail(`${where} is a line of no stated size.`);
    }

    const [dx, dy] = badge.offset ?? [0, 0];
    const across = side === "left" || side === "right";
    const x = across ? `bounds.${side}` : "bounds.centreX";
    const y = across ? "bounds.centreY" : `bounds.${side}`;
    const half = badge.size / 2;
    const end = (sign: number) => ({
      x: { path: x, number: { plus: dx + (orientation === "horizontal" ? sign * half : 0) } },
      y: { path: y, number: { plus: dy + (orientation === "vertical" ? sign * half : 0) } },
    });
    const className = this.bindings.badgeClassName?.(name, badge);
    const tooltip = badge.tooltip === undefined ? undefined : typeof badge.tooltip === "string" ? this.literal(badge.tooltip, `${where} tooltip`) : this.binding(badge.tooltip, `${where} tooltip`);
    const when = this.visibility(badge.visible, where);

    return {
      glyph: "line",
      anchor: "canvas",
      from: end(-1),
      to: end(1),
      ...(className !== undefined ? { className } : {}),
      ...(tooltip !== undefined ? { tooltip } : {}),
      ...(when !== undefined ? { when } : {}),
    };
  }

  /**
   * A packed badge (DISL §6.9 *Packed badges*): its text in the next free slot of the node's badge
   * layout - the layout's corner moved by its offset, and `spacing` further in its direction for each
   * packed badge before it that is drawn - shown while its condition holds. The text is anchored on the
   * side the row grows from, so a row running left ends at the corner and one running right starts there.
   */
  private packedBadge(name: string, layout: DislBadgeLayout | undefined, badge: DislBadge, where: string): DecorationDeclaration {
    if (layout === undefined) {
      return fail(`${where} is packed, and the node states no badgeLayout to pack it into.`);
    }

    const corner = /^(top|bottom)-(left|right)$/.exec(layout.start);
    if (corner === null) {
      return fail(`node "${name}" starts its badges at "${layout.start}"; the library starts a row of badges at a corner.`);
    }
    if (badge.text === undefined) {
      return fail(`${where} is packed and shows no text; the library draws a packed badge's text.`);
    }

    const [dx, dy] = layout.offset ?? [0, 0];
    const steps: Readonly<Record<DislBadgeLayout["direction"], { x: number; y: number }>> = {
      left: { x: -layout.spacing, y: 0 },
      right: { x: layout.spacing, y: 0 },
      up: { x: 0, y: -layout.spacing },
      down: { x: 0, y: layout.spacing },
    };
    const step = steps[layout.direction] ?? fail(`node "${name}" runs its badges "${String(layout.direction)}", which is no direction.`);
    const textAnchor = layout.direction === "left" ? "end" : layout.direction === "right" ? "start" : "middle";
    const text = typeof badge.text === "string" ? this.literal(badge.text, `${where} text`) : this.binding(badge.text, `${where} text`);
    const tooltip = badge.tooltip === undefined ? undefined : typeof badge.tooltip === "string" ? this.literal(badge.tooltip, `${where} tooltip`) : this.binding(badge.tooltip, `${where} tooltip`);
    const when = this.visibility(badge.visible, where);
    const className = this.bindings.badgeClassName?.(name, badge);
    const data = this.bindings.badgeData?.(name, badge);

    return {
      glyph: "marker",
      anchor: "canvas",
      from: {
        x: { path: `bounds.${corner[2]}`, number: { plus: dx } },
        y: { path: `bounds.${corner[1]}`, number: { plus: dy } },
      },
      pack: { step },
      text,
      textAnchor,
      ...(className !== undefined ? { className } : {}),
      ...(tooltip !== undefined ? { tooltip } : {}),
      ...(data !== undefined ? { data } : {}),
      ...(when !== undefined ? { when } : {}),
    };
  }

  /** A text written as it is, refused where a brace would read as a {placeholder}. */
  private literal(text: string, where: string): { template: string } {
    if (/[{}]/.test(text)) {
      fail(`${where} "${text}" holds a brace, which the library would read as a {placeholder}.`);
    }
    return { template: text };
  }

  /**
   * The anchors a drawn connection may start from at this end: the named anchors the edge's `connect.from`
   * maps (DISL 0.3 §6.10), whichever end of the relation each starts, in the order the end's node lists
   * them. A gesture's direction is the module's to read from the anchor it lifted from.
   */
  private startingAnchors(edge: DislEdgeNotation, types: readonly string[], end: "source" | "target"): readonly string[] | undefined {
    const from = edge.connect?.from;
    if (from === undefined || end !== "source") {
      return undefined;
    }

    const listed = types.flatMap((type) => (this.spec.notation.nodes[type]?.anchors?.points ?? []).map((point) => point.id));
    const named = Object.keys(from);
    return [...new Set([...listed.filter((id) => named.includes(id)), ...named])];
  }

  /**
   * Whether a connect gesture released on empty canvas creates the missing end: the tool the edge's
   * `connect.tool` names, else a library tool creating this relation, has a `createTarget` or a
   * `createSource` (DISL 0.3 §7.2). The library then raises the release for the module to answer.
   */
  private createsOnEmptyRelease(name: string, edge: DislEdgeNotation): boolean {
    const tools = this.spec.toolbox?.tools ?? {};
    const named = edge.connect?.tool;
    const tool = named !== undefined
      ? tools[named] ?? (this.spec.toolbox?.groups ?? []).flatMap((group) => group.tools).find((each) => each.id === named) ?? fail(`edge "${name}" connects with the tool "${named}", which the toolbox does not declare.`)
      : Object.values(tools).find((each) => each.creates === name);
    return tool !== undefined && (tool.createTarget !== undefined || tool.createSource !== undefined);
  }

  /**
   * The edge's label along its route: at its start, middle or end, the side above or below the line at its
   * distance. The text is the connection's own label, which the model carries; only an attribute can be it.
   */
  private edgeLabel(name: string, edge: DislEdgeNotation): RouteLabelRule | undefined {
    // A label at a fraction of a curved line is a mark along it, which `alongTheLine` reads.
    const labels = (edge.labels ?? []).filter((label) => typeof label.at !== "number");
    if (labels.length === 0) {
      return undefined;
    }

    const where = `edge "${name}" label`;
    if (labels.length > 1) {
      fail(`${where}s: the library draws one label along a line; the specification declares ${labels.length}.`);
    }

    const label = labels[0]!;
    // `self.<attribute>` as CEL is the attribute itself: the connection's own label either way.
    // A CEL the module's celPaths names is one the module composes into the connection's own label.
    const ownAttribute = isObject(label.text) && "cel" in label.text && typeof label.text.cel === "string"
      && (/^self\.[A-Za-z_][A-Za-z0-9_]*$/.test(label.text.cel.trim()) || this.bindings.celPaths[label.text.cel] !== undefined);
    if (!ownAttribute && (!isObject(label.text) || !("attribute" in label.text) || typeof label.text.attribute !== "string")) {
      fail(`${where} "${label.id}" binds ${JSON.stringify(label.text)}; the library draws a line's label from the connection's own label, an attribute.`);
    }

    const at = label.at ?? "middle";
    const placement = at === "start" ? "source" : at === "middle" ? "midpoint" : at === "end" ? "target" : fail(`${where} "${label.id}" sits at ${JSON.stringify(at)}; the library places a line's label at its start, middle or end.`);
    const distance = label.distance ?? 0;
    const side = label.side ?? "on";
    const offset = side === "above" ? -distance : side === "below" ? distance : side === "on" && distance === 0 ? 0 : fail(`${where} "${label.id}" sits ${distance} ${side} its line, which the library cannot place.`);
    const editable = label.editable === true || label.editable === "inline" || label.editable === "multiline";
    return { placement, ...(offset !== 0 ? { offset } : {}), ...(editable ? { editable } : {}) };
  }

  /**
   * How a relation without a target is drawn (DISL §6.10 *Stubs*): a line of its length out of the
   * middle of the source's side, painted by the class the module names for its style, with its label
   * above or below it, bound as any text is. `auto`, which leaves the side to the runtime, is refused:
   * the library draws a stub out of the side it is given.
   */
  private stub(name: string, edge: DislEdgeNotation): StubDeclaration | undefined {
    const stub = edge.stub;
    if (stub === undefined) {
      return undefined;
    }

    const where = `edge "${name}"`;
    if (stub.side !== "right" && stub.side !== "left" && stub.side !== "top" && stub.side !== "bottom") {
      fail(`${where}: its stub leaves "${String(stub.side)}"; the library draws a stub out of the right, left, top or bottom.`);
    }
    if (typeof stub.length !== "number" || stub.length <= 0) {
      fail(`${where}: its stub is ${JSON.stringify(stub.length)} long; the library draws a stub of a positive length.`);
    }

    const className = this.bindings.stubClassName?.(name, stub);
    const label = stub.label;
    const side = label?.side ?? "above";
    if (label !== undefined && side !== "above" && side !== "below") {
      fail(`${where}: its stub label "${label.id}" sits "${side}"; the library writes a stub's label above or below it.`);
    }
    const labelClass = label === undefined ? undefined : this.bindings.edgeLabelClassName?.(name, label.id);
    const style = this.styleOf(stub.style);
    const dash = isObject(style?.stroke) && Array.isArray(style.stroke.dash) && style.stroke.dash.length > 0 ? (style.stroke.dash as readonly number[]) : undefined;

    return {
      length: stub.length,
      side: stub.side,
      ...(className !== undefined ? { className } : {}),
      ...(dash !== undefined ? { dash } : {}),
      ...(label !== undefined
        ? {
          label: {
            text: this.binding(label.text, `${where} stub label "${label.id}"`) as NonNullable<StubDeclaration["label"]>["text"],
            side: side as "above" | "below",
            ...(label.distance !== undefined ? { distance: label.distance } : {}),
            ...(labelClass !== undefined ? { className: labelClass } : {}),
          },
        }
        : {}),
    };
  }

  // ---- actions ------------------------------------------------------------------------------

  /** The concrete node types or relation a menu is for, or null for the canvas and gesture menus. */
  private menuTargets(menu: DislContextMenu): { kind: "element" | "connection"; types: readonly string[] } | null {
    if (menu.for.includes("diagram") || menu.for.includes("connection")) {
      return null;
    }

    if (menu.for.every((name) => this.spec.metamodel.relations?.[name] !== undefined)) {
      return { kind: "connection", types: menu.for };
    }

    // A menu for types the notation draws as rows of a list, not as nodes (DISL 0.4 §6.9), declares
    // nothing on the canvas: a row has no gesture of its own, and its menu is the backend's.
    const types = menu.for.flatMap((name) => this.concreteTypes(name));
    return types.length === 0 ? null : { kind: "element", types };
  }

  /**
   * The actions a key or a canvas gesture invokes, from the context menus' entries. A double-click is
   * the library's `activate` gesture and invokes the node's `doubleClick` entry (DISL's default:
   * `editLabel`); the Delete key is the library's `delete` gesture. Gesture-invoked actions come
   * first, then those only a shortcut invokes, each in menu order.
   */
  private actions(): ActionDeclaration[] {
    const found: { id: string; kind: "element" | "connection"; types: Set<string>; keys: ShortcutInvocation[]; gestures: ("activate" | "delete")[]; rank: number; shortcut?: string; pins?: true }[] = [];
    for (const menu of this.spec.toolbox?.contextMenus ?? []) {
      const targets = this.menuTargets(menu);
      if (targets === null) {
        continue;
      }

      for (const tool of menu.tools) {
        const activates = tool.kind === (targets.kind === "element" ? this.doubleClickOf(targets.types) : undefined);
        const deletes = tool.kind === "delete";
        const keys = tool.shortcut !== undefined && !(deletes && tool.shortcut === "Delete") ? [shortcutOf(tool.shortcut)] : [];
        // DISL 0.4's `pin` entry keeps an element where the layout drew it, which the canvas alone can say.
        const pins = tool.kind === "pin" && targets.kind === "element";
        if (!activates && !deletes && !pins && keys.length === 0) {
          continue;
        }

        const key = tool.operation ?? tool.kind;
        for (const forType of menu.for) {
          const id = this.wire.actions?.[`${forType}/${key}`] ?? this.wire.actions?.[key] ?? (this.bindings.wireIds === undefined ? key : fail(`the "${key}" entry of the menu for ${forType} has no wire id in ${this.bindings.wireIds}.actions.`));
          let action = found.find((entry) => entry.id === id && entry.kind === targets.kind);
          if (action === undefined) {
            action = { id, kind: targets.kind, types: new Set(), keys: [], gestures: [], rank: activates ? 0 : deletes ? 1 : 2 };
            found.push(action);
          }
          if (action.shortcut === undefined && tool.shortcut !== undefined) {
            action.shortcut = shortcutOf(tool.shortcut).key;
          }
          (targets.kind === "element" ? this.concreteTypes(forType) : [forType]).forEach((type) => action!.types.add(type));
          keys.filter((each) => !action!.keys.some((known) => samePress(known, each))).forEach((each) => action!.keys.push(each));
          if (activates && !action.gestures.includes("activate")) {
            action.gestures.push("activate");
          }
          if (deletes && !action.gestures.includes("delete")) {
            action.gestures.push("delete");
          }
          if (pins) {
            action.pins = true;
          }
        }
      }
    }

    // A double-click that runs an operation no menu lists (DISL §6.9 `doubleClick`) is still the activate gesture.
    for (const type of this.nodeNames) {
      const operation = this.spec.notation.nodes[type]?.doubleClick;
      if (operation === undefined || this.spec.behavior?.operations?.[operation] === undefined
        || found.some((action) => action.kind === "element" && action.types.has(type) && action.gestures.includes("activate"))) {
        continue;
      }

      const id = this.wire.actions?.[`${type}/${operation}`] ?? this.wire.actions?.[operation] ?? (this.bindings.wireIds === undefined ? operation : fail(`the double-click operation "${operation}" of ${type} has no wire id in ${this.bindings.wireIds}.actions.`));
      let action = found.find((entry) => entry.id === id && entry.kind === "element");
      if (action === undefined) {
        action = { id, kind: "element", types: new Set(), keys: [], gestures: ["activate"], rank: 0 };
        found.push(action);
      } else if (!action.gestures.includes("activate")) {
        action.gestures.push("activate");
      }
      action.types.add(type);
      // The key the operation itself states (DISL §9.3 `shortcut`), which no menu entry carries here.
      const declared = this.spec.behavior?.operations?.[operation];
      const shortcut = isObject(declared) && typeof declared.shortcut === "string" ? declared.shortcut : undefined;
      if (shortcut !== undefined) {
        const press = shortcutOf(shortcut);
        if (!action.keys.some((known) => samePress(known, press))) {
          action.keys.push(press);
        }
        action.shortcut ??= press.key;
      }
    }

    const allNodes = new Set(this.nodeNames);
    return [...found].sort((a, b) => a.rank - b.rank).map((action): ActionDeclaration => {
      const invokedBy: ActionInvocation[] = [
        ...action.keys,
        ...action.gestures.map((gesture): ActionInvocation => ({ kind: "gesture", gesture })),
        ...(action.pins === true ? [{ kind: "menu" } as const] : []),
      ];
      const narrowed = action.kind === "element" && [...allNodes].some((type) => !action.types.has(type));
      const declaration: ActionDeclaration = {
        id: action.id,
        invokedBy,
        appliesTo: [
          action.kind === "element"
            ? { kind: "element", ...(narrowed ? { elementTypes: [...action.types].map((type) => this.typeId(type)) } : {}) }
            : { kind: "connection" },
        ],
        ...(action.pins === true ? { keepsWhereDrawn: true as const } : {}),
      };
      return this.bindings.action?.(declaration, action.shortcut !== undefined ? { shortcut: action.shortcut } : {}) ?? declaration;
    });
  }

  /** The entry a double-click invokes on these types: their shared `doubleClick`, DISL's default `editLabel`. */
  private doubleClickOf(types: readonly string[]): string {
    const chosen = new Set(types.map((type) => this.spec.notation.nodes[type]?.doubleClick ?? "editLabel"));
    if (chosen.size !== 1) {
      fail(`a menu covers types whose double-click invokes different entries (${[...chosen].join(", ")}).`);
    }

    return [...chosen][0]!;
  }

  // ---- canvas -------------------------------------------------------------------------------

  private systemNamed(name: string | undefined): DislCoordinateSystem | undefined {
    return name === undefined ? undefined : this.spec.coordinates?.systems[name] ?? fail(`coordinate system "${name}" is not declared.`);
  }

  private axis(axis: string | DislAxis): DislAxis {
    return typeof axis === "string" ? this.spec.coordinates?.axes?.[axis] ?? fail(`axis "${axis}" is not declared.`) : axis;
  }

  /** A diagram attribute's value: the option given, else the attribute's default. */
  private diagramValue(value: Bindable<string>): string {
    if (typeof value === "string") {
      return value;
    }

    if ("attribute" in value) {
      const attribute = value.attribute;
      const given = this.options.diagram?.[attribute] ?? this.spec.metamodel.diagram?.attributes?.[attribute]?.default;
      return typeof given === "string" ? given : fail(`the diagram attribute "${attribute}" has no value to compile with.`);
    }

    return fail(`${JSON.stringify(value)} is bound to something other than a diagram attribute.`);
  }

  private unitMonths(unit: string): number {
    return UNIT_MONTHS[unit] ?? fail(`the time unit "${unit}" has no length in months.`);
  }

  /** One snap step of the default system per axis, each element snapping from its own origin. */
  private snap(): SnapDeclaration | undefined {
    const system = this.system as (DislCoordinateSystem & { snapping?: Record<string, unknown> }) | undefined;
    const snapping = system?.snapping;
    if (system === undefined || snapping === undefined) {
      return undefined;
    }

    const origins = this.bindings.snapOrigins;
    const step = (axisName: "x" | "y"): number => {
      const rule = snapping[axisName] as Record<string, unknown> | undefined;
      const axis = this.axis(system[axisName] ?? fail(`the ${axisName} snapping rule ${JSON.stringify(rule)} is on a system that declares no ${axisName} axis.`));
      if (isObject(rule?.calendar) && isObject(axis.scale) && JSON.stringify(rule.calendar.unit) === JSON.stringify(axis.scale.unit)) {
        // A calendar snap in the axis's own unit is one step of the axis: `scale.size` canvas units.
        return axis.scale.size;
      }
      if (isObject(rule?.grid) && typeof rule.grid.spacing === "number" && typeof axis.scale === "number") {
        return rule.grid.spacing * axis.scale;
      }
      return fail(`the ${axisName} snapping rule ${JSON.stringify(rule)} has no step this compiler can state.`);
    };

    const axisSnap = (axisName: "x" | "y"): SnapAxis | undefined => {
      const bound = this.bindings.snapAxes?.[axisName];
      if (bound !== undefined) {
        return bound;
      }
      // An axis that does not snap (`"none"`) moves freely: it has no resting lines to state.
      if (snapping[axisName] === "none") {
        return undefined;
      }
      return { step: step(axisName), ...(origins !== undefined ? { origin: { path: origins[axisName] } } : {}) };
    };
    const y = axisSnap("y");
    const x = axisSnap("x");
    return x === undefined && y === undefined ? undefined : { ...(y !== undefined ? { y } : {}), ...(x !== undefined ? { x } : {}) };
  }

  /** Whether a drag shows the value it would rest on: the default system's `snapping.feedback.showValue`. */
  private showsDragValue(): boolean {
    const snapping = (this.system as (DislCoordinateSystem & { snapping?: Record<string, unknown> }) | undefined)?.snapping;
    return isObject(snapping?.feedback) && snapping.feedback.showValue === true;
  }

  private dragHint(): LabelDeclaration {
    return this.bindings.dragHint ?? fail("the snapping shows the value a drag would rest on; the module states no dragHint to show it with.");
  }

  /** The view-fixed rulers of a coordinate system's axes. */
  private rulers(system: DislCoordinateSystem | undefined): RulerDeclaration[] {
    if (system === undefined || this.bindings.ownRulers === true) {
      return [];
    }

    return (["x", "y"] as const).flatMap((axisName): RulerDeclaration[] => {
      const declared = system[axisName];
      if (declared === undefined) {
        return [];
      }

      const axis = this.axis(declared);
      const ruler = axis.ruler;
      if (ruler === undefined || ruler.visible === false) {
        return [];
      }

      if (axisName !== "x" || ruler.position !== "bottom" || ruler.attach !== "view" || axis.valueType !== "yearMonth" || !isObject(axis.scale)) {
        return fail("only a view-fixed bottom ruler over a month-indexed horizontal axis is compiled.");
      }

      const unitMonths = this.unitMonths(this.diagramValue(axis.scale.unit));
      const minUnit = ruler.minUnit;
      const finest = minUnit === undefined ? 1 : this.unitMonths(this.diagramValue(minUnit));
      const spacings = new Set(ruler.levels.map((level) => level.minSpacingPx));
      if (spacings.size !== 1) {
        fail("the ruler's levels ask for different spacings; the library spaces every rung alike.");
      }

      const ladder = ruler.levels
        .filter((level) => this.unitMonths(level.unit) >= finest)
        .map((level): RulerRung => ({
          every: RUNG_STEPS[level.unit] ?? fail(`the ruler level "${level.unit}" has no library rung.`),
          label: level.format.replaceAll("uuuu", "yyyy") as RulerRung["label"],
        }));
      const [minSpacingPx] = [...spacings];
      return [{
        orientation: "horizontal",
        edge: "bottom",
        scale: { unit: "month", unitsPerStep: axis.scale.size / unitMonths, origin: String(axis.origin) },
        ladder,
        ...(minSpacingPx !== undefined ? { minSpacingPx } : {}),
      }];
    });
  }

  /** The canvas's one filter box, keyed by the attribute it filters on, with the legend beneath it. */
  private filter(): FilterDeclaration | undefined {
    const filters = Object.entries(this.spec.notation.canvas?.filters ?? {}).filter(([, filter]) => !isKeptSwitch(filter));
    if (filters.length === 0) {
      return undefined;
    }

    if (filters.length > 1) {
      fail("the library draws one filter box; the specification declares several filters.");
    }

    const [attribute, filter] = filters[0]!;
    const legend = this.spec.notation.canvas?.legend;
    const entries = legend === undefined || legend.visible === false
      ? []
      : (legend.entries ?? []).flatMap((enumName) => {
        const values = this.spec.metamodel.enums?.[enumName]?.values ?? fail(`the legend names "${enumName}", which is no enum.`);
        return Object.entries(values).map(([value, entry]) => ({
          caption: entry.label ?? value,
          swatchClass: this.bindings.legendSwatchClass?.(enumName, value) ?? fail("the legend needs the module's legendSwatchClass."),
        }));
      });

    return {
      field: `payload.${attribute}`,
      label: filter.label,
      ...(filter.appliesTo !== undefined ? { elementTypes: filter.appliesTo.map((type) => this.typeId(type)) } : {}),
      ...(entries.length > 0 ? { legend: entries } : {}),
    };
  }

  /**
   * The canvas's switches: each filter drawn as a switch whose value is kept with the diagram (DISL 0.4
   * §6.13.1). The canvas holds no value for one; it shows what the model's background says under the
   * name the persistence binds the filter to, and raises a request under the filter's own name.
   */
  private switches(): SwitchDeclaration[] {
    return Object.entries(this.spec.notation.canvas?.filters ?? {})
      .filter(([, filter]) => isKeptSwitch(filter))
      .map(([name, filter]) => {
        const bound = this.spec.persistence?.view?.bind?.filters?.values?.[name] ?? fail(`the kept filter "${name}" is bound to no name by persistence.view.bind.filters.`);
        return { id: name, caption: filter.label, on: `payload.${bound}` };
      });
  }

  /**
   * Whether the canvas background has a menu in a viewpoint: a set for `diagram` whose `when` holds there.
   * The canvas has no read-only state of its own, so a menu there "wherever the diagram can be edited"
   * (`!env.readOnly`) is there, and its entries are the backend's answer for the diagram as it is.
   */
  private backgroundMenu(viewpoint: string | undefined): boolean {
    return (this.spec.toolbox?.contextMenus ?? []).some((menu) => {
      if (!menu.for.includes("diagram")) {
        return false;
      }
      if (menu.when === undefined) {
        return true;
      }
      const readOnly = /^(!?)\s*env\.readOnly$/.exec(menu.when.trim());
      if (readOnly !== null) {
        return readOnly[1] === "!";
      }
      const match = /^env\.viewpoint\s*==\s*'([^']+)'$/.exec(menu.when.trim());
      return match !== null ? match[1] === viewpoint : fail(`the canvas menu's when "${menu.when}" is not a viewpoint test.`);
    });
  }

  private dragging(elementTypes: readonly ElementTypeDefinition[]): DiagramDefinition["dragging"] {
    return elementTypes.some((type) => type.draggable !== false) ? "enabled" : "disabled";
  }

  // ---- layout and viewpoints ----------------------------------------------------------------

  /**
   * The canvas's layout mode for a layout algorithm: the standard `rowPacked` (DISL 0.3 §10.1) packs rows;
   * the host computes `rows` (§10.2), `tidyTree` (§10.3), `circular`, `layered` and a plugin's layout and sends positions, and
   * `none` leaves them where they are, so the canvas draws them by hand.
   */
  private layoutMode(algorithmName: string | undefined): LayoutMode {
    if (algorithmName === undefined) {
      return "manual";
    }

    const algorithm = this.spec.layout?.algorithms?.[algorithmName] ?? fail(`layout "${algorithmName}" is not declared.`);
    if (algorithm.algorithm === "rowPacked") {
      return "row-packed";
    }

    if (algorithm.algorithm.startsWith("plugin:") || ["none", "rows", "tidyTree", "circular", "layered"].includes(algorithm.algorithm)) {
      return "manual";
    }

    return fail(`layout algorithm "${algorithm.algorithm}" has no library mode.`);
  }

  private layout(
    rulers: readonly RulerDeclaration[],
    backgroundMenu: boolean,
    dragging: DiagramDefinition["dragging"],
  ): LayoutDefinition {
    const defaultName = this.defaultViewpoint?.[0];
    const rings = this.rings(this.defaultViewpoint?.[1].layout ?? this.spec.layout?.default);
    if (rings !== undefined) {
      return rings;
    }

    const defaultMode = this.layoutMode(this.defaultViewpoint?.[1].layout ?? this.spec.layout?.default);
    const variants = Object.entries(this.spec.viewpoints ?? {}).filter(([, viewpoint]) => viewpoint.variantOf !== undefined);
    if (variants.length === 0) {
      return { modes: [defaultMode] };
    }

    if (variants.length > 1 || variants[0]![1].variantOf !== defaultName) {
      fail("the library toggles between the default viewpoint and one variant of it.");
    }

    const [variantName, variant] = variants[0]!;
    const mode = this.layoutMode(variant.layout);
    const nodes = this.nodeNames.map((name) => [name, { ...this.spec.notation.nodes[name]!, ...variant.notation?.nodes?.[name] } as DislNodeNotation] as const);
    const variantTypes = nodes.map(([name, node]) => this.elementType(name, node));
    const variantRulers = this.rulers(this.systemNamed(variant.coordinateSystem));
    const variantMenu = this.backgroundMenu(variantName);
    const variantDragging = this.dragging(variantTypes);

    const overrides: Partial<Omit<DiagramDefinition, "layout">> = {
      elementTypes: variantTypes,
      ...(variantDragging !== dragging ? { dragging: variantDragging } : {}),
      ...(JSON.stringify(variantRulers) !== JSON.stringify(rulers) ? { chrome: { rulers: variantRulers } } : {}),
      ...(variantMenu !== backgroundMenu ? { backgroundMenu: variantMenu } : {}),
    };

    return {
      modes: [defaultMode, mode],
      ...(variant.toggle !== undefined ? { toggle: { caption: variant.toggle.label, on: mode } } : {}),
      ...(mode === "row-packed" ? { rowPacked: this.rowPacked(variant, nodes) } : {}),
      modeOverrides: { [mode]: overrides },
    };
  }

  /**
   * A force layout with rings (DISL 0.4 §10): the library's radiating layout, each ring the concrete
   * types its tier names, from the centre outwards. Where the layout respects what the reader pinned,
   * a pinned element is the payload's to say.
   */
  private rings(algorithmName: string | undefined): LayoutDefinition | undefined {
    const algorithm = algorithmName === undefined ? undefined : this.spec.layout?.algorithms?.[algorithmName];
    if (algorithm?.algorithm !== "force") {
      return undefined;
    }

    const tiers = algorithm.force?.tiers ?? fail(`the force layout "${algorithmName}" names no tiers; the library's force layout places types on rings.`);
    return {
      modes: ["tiered-force"],
      tiers: tiers.map((tier) => this.concreteTypes(tier).map((type) => this.typeId(type))),
      ...(this.spec.layout?.respect === "pinned" ? { pinned: "payload.pinned" } : {}),
    };
  }

  /** The row-packed layout: the width the viewpoint binds, the lanes' gap, the rows' step. */
  private rowPacked(variant: DislViewpoint, nodes: readonly (readonly [string, DislNodeNotation])[]): NonNullable<LayoutDefinition["rowPacked"]> {
    const algorithm = this.spec.layout!.algorithms![variant.layout!]!;
    const packed = algorithm.rowPacked;
    const sized = nodes.filter(([name]) => variant.notation?.nodes?.[name]?.size?.width !== undefined);
    const widths = new Set(sized.map(([, node]) => JSON.stringify(node.size!.width)));
    if (sized.length === 0 || widths.size !== 1) {
      fail("the row-packed viewpoint must bind one width, shared by the types it sizes.");
    }

    const system = this.systemNamed(variant.coordinateSystem);
    const rows = system?.y === undefined ? undefined : this.axis(system.y);
    if (rows === undefined || typeof rows.scale !== "number") {
      return fail("the row-packed viewpoint's system needs a linear row axis.");
    }

    return {
      width: this.binding(sized[0]![1].size!.width, "the row-packed width"),
      gap: packed?.gap ?? 0,
      types: sized.map(([name]) => this.typeId(name)),
      rowStep: rows.scale,
      followConnections: packed?.followConnections !== undefined,
    };
  }
}

/** The numbers of a dash list as CEL writes it (`5.0, 4.0`). */
function dashList(text: string, where: string): readonly number[] {
  const entries = text.split(",").map((entry) => entry.trim()).filter((entry) => entry !== "");
  const numbers = entries.map(Number);
  if (numbers.some((entry) => !Number.isFinite(entry))) {
    fail(`${where} lists "${text}", which is not a list of numbers.`);
  }
  return numbers;
}

/** Every CEL text in a style, wherever a colour or a number is bound. */
function celTextsIn(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value.flatMap(celTextsIn);
  }
  if (!isObject(value)) {
    return [];
  }
  return Object.entries(value).flatMap(([key, entry]) => (key === "cel" && typeof entry === "string" ? [entry] : celTextsIn(entry)));
}

/** A value DISL binds rather than writes (`{cel}`, `{token}`, `{attribute}`): replaced whole by a subtype's, never merged into. */
function isBound(value: Readonly<Record<string, unknown>>): boolean {
  return "cel" in value || "token" in value || "attribute" in value;
}

/**
 * A subtype's notation over its supertype's, merged per key as DISL resolves a visual property
 * (6.6, *Resolution of a visual property*): an object the subtype states is merged into the one it
 * inherits, and anything else it states - a list, a scalar, a bound value - replaces it.
 */
function mergedPerKey<T>(inherited: T, own: T): T {
  if (!isObject(inherited) || !isObject(own) || isBound(own) || isBound(inherited)) {
    return own;
  }

  const merged: Record<string, unknown> = { ...inherited };
  for (const [key, value] of Object.entries(own)) {
    merged[key] = key in merged ? mergedPerKey(merged[key], value) : value;
  }
  return merged as T;
}

/**
 * Notation looked up along the supertype chain (DISL 4.7): an abstract type is not drawn; a type with
 * notation of its own draws it merged per key over its supertypes'; and a concrete type with none draws
 * its nearest supertype's, in that supertype's place, in the metamodel's order.
 */
function inherited<T>(notation: Readonly<Record<string, T>>, types: Readonly<Record<string, { abstract?: boolean; extends?: string | readonly string[] }>>): Record<string, T> {
  const chain = (type: string): string[] => linearisation(type, types);
  const resolved = (type: string): T =>
    chain(type).reverse().filter((name) => notation[name] !== undefined).reduce<T | undefined>((merged, name) => (merged === undefined ? notation[name]! : mergedPerKey(merged, notation[name]!)), undefined)!;
  const nearest = (type: string): string | undefined => chain(type).find((name) => notation[name] !== undefined);

  const result: Record<string, T> = {};
  for (const name of Object.keys(notation)) {
    if (types[name]?.abstract !== true) {
      result[name] = resolved(name);
    }
    for (const [other, declared] of Object.entries(types)) {
      if (declared.abstract !== true && notation[other] === undefined && nearest(other) === name) {
        result[other] = resolved(other);
      }
    }
  }
  return result;
}

/** The specification with its node and edge notation resolved along the supertype chain. See {@link inherited}. */
function inheritedNotation(spec: DislDocument): DislDocument {
  const nodes = inherited(spec.notation.nodes, spec.metamodel.types);
  const edges = spec.notation.edges === undefined ? undefined : inherited(spec.notation.edges, spec.metamodel.relations ?? {});
  return { ...spec, notation: { ...spec.notation, nodes, ...(edges !== undefined ? { edges } : {}) } };
}

/** A DISL notation as the library's diagram definition. See this file's opening comment. */
/** A theme token as its CSS custom property: `color.text.muted` is `--color-text-muted`. */
function tokenProperty(token: string): string {
  return `--${token.replace(/\./g, "-")}`;
}

export function compileNotation(spec: DislDocument, bindings: NotationBindings, options: CompileOptions = {}): DiagramDefinition {
  return new Compiler(inheritedNotation(spec), bindings, options).compile();
}
