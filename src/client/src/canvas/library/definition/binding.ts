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

/** One value, read from the model. */
export interface FieldBinding {
  path: BindingPath;
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
}

export type Binding = FieldBinding | TemplateBinding | CollectionBinding;

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
  return { element: source.element, payload: source.payload };
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
export function resolveOneAt(binding: FieldBinding | TemplateBinding, root: unknown): string | null {
  return resolveAgainst(binding, root);
}

/** The per-item numeric form, for a band's `start` or a region's `width`. */
export function resolveNumberAt(binding: FieldBinding | TemplateBinding, root: unknown): number | null {
  const text = resolveAgainst(binding, root);
  if (text === null) {
    return null;
  }

  const value = Number(text);
  return Number.isFinite(value) ? value : null;
}

function resolveAgainst(binding: FieldBinding | TemplateBinding, root: unknown): string | null {
  if (!holds(binding.when, root)) {
    return null;
  }

  return "template" in binding ? fillTemplate(binding.template, root) : textOf(valueAt(root, binding.path));
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

  return lines;
}
