import {
  holds,
  resolveNumber,
  resolveNumberAt,
  resolveOne,
  resolveOneAt,
  valueAtPath,
  type Binding,
  type BindingSource,
  type FieldBinding,
  type PartsBinding,
  type TemplateBinding,
} from "./binding";
import type {
  DecorationDeclaration,
  DecorationGlyph,
  DeclaredNumber,
  LabelTypography,
  MarkerKind,
  ShapeBounds,
} from "./diagramDefinition";

/**
 * `decorations` — the ornaments five renderers draw with no shared component at all.
 *
 * Of the twenty-eight custom shapes measured, twelve wrap exactly one shared component, about
 * ten wrap one and add raw `<text>` — which `labels` now covers — and <b>five use no component
 * whatsoever</b>. Those five draw four things between them, and this is the vocabulary for
 * exactly those four: a stub, a badge, an annotation, a target.
 *
 * <b>Drawn, and nothing else.</b> No hit-testing, no gesture, no anchor: anything needing those
 * is an element type, and holding that line is what keeps this a small closed set rather than a
 * second element system growing beside the first.
 *
 * Resolution is pure, like `layoutLabels`, so the acceptance can be asserted without a canvas.
 */

/** One decoration, resolved and ready to draw. Geometry is absolute canvas units. */
export interface ResolvedDecoration {
  glyph: DecorationGlyph;
  from: { x: number; y: number };
  to: { x: number; y: number };
  radius: number;
  width: number;
  height: number;
  d?: string;
  marker?: string;
  text?: string;
  textAt: { x: number; y: number };
  textAnchor: "start" | "middle" | "end";
  typography?: LabelTypography;
  className?: string;
  markerEnd?: MarkerKind;
  tooltip?: string;
  data?: Readonly<Record<string, string>>;
  role?: string;
  accessibleName?: string;
  index: number;
}

/**
 * A declared number: written outright, or read from the model.
 *
 * A binding that does not resolve to a number falls back rather than throwing - the same rule
 * every other resolution here follows. The fallback is the caller's default, so a decoration
 * with one unresolvable coordinate still draws somewhere sensible instead of vanishing; a
 * decoration that vanished on a typo would be indistinguishable from one nobody declared.
 */
function numberOf(value: DeclaredNumber | undefined, source: BindingSource, fallback: number): number {
  if (value === undefined) {
    return fallback;
  }

  if (typeof value === "number") {
    return value;
  }

  return resolveNumber(value, source) ?? fallback;
}

function pointOf(
  point: { x: DeclaredNumber; y: DeclaredNumber } | undefined,
  source: BindingSource,
  centre: { x: number; y: number },
  anchor: "centre" | "canvas" = "centre",
): { x: number; y: number } {
  if (!point) {
    return centre;
  }

  // Centre-relative by default - what a fixed ornament means - and absolute when the
  // declaration says its numbers already are, which is what a bound `bounds.*` resolves to.
  const origin = anchor === "canvas" ? { x: 0, y: 0 } : centre;
  return { x: origin.x + numberOf(point.x, source, 0), y: origin.y + numberOf(point.y, source, 0) };
}

/**
 * Every decoration a type declares, resolved against one element.
 *
 * A declaration whose `when` fails contributes nothing, exactly as a label's does. Geometry is
 * relative to the element's centre on the way in and absolute on the way out, so the renderer
 * does no arithmetic of its own — the same division of labour that keeps `layoutLabels`
 * testable apart from React.
 */
export function resolveDecorations(
  declarations: readonly DecorationDeclaration[] | undefined,
  source: BindingSource,
  bounds: ShapeBounds,
): readonly ResolvedDecoration[] {
  if (!declarations || declarations.length === 0) {
    return [];
  }

  const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
  const resolved: ResolvedDecoration[] = [];

  declarations.forEach((declaration, index) => {
    if (!holds(declaration.when, source)) {
      return;
    }

    if (declaration.each !== undefined) {
      resolveEach(declaration, source, centre, index, resolved);
      return;
    }

    const from = pointOf(declaration.from, source, centre, declaration.anchor);
    resolved.push({
      glyph: declaration.glyph,
      from,
      to: pointOf(declaration.to, source, from, declaration.anchor),
      radius: numberOf(declaration.radius, source, 0),
      width: numberOf(declaration.width, source, 0),
      height: numberOf(declaration.height, source, 0),
      d: declaration.d ? (resolveOne(declaration.d, source) ?? undefined) : undefined,
      marker: declaration.marker,
      text: declaration.text ? (resolveOne(declaration.text, source) ?? undefined) : undefined,
      textAt: pointOf(declaration.textAt, source, from, declaration.anchor),
      textAnchor: declaration.textAnchor ?? "start",
      typography: declaration.typography,
      // Bound, so a problem mark can be coloured by a severity the library has never heard of.
      className:
        typeof declaration.className === "string" || declaration.className === undefined
          ? declaration.className
          : (resolveOne(declaration.className, source) ?? undefined),
      markerEnd: declaration.markerEnd,
      tooltip: declaration.tooltip ? (resolveOne(declaration.tooltip, source) ?? undefined) : undefined,
      data: resolvedData(declaration.data, (binding) => resolveOne(binding, source)),
      role: declaration.accessibility?.role,
      accessibleName: declaration.accessibility?.label ? (resolveOne(declaration.accessibility.label, source) ?? undefined) : undefined,
      index,
    });
  });

  return resolved;
}

