import type { DiagramModelElement } from "../api/diagramModel";

/**
 * A binding: a declared path into the model, optionally composed, repeated or conditioned.
 *
 * <b>A BINDING CANNOT CALL ANYTHING, AND THAT IS THE POINT OF THE WHOLE FILE.</b>
 *
 * This is the mechanism the declarative-modules specification rests on, and the one place it
 * can be lost. A binding that could invoke a module function would be the escape hatch under a
 * new name: `CustomShapeRef` was a function, twenty-eight modules used it, zero used the
 * declarative vocabulary beside it, and this specification exists because of that. A callable
 * binding would rebuild that door in a week and nobody would notice, because it would arrive
 * looking like a convenience.
 *
 * <b>So the prohibition is in the type rather than in a comment.</b> Every field below is a
 * string, a number, a literal union, or another Binding - a function is assignable to none of
 * them, so `{ path: () => x }` does not compile and `{ text: (e) => e.name }` has nowhere to
 * go. That is a stronger guarantee than a rule someone has to remember at the moment they are
 * tired and the deadline is close. The conformance guard checks the same thing from the
 * outside, because a type can be defeated by `as unknown as`; two mechanisms, one claim.
 *
 * <b>The design put this file first deliberately.</b> Labels, decorations, backgrounds and
 * actions are all "a declared path into the model, formatted" - four features over one
 * mechanism. Getting the mechanism wrong makes all four wrong.
 *
 * Nothing here touches React, the DOM or the canvas: resolution is a pure function of a
 * binding and a source, which is what lets a guard, a test and a serialiser all handle it.
 */

/**
 * A dotted path, rooted at `element` or `payload` - `payload.name`, `element.label`,
 * `payload.rows`. Inside an `each`, a path is rooted at the ITEM instead; see
 * {@link CollectionBinding}.
 *
 * A path is a string rather than a typed accessor because it has to survive a guard reading it
 * as text and, later, a serialiser. Its cost is that a typo resolves to nothing rather than
 * failing to compile - which is why {@link resolveOne} draws nothing instead of throwing, and
 * why the sufficiency table names the fields each module needs.
 */
export type BindingPath = string;

/** What a `when` asks of a path. Data, like everything else here - never a predicate. */
export type Condition =
  | {
      path: BindingPath;
      /**
       * `present`/`absent` ask whether the path resolves at all; `non-empty`/`empty` ask about
       * a string's or a list's length - the measured need is `badges.length > 0`, which ten
       * renderers spell as a ternary today.
       */
      is: "present" | "absent" | "non-empty" | "empty" | "true" | "false";
    }
  | { path: BindingPath; equals: string | number | boolean };

/**
 * How a number reaches the page, when the number on the model is not the number to show.
 *
 * <b>Added because sufficiency row 26 could not be filled</b>, and for no other reason. The
 * timeline's drag hint is the time under the pointer: the element's x, times the module's
 * seconds per unit, plus its origin, formatted as a clock. Three of those four are model
 * fields; the arithmetic between them was the only thing a binding could not say, and the
 * alternative on offer was a module-supplied function - the escape hatch this whole file exists
 * to keep shut.
 *
 * <b>Two terms, not an expression language.</b> `times` then `plus`, in that order, and nothing
 * else: no nesting, no operators, no precedence to get wrong. A row needing more than this is a
 * row that has found a real gap, and it goes in the register rather than growing this type.
 *
 * `times` and `plus` may name paths, which resolve against the SAME root as the value - a
 * scale living beside the field it scales.
 */
export interface NumberFormat {
  times?: BindingPath | number;
  plus?: BindingPath | number;
  /**
   * Wraps the result into `0..modulo-1`.
   *
   * <b>Sufficiency row 1 justifies the third term</b>, and it is the last one: ansible colours a
   * play by `playIndex % PALETTE_SLOTS`, so that the fourteenth play reuses the first play's
   * colour rather than running out. Without it a palette slot is arithmetic no declaration can
   * do, and the module would keep a renderer for a class name.
   */
  modulo?: BindingPath | number;
  /** How the result reads. `number` keeps it as it is; the temporal formats read it as epoch seconds. */
  format?: TemporalFormat;
}

