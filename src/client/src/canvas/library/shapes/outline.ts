import type { BuiltInShape, ShapeBounds, ShapePoint } from "../definition/diagramDefinition";

/**
 * One outline per shape, and the two things computed from it.
 *
 * <b>Why this file exists.</b> A shape used to be drawn in one place, hit for its edge point in
 * another and given a text position in a third, each with its own idea of where the shape is. For
 * a box those three agree by accident; for a trapezoid they do not, and the disagreement shows as
 * a label over a slanted edge or an arrowhead beside the shape rather than on it. So the outline
 * is stated once here and the drawing, the text region and the edge point all read it. They
 * cannot disagree, because there is nothing else for them to read.
 *
 * <b>What a shape without an outline means.</b> `box`, `pill`, `ellipse` and the rest keep the
 * geometry they already have, and {@link outlineOf} answers with an empty list for them - "no
 * polygon outline; use the rectangle". That is deliberate rather than an omission: replacing a
 * working circle with a 64-gon would move drawings nobody asked to move.
 */

/** The inner padding between a text region and the outline, on each side, in canvas units. */
export const TEXT_REGION_PADDING = 6;

/** The squircle: |x/a|^n + |y/b|^n = 1 with n = 4, which is what "superellipse" means here. */
const SUPERELLIPSE_EXPONENT = 4;

/** How finely the superellipse is sampled. Even, so the extremes land on samples. */
const SUPERELLIPSE_SAMPLES = 64;

/** How finely the diode's closing semicircle is sampled. */
const DIODE_ARC_SAMPLES = 24;

/**
 * The corner fractions of the straight-sided shapes, as fractions of the bounds.
 *
 * `diamond`, `hexagon` and `parallelogram` are the lists the canvas already drew, moved here
 * unchanged so their drawing does not shift; `trapezoid` is new and narrower at the bottom.
 */
const CORNER_FRACTIONS: Partial<Record<BuiltInShape, readonly (readonly [number, number])[]>> = {
  diamond: [[0.5, 0], [1, 0.5], [0.5, 1], [0, 0.5]],
  hexagon: [[0.25, 0], [0.75, 0], [1, 0.5], [0.75, 1], [0.25, 1], [0, 0.5]],
  parallelogram: [[0.2, 0], [1, 0], [0.8, 1], [0, 1]],
  trapezoid: [[0, 0], [1, 0], [0.85, 1], [0.15, 1]],
};

/** The shapes this file has an outline for - what a caller may rely on being non-empty. */
export const OUTLINED_SHAPES: readonly BuiltInShape[] = [
  "diamond",
  "hexagon",
  "parallelogram",
  "trapezoid",
  "superellipse",
  "diode",
];

function at(bounds: ShapeBounds, fx: number, fy: number): ShapePoint {
  return { x: bounds.x + (fx * bounds.width), y: bounds.y + (fy * bounds.height) };
}

/**
 * The closed outline of a shape, in canvas units, walked in one direction.
 *
 * Empty for a shape with no polygon outline - see this file's note on why that is deliberate.
 */