/**
 * One decoration per entry of a collection, stepped along from the declaration's own origin.
 *
 * <b>Rooted at the ITEM</b>, like every other `each` in this vocabulary: `{ path: "glyph" }`
 * inside one reads the entry's own field, not the element's. An author who had to remember that
 * this one rooted differently would get it wrong silently, because a mis-rooted path resolves
 * to nothing rather than failing.
 */
function resolveEach(
  declaration: DecorationDeclaration,
  source: BindingSource,
  centre: { x: number; y: number },
  index: number,
  into: ResolvedDecoration[],
): void {
  const items = itemsOf(declaration.each!, source);
  const origin = pointOf(declaration.from, source, centre, declaration.anchor);
  const step = declaration.step ?? { x: 0, y: 0 };

  items.forEach((item, entry) => {
    const at = { x: origin.x + step.x * entry, y: origin.y + step.y * entry };
    into.push({
      glyph: declaration.glyph,
      from: at,
      to: at,
      radius: itemNumber(declaration.radius, item, 0),
      width: itemNumber(declaration.width, item, 0),
      height: itemNumber(declaration.height, item, 0),
      d: declaration.d ? (resolveOneAt(asItemBinding(declaration.d), item) ?? undefined) : undefined,
      marker: declaration.marker,
      text: declaration.text ? (resolveOneAt(asItemBinding(declaration.text), item) ?? undefined) : undefined,
      textAt: at,
      textAnchor: declaration.textAnchor ?? "start",
      typography: declaration.typography,
      className:
        typeof declaration.className === "string" || declaration.className === undefined
          ? declaration.className
          : (resolveOneAt(asItemBinding(declaration.className), item) ?? undefined),
      markerEnd: declaration.markerEnd,
      tooltip: declaration.tooltip ? (resolveOneAt(asItemBinding(declaration.tooltip), item) ?? undefined) : undefined,
      data: resolvedData(declaration.data, (binding) => resolveOneAt(asItemBinding(binding), item)),
      role: declaration.accessibility?.role,
      accessibleName: declaration.accessibility?.label
        ? (resolveOneAt(asItemBinding(declaration.accessibility.label), item) ?? undefined)
        : undefined,
      // The declaration's index and the entry's, so React keys stay stable across a re-render -
      // and OFFSET past the plain declarations, because `index * 1000 + entry` collides with
      // declaration 1 on the second entry of declaration 0. A test caught that, and the failure
      // it prevents is two glyphs sharing a key and one of them vanishing on a re-render.
      index: (index + 1) * 1000 + entry,
    });
  });
}

/**
 * The `data-*` attributes a declaration names, resolved.
 *
 * An attribute whose binding resolves to nothing is LEFT OUT rather than written as the string
 * "undefined" - the same rule every other resolution here follows, and the difference between
 * an absent flag and one that reads as present.
 */
function resolvedData(
  declared: Readonly<Record<string, string | Binding>> | undefined,
  resolve: (binding: Binding) => string | null,
): Readonly<Record<string, string>> | undefined {
  if (declared === undefined) {
    return undefined;
  }

  const out: Record<string, string> = {};
  for (const [name, value] of Object.entries(declared)) {
    const resolved = typeof value === "string" ? value : resolve(value);
    if (resolved !== null) {
      out[name] = resolved;
    }
  }

  return out;
}

/** The entries a collection binding names, or none. */
function itemsOf(each: Binding, source: BindingSource): readonly unknown[] {
  if (!("path" in each)) {
    return [];
  }

  const value = valueAtPath(each.path, source);
  return Array.isArray(value) ? value : [];
}

/** A binding used per item: only the field and template forms make sense rooted at an entry. */
function asItemBinding(binding: Binding): FieldBinding | TemplateBinding | PartsBinding {
  return binding as FieldBinding | TemplateBinding | PartsBinding;
}

function itemNumber(value: DeclaredNumber | undefined, item: unknown, fallback: number): number {
  if (value === undefined) {
    return fallback;
  }

  if (typeof value === "number") {
    return value;
  }

  return resolveNumberAt(asItemBinding(value), item) ?? fallback;
}