/**
 * A closed set of formats for a number, shared by bindings and by a ruler's ladder.
 *
 * <b>UTC throughout</b>, deliberately: a document shows the same text on every machine, and a
 * formatter using local time would shift every label by the viewer's offset - a defect
 * invisible to whoever wrote it, because they would see the right answer on their own screen.
 */
export type TemporalFormat = "yyyy" | "MMM" | "MMM yyyy" | "d MMM" | "HH:mm" | "HH:mm:ss" | "number";

/** One value, read from the model. */
export interface FieldBinding {
  path: BindingPath;
  when?: Condition;
  /**
   * A count with its noun agreeing - `1 job`, `3 jobs`.
   *
   * Sufficiency row 3 justifies it: `jobCountLabel` is the only hand-written formatter among
   * the twenty-eight that a template cannot reach, because a template can interpolate the
   * number but cannot decline the noun. Two forms rather than a plural-rules library, because
   * two is what every measured case needs and a library would decide policy the modules have
   * never asked for.
   */
  plural?: { one: string; other: string };
  /** Arithmetic and formatting over a numeric field. See {@link NumberFormat}. */
  number?: NumberFormat;
}

/**
 * Several values as one line, with the ones that resolve to nothing left out ENTIRELY.
 *
 * <b>Sufficiency rows 3 and 10 justify this and templates cannot.</b> `mode · default · 2
 * overrides` drops to `mode · 2 overrides` when the frame is not the default one - and a
 * template writing `"{a} · {b} · {c}"` leaves the separators behind, which is the stated limit
 * of `fillTemplate`. Here the join happens after the parts are known, so an absent part costs
 * nothing rather than a stray delimiter.
 */
export interface PartsBinding {
  parts: readonly (FieldBinding | TemplateBinding)[];
  /** What goes between the parts that survived. */
  join: string;
  when?: Condition;
}

/**
 * One value, composed from several. `{payload.predicate}: {payload.value}` is the rdf card's
 * row, which is the shape this was drawn from.
 */
export interface TemplateBinding {
  /** Literal text with `{path}` placeholders. An unresolved placeholder contributes nothing. */
  template: string;
  when?: Condition;
}

/**
 * One value per entry of a list - the case that decides whether this vocabulary is sufficient.
 *
 * A fixed set of named slots would serve c4 and leave the rdf family exactly where it is: the
 * renderers that hand-roll a LOOP are why this binds to a collection. `each` resolves once per
 * item, and <b>its paths are rooted at the item</b>, not at the element - so
 * `{ path: "payload.rows", each: { template: "{predicate}: {value}" } }` reads `predicate` off
 * each row.
 */
export interface CollectionBinding {
  path: BindingPath;
  each: FieldBinding | TemplateBinding;
  when?: Condition;
  /**
   * Set, and the entries become ONE line joined by this - `Dataset · Table · View`.
   *
   * Ten of the twenty-eight sufficiency rows draw a badge strip that way, and stacking them as
   * lines would be a different drawing rather than the same one declared. Unset, the entries
   * stay separate lines, which is what the rdf family's rows need.
   */
  join?: string;
}

export type Binding = FieldBinding | TemplateBinding | CollectionBinding | PartsBinding;

/**
 * What a binding resolves against: the element the canvas already holds, plus the module's own
 * payload.
 *
 * The payload is separate rather than merged into the element because the element is the
 * library's contract and the payload is the module's - a merge would let a module's field name
 * shadow `x`, and the collision would surface as a drawing that is subtly wrong rather than as
 * an error. `DiagramModelElement` gains no payload field here: task 6 decides what crosses the
 * wire, and this file must not pre-empt it.
 */
export interface BindingSource {
  element: DiagramModelElement;
  payload?: unknown;
  /**
   * What the canvas is doing to this element right now. See {@link InteractionState}.
   *
   * <b>A third root rather than a fourth mechanism.</b> A condition already reads a path, so
   * `{ path: "state.selected", is: "true" }` needs no new syntax and no new resolver - the one
   * thing that was missing was the state being IN the root at all.
   */
  state?: InteractionState;
}