export function outlineOf(shape: BuiltInShape, bounds: ShapeBounds): readonly ShapePoint[] {
  const corners = CORNER_FRACTIONS[shape];
  if (corners !== undefined) {
    return corners.map(([fx, fy]) => at(bounds, fx, fy));
  }

  if (shape === "superellipse") {
    const a = bounds.width / 2;
    const b = bounds.height / 2;
    const cx = bounds.x + a;
    const cy = bounds.y + b;
    const power = 2 / SUPERELLIPSE_EXPONENT;
    return Array.from({ length: SUPERELLIPSE_SAMPLES }, (_, index) => {
      const angle = (2 * Math.PI * index) / SUPERELLIPSE_SAMPLES;
      const cos = Math.cos(angle);
      const sin = Math.sin(angle);
      return {
        x: cx + (Math.sign(cos) * Math.abs(cos) ** power * a),
        y: cy + (Math.sign(sin) * Math.abs(sin) ** power * b),
      };
    });
  }

  if (shape === "diode") {
    // A rectangle closed on the right by a semicircle: the shape reads as pointing right, which
    // is what it is for. A radius wider than the box would invert it, so it is clamped.
    const radius = Math.min(bounds.height / 2, bounds.width);
    const straightTo = bounds.x + bounds.width - radius;
    const centreY = bounds.y + (bounds.height / 2);
    const arc = Array.from({ length: DIODE_ARC_SAMPLES + 1 }, (_, index) => {
      const angle = -Math.PI / 2 + ((Math.PI * index) / DIODE_ARC_SAMPLES);
      return { x: straightTo + (Math.cos(angle) * radius), y: centreY + (Math.sin(angle) * radius) };
    });
    return [
      { x: bounds.x, y: bounds.y },
      { x: straightTo, y: bounds.y },
      ...arc,
      { x: straightTo, y: bounds.y + bounds.height },
      { x: bounds.x, y: bounds.y + bounds.height },
    ];
  }

  return [];
}

/** Whether a point lies inside a closed outline, by the even-odd ray rule; the edge counts as in. */
export function isInsideOutline(point: ShapePoint, outline: readonly ShapePoint[]): boolean {
  if (outline.length < 3) {
    return false;
  }

  let inside = false;
  for (let i = 0, j = outline.length - 1; i < outline.length; j = i++) {
    const a = outline[i];
    const b = outline[j];
    if (onSegment(point, a, b)) {
      return true;
    }
    const crosses = a.y > point.y !== b.y > point.y;
    if (crosses && point.x < a.x + (((point.y - a.y) / (b.y - a.y)) * (b.x - a.x))) {
      inside = !inside;
    }
  }
  return inside;
}

/** A point on the segment, within a hair - so a corner exactly on the edge reads as inside. */
function onSegment(point: ShapePoint, a: ShapePoint, b: ShapePoint): boolean {
  const cross = ((b.x - a.x) * (point.y - a.y)) - ((b.y - a.y) * (point.x - a.x));
  if (Math.abs(cross) > 1e-6) {
    return false;
  }
  return (
    point.x >= Math.min(a.x, b.x) - 1e-6 && point.x <= Math.max(a.x, b.x) + 1e-6 &&
    point.y >= Math.min(a.y, b.y) - 1e-6 && point.y <= Math.max(a.y, b.y) + 1e-6
  );
}

/** The horizontal extent of an outline at one height; null where the outline does not reach it. */
function spanAt(outline: readonly ShapePoint[], y: number): { from: number; to: number } | null {
  const xs: number[] = [];
  for (let i = 0, j = outline.length - 1; i < outline.length; j = i++) {
    const a = outline[i];
    const b = outline[j];
    if (Math.abs(a.y - y) < 1e-9) {
      xs.push(a.x);
    }
    const spans = (a.y < y && b.y > y) || (b.y < y && a.y > y);
    if (spans) {
      xs.push(a.x + (((y - a.y) / (b.y - a.y)) * (b.x - a.x)));
    }
  }
  return xs.length === 0 ? null : { from: Math.min(...xs), to: Math.max(...xs) };
}

/**
 * The widest rectangle of `bandHeight`, centred vertically in the shape, whose corners lie inside
 * the outline - less {@link TEXT_REGION_PADDING} on each side. This is where a label goes.
 *
 * For a shape with no outline it is the bounds less the padding, which is what the canvas already
 * did. For an outlined shape the band's own top and bottom decide the width, which is the whole
 * point: a trapezoid's text has to fit the narrow end of the band it sits in, not the wide one.
 */
