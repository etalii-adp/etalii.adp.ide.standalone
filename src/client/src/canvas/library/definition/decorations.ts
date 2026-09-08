import { holds, resolveNumber, resolveOne, type BindingSource } from "./binding";
import type {
  DecorationDeclaration,
  DecorationGlyph,
  DeclaredNumber,
  LabelTypography,
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
): { x: number; y: number } {
  if (!point) {
    return centre;
  }

  return { x: centre.x + numberOf(point.x, source, 0), y: centre.y + numberOf(point.y, source, 0) };
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

    const from = pointOf(declaration.from, source, centre);
    resolved.push({
      glyph: declaration.glyph,
      from,
      to: pointOf(declaration.to, source, from),
      radius: numberOf(declaration.radius, source, 0),
      width: numberOf(declaration.width, source, 0),
      height: numberOf(declaration.height, source, 0),
      d: declaration.d ? (resolveOne(declaration.d, source) ?? undefined) : undefined,
      marker: declaration.marker,
      text: declaration.text ? (resolveOne(declaration.text, source) ?? undefined) : undefined,
      textAt: pointOf(declaration.textAt, source, from),
      textAnchor: declaration.textAnchor ?? "start",
      typography: declaration.typography,
      className: declaration.className,
      index,
    });
  });

  return resolved;
}