/**
 * The canvas's own view of an element, readable by a condition under the `state` root.
 *
 * <b>Twenty-three of the twenty-eight sufficiency rows need this</b> (register entry G2): every
 * one of them puts a class on `selected`, `dragging` or `connectTarget`, and a vocabulary that
 * could not see them would have left every migrated element unable to look selected. That is
 * not a small omission dressed up - it is the difference between a declaration that can replace
 * a renderer and one that cannot.
 *
 * <b>A closed set, and deliberately small.</b> These four are what the library itself knows.
 * `expanded` and `focused`, which two modules also style on, are the MODULE's state and reach a
 * declaration as payload fields - putting them here would mean the library holding state it has
 * no way to be right about.
 */
export interface InteractionState {
  selected?: boolean;
  dragging?: boolean;
  connectTarget?: boolean;
  editing?: boolean;
}

/** True when `value` is a record we can index. Arrays are excluded: a path step is a name. */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * Walks a dotted path, returning `undefined` for anything that does not resolve.
 *
 * Every failure is the same failure - a missing root, a missing step, a step through a
 * primitive - because the caller's answer is the same in all three: draw nothing. A resolver
 * that distinguished them would be inviting a caller to branch on why a field was absent,
 * which is module logic arriving through the back door.
 */
function valueAt(root: unknown, path: BindingPath): unknown {
  let current = root;
  for (const step of path.split(".")) {
    if (!isRecord(current)) {
      return undefined;
    }

    current = current[step];
  }

  return current;
}

/** A primitive rendered as text; anything else - an object, a function, null - is nothing. */
function textOf(value: unknown): string | null {
  if (typeof value === "string") {
    return value;
  }

  if (typeof value === "number" || typeof value === "boolean") {
    return String(value);
  }

  return null;
}

function rootOf(source: BindingSource): Record<string, unknown> {
  // `state` sits beside `element` and `payload` rather than inside either: it is neither the
  // library's model nor the module's data, and merging it into one of them would let a payload
  // field named `selected` decide how selection looks.
  return { element: source.element, payload: source.payload, state: source.state ?? {} };
}

/** Whether a condition holds. An unresolvable path is `absent`, never an error. */
export function holds(condition: Condition | undefined, source: BindingSource | unknown): boolean {
  if (!condition) {
    return true;
  }

  const root = isBindingSource(source) ? rootOf(source) : source;
  const value = valueAt(root, condition.path);

  if ("equals" in condition) {
    return value === condition.equals;
  }

  const empty =
    value === undefined ||
    value === null ||
    value === "" ||
    (Array.isArray(value) && value.length === 0);

  switch (condition.is) {
    case "present":
      return value !== undefined && value !== null;
    case "absent":
      return value === undefined || value === null;
    case "non-empty":
      return !empty;
    case "empty":
      return empty;
    case "true":
      return value === true;
    case "false":
      return value === false;
  }
}

function isBindingSource(value: unknown): value is BindingSource {
  return isRecord(value) && isRecord(value.element) && typeof value.element.id === "string";
}

/**
 * Fills `{path}` placeholders, dropping the ones that do not resolve.
 *
 * <b>A stated limit rather than a clever rule</b>: literal text between two optional values
 * survives when only one resolves, so `"{a} - {b}"` with no `b` reads `"a -"`. The remedy is a
 * `when`, or two labels rather than one template - not punctuation-aware trimming, which would
 * be a formatter guessing at intent. Runs of whitespace collapse and the result is trimmed,
 * which handles the common `"{predicate}: {value} {annotation}"` case where the tail is absent.
 */
function fillTemplate(template: string, root: unknown): string | null {
  const filled = template.replace(/\{([^{}]+)\}/g, (_match, path: string) => textOf(valueAt(root, path.trim())) ?? "");
  const collapsed = filled.replace(/\s+/g, " ").trim();
  return collapsed.length > 0 ? collapsed : null;
}

/**
 * One value, resolved against a ROOT rather than against a source.
 *
 * The per-item form: inside a collection, a path names a field of the item, not of the element.
 * `labels`' `each` has worked this way since task 2, and `background`'s bands and regions use
 * the same rule - because an author who had to remember that `labels.each` roots at the item
 * while `bands.each` roots at the payload would get it wrong, and would get it wrong silently,
 * since a mis-rooted path resolves to nothing rather than failing.
 */
export function resolveOneAt(binding: FieldBinding | TemplateBinding | PartsBinding, root: unknown): string | null {
  return resolveAgainst(binding, root);
}