export function textRegionOf(shape: BuiltInShape, bounds: ShapeBounds, bandHeight: number): ShapeBounds {
  const outline = outlineOf(shape, bounds);
  if (outline.length === 0) {
    return inset(bounds);
  }

  const centreY = bounds.y + (bounds.height / 2);
  const half = Math.min(bandHeight, bounds.height) / 2;
  const top = centreY - half;
  const bottom = centreY + half;

  const spans = [spanAt(outline, top), spanAt(outline, bottom)];
  const reached = spans.filter((span): span is { from: number; to: number } => span !== null);
  if (reached.length < spans.length) {
    // The band leaves the shape: nothing of that height fits, so the region collapses rather
    // than being invented. A caller asking for a band taller than the shape gets nothing.
    return { x: bounds.x + (bounds.width / 2), y: centreY, width: 0, height: 0 };
  }

  const from = Math.max(...reached.map((span) => span.from));
  const to = Math.min(...reached.map((span) => span.to));
  return inset({ x: from, y: top, width: Math.max(0, to - from), height: half * 2 });
}

/** The rectangle less the padding on each side, never inverted. */
function inset(bounds: ShapeBounds): ShapeBounds {
  const width = Math.max(0, bounds.width - (2 * TEXT_REGION_PADDING));
  const height = Math.max(0, bounds.height - (2 * TEXT_REGION_PADDING));
  return {
    x: bounds.x + ((bounds.width - width) / 2),
    y: bounds.y + ((bounds.height - height) / 2),
    width,
    height,
  };
}

/**
 * Where the ray from the shape's centre in direction `(dx, dy)` meets its OUTLINE - the third
 * reader of the one outline, after the drawing and the text region.
 *
 * `null` for a shape with no outline here, which is the caller's signal to keep its existing
 * rectangle geometry: `box` and `pill` must not move, and a null rather than a fallback keeps that
 * decision at the call site instead of hiding a second answer in this function.
 *
 * The nearest crossing is taken, so the answer is the first boundary the ray reaches. That is
 * correct for any outline star-shaped about its centre, which every shape here is; a concave shape
 * added later would need the same word said about it again rather than silently inheriting this one.
 */
export function outlineEdgePoint(
  shape: BuiltInShape,
  bounds: ShapeBounds,
  dx: number,
  dy: number,
): ShapePoint | null {
  const outline = outlineOf(shape, bounds);
  if (outline.length < 3) {
    return null;
  }

  const centre = { x: bounds.x + (bounds.width / 2), y: bounds.y + (bounds.height / 2) };
  if (dx === 0 && dy === 0) {
    // No direction has no edge to find, which is `edgePointOf`'s answer for the same case.
    return centre;
  }

  let nearest: number | null = null;
  for (let i = 0, j = outline.length - 1; i < outline.length; j = i++) {
    const a = outline[i];
    const b = outline[j];
    const ex = b.x - a.x;
    const ey = b.y - a.y;
    const denominator = (dx * ey) - (dy * ex);
    if (Math.abs(denominator) < 1e-12) {
      // Parallel to this segment: it is either missed entirely or grazed along its length, and
      // the crossing that matters is on one of the segments meeting it.
      continue;
    }

    const wx = a.x - centre.x;
    const wy = a.y - centre.y;
    const along = ((wx * ey) - (wy * ex)) / denominator;
    const across = ((wx * dy) - (wy * dx)) / denominator;
    if (along >= 0 && across >= -1e-9 && across <= 1 + 1e-9 && (nearest === null || along < nearest)) {
      nearest = along;
    }
  }

  return nearest === null ? null : { x: centre.x + (dx * nearest), y: centre.y + (dy * nearest) };
}

/** The four corners of a rectangle, for a caller checking it against an outline. */
export function cornersOf(bounds: ShapeBounds): readonly ShapePoint[] {
  return [
    { x: bounds.x, y: bounds.y },
    { x: bounds.x + bounds.width, y: bounds.y },
    { x: bounds.x + bounds.width, y: bounds.y + bounds.height },
    { x: bounds.x, y: bounds.y + bounds.height },
  ];
}