/** The per-item numeric form, for a band's `start` or a region's `width`. */
export function resolveNumberAt(binding: FieldBinding | TemplateBinding | PartsBinding, root: unknown): number | null {
  const text = resolveAgainst(binding, root);
  if (text === null) {
    return null;
  }

  const value = Number(text);
  return Number.isFinite(value) ? value : null;
}

function resolveAgainst(binding: FieldBinding | TemplateBinding | PartsBinding, root: unknown): string | null {
  if (!holds(binding.when, root)) {
    return null;
  }

  if ("template" in binding) {
    return fillTemplate(binding.template, root);
  }

  if ("parts" in binding) {
    // Joined AFTER the parts are known, which is the whole difference from a template: an
    // absent part leaves no separator behind.
    const survivors = binding.parts.map((part) => resolveAgainst(part, root)).filter((part): part is string => part !== null && part.length > 0);
    return survivors.length > 0 ? survivors.join(binding.join) : null;
  }

  const raw = valueAt(root, binding.path);
  if (binding.number) {
    return formatNumber(raw, binding.number, root);
  }

  if (binding.plural) {
    const count = numberOfValue(raw);
    return count === null ? null : `${count} ${count === 1 ? binding.plural.one : binding.plural.other}`;
  }

  return textOf(raw);
}

/** A term of a {@link NumberFormat}: a literal, or a path resolved against the same root. */
function termOf(term: BindingPath | number | undefined, root: unknown, fallback: number): number {
  if (term === undefined) {
    return fallback;
  }

  if (typeof term === "number") {
    return term;
  }

  return numberOfValue(valueAt(root, term)) ?? fallback;
}

/**
 * A value as a number, or null.
 *
 * <b>Absent is null rather than zero, and a test caught this being wrong.</b> `Number(null)` is
 * `0`, so a missing count read as "0 jobs" and a missing instant as "1 Jan 1970" - a drawing
 * that looks like data rather than like the absence it is. The rule the whole file follows is
 * that an unresolvable path draws NOTHING; going through Number() quietly broke it for two of
 * the new forms.
 */
function numberOfValue(raw: unknown): number | null {
  const text = textOf(raw);
  if (text === null || text.trim() === "") {
    return null;
  }

  const value = Number(text);
  return Number.isFinite(value) ? value : null;
}

function formatNumber(raw: unknown, format: NumberFormat, root: unknown): string | null {
  const base = numberOfValue(raw);
  if (base === null) {
    return null;
  }

  // Times then plus, in that order, because a scale is applied before an origin is added - and
  // because an order stated once cannot be got wrong by an author guessing at precedence.
  // Times, then plus, then modulo - one order, stated once, so an author never guesses at
  // precedence and never gets a wrong drawing instead of an error.
  const scaled = base * termOf(format.times, root, 1) + termOf(format.plus, root, 0);
  const modulo = format.modulo === undefined ? undefined : termOf(format.modulo, root, 0);
  const value = modulo !== undefined && modulo > 0 ? ((scaled % modulo) + modulo) % modulo : scaled;
  return formatEpochSeconds(value, format.format ?? "number");
}

const MONTH_NAMES = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function pad(value: number): string {
  return value < 10 ? `0${value}` : String(value);
}

/**
 * A number as text, reading it as epoch seconds for every format but `number`.
 *
 * <b>Lives here rather than in `chrome` because two callers need the same answer.</b> A ruler's
 * tick and a label bound to the same instant must read identically, and two formatters that can
 * drift apart is exactly the kind of duplication this specification is removing.
 *
 * Every field is a UTC one. The guard for that is in `chrome.test.ts`, and it took two attempts
 * to write one that is not vacuous: a date probe at midnight only shifts under a negative
 * offset, and `toContain` cannot see a uniform shift at all.
 */
export function formatEpochSeconds(value: number, format: TemporalFormat): string {
  if (format === "number") {
    return String(value);
  }

  const date = new Date(value * 1000);
  switch (format) {
    case "yyyy":
      return String(date.getUTCFullYear());
    case "MMM":
      return MONTH_NAMES[date.getUTCMonth()]!;
    case "MMM yyyy":
      return `${MONTH_NAMES[date.getUTCMonth()]!} ${date.getUTCFullYear()}`;
    case "d MMM":
      return `${date.getUTCDate()} ${MONTH_NAMES[date.getUTCMonth()]!}`;
    case "HH:mm":
      return `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}`;
    case "HH:mm:ss":
      return `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:${pad(date.getUTCSeconds())}`;
  }
}

/**
 * The one value a binding yields, or null when it yields none.
 *
 * A collection binding resolved through here gives its FIRST entry, so a single-value consumer
 * handed a collection degrades rather than throwing - the caller wanted one line and gets one.
 */
export function resolveOne(binding: Binding, source: BindingSource): string | null {
  const all = resolveMany(binding, source);
  return all.length > 0 ? all[0]! : null;
}

/**
 * The one NUMBER a binding yields, or null.
 *
 * Geometry binds to numbers - a decoration's radius, a band's extent - and a declaration that
 * had to spell those as text would be a worse thing to read and a worse thing to check. Parsing
 * rather than a separate numeric binding form, because the alternative is two resolvers that
 * can disagree about what `payload.r` means.
 *
 * A value that is not a number yields null, and the caller draws nothing: exactly the rule the
 * string form follows, for exactly the reason - a typo in an authored declaration must cost one
 * ornament, not the canvas.
 */
export function resolveNumber(binding: Binding, source: BindingSource): number | null {
  const text = resolveOne(binding, source);
  if (text === null) {
    return null;
  }

  const value = Number(text);
  return Number.isFinite(value) ? value : null;
}

/**
 * Every value a binding yields: none, one, or one per entry of a collection.
 *
 * <b>Nothing here throws.</b> A path that does not resolve, a collection that is not a list, an
 * empty list and a failed condition all yield an empty array, because the drawing answer to all
 * four is the same: draw nothing. A binding is authored data that ships to users, and a
 * declaration with a typo in it must leave a gap in a diagram rather than take the canvas down.
 */
/**
 * One resolved line, and the root it was resolved against.
 *
 * <b>Added so a second column can read the SAME item its line came from.</b> A shacl row is a
 * path, a summary and a cardinality on one line, per constraint; declaring three collections
 * over the same list would resolve it three times, and - the real hazard - would let the three
 * fall out of step the moment one carried a condition, silently pairing row 2's cardinality
 * with row 3's path. Pairing them here makes that impossible rather than unlikely.
 */
export interface ResolvedEntry {
  text: string;
  /** What a per-item path resolves against: the item for a collection, the source root otherwise. */
  root: unknown;
}

/** Every line a binding yields, each with the root it came from. See {@link ResolvedEntry}. */
export function resolveEntries(binding: Binding, source: BindingSource): readonly ResolvedEntry[] {
  const root = rootOf(source);

  if (!holds(binding.when, source)) {
    return [];
  }

  if (!("each" in binding)) {
    const one = resolveAgainst(binding, root);
    return one === null ? [] : [{ text: one, root }];
  }

  const items = valueAt(root, binding.path);
  if (!Array.isArray(items)) {
    return [];
  }

  const entries: ResolvedEntry[] = [];
  for (const item of items) {
    const line = resolveAgainst(binding.each, item);
    if (line !== null) {
      entries.push({ text: line, root: item });
    }
  }

  // A joined collection is ONE line, and its root is the source rather than any one item: a
  // column beside a badge strip belongs to the element, not to the third badge.
  return binding.join !== undefined
    ? (entries.length > 0 ? [{ text: entries.map((entry) => entry.text).join(binding.join), root }] : [])
    : entries;
}

export function resolveMany(binding: Binding, source: BindingSource): readonly string[] {
  const root = rootOf(source);

  if (!holds(binding.when, source)) {
    return [];
  }

  if (!("each" in binding)) {
    const one = resolveAgainst(binding, root);
    return one === null ? [] : [one];
  }

  const items = valueAt(root, binding.path);
  if (!Array.isArray(items)) {
    return [];
  }

  const lines: string[] = [];
  for (const item of items) {
    // Rooted at the ITEM: `{predicate}` inside an `each` reads the row's own field.
    const line = resolveAgainst(binding.each, item);
    if (line !== null) {
      lines.push(line);
    }
  }

  // One joined line, or the lines themselves. An empty collection stays empty either way -
  // joining nothing must not produce an empty string that draws as a blank label.
  return binding.join !== undefined ? (lines.length > 0 ? [lines.join(binding.join)] : []) : lines;
}
