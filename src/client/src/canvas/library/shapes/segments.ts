import type { EdgeAttachment, EdgeName, SegmentDeclaration, ShapeBounds, ShapePoint } from "../definition/diagramDefinition";
import { resolveNumber, valueAtPath, type BindingSource } from "../definition/binding";
import { arrowBannerPointDepth } from "./outline";

/**
 * An `arrow-banner` cut into segments: each segment's polygon, the dividers between them, and the
 * stretch of the top and bottom edges each one owns.
 *
 * <b>One geometry, read four times</b> - by the drawing, by the tooltips, by an attachment that
 * names a segment, and by the boundary handles - for the same reason `outline.ts` exists: four
 * readers with their own idea of where a segment is would disagree, and the disagreement would
 * show as an influence arriving beside the phase it names.
 *
 * Every point lies on or inside the banner's outline. A chevron's tails are pulled in where the
 * boundaries crowd each other or the point, rather than poking outside the shape.
 */

/** One drawn segment. */
export interface SegmentGeometry {
  index: number;
  /** The segment's closed polygon, in canvas units. */
  polygon: readonly ShapePoint[];
  /** The stretch of the top edge this segment owns, as x from..to. */
  top: { from: number; to: number };
  /** The stretch of the bottom edge this segment owns, as x from..to. */
  bottom: { from: number; to: number };
}

/** The line between two segments: a chevron's three points, or a straight line's two. */
export interface SegmentDivider {
  /** The inner boundary's index: 0 is the boundary between segments 0 and 1. */
  index: number;
  /** Where the boundary lies - the chevron's tip, or the line's x. */
  x: number;
  points: readonly ShapePoint[];
}

export interface SegmentLayout {
  /** How many segments are drawn. */
  count: number;
  segments: readonly SegmentGeometry[];
  dividers: readonly SegmentDivider[];
}

/** How many segments a declaration draws for this element: its bound count, kept within 1..max. */
export function segmentCountOf(declaration: SegmentDeclaration, source: BindingSource): number {
  const max = Math.max(1, Math.floor(declaration.max));
  const raw = typeof declaration.count === "number" ? declaration.count : resolveNumber(declaration.count, source);
  if (raw === null || !Number.isFinite(raw)) {
    return max;
  }

  return Math.min(max, Math.max(1, Math.round(raw)));
}

/**
 * The boundary fractions the model gives, when they are usable for `count` segments: exactly
 * `count - 1` numbers, strictly increasing, strictly inside 0..1. Anything else is an even spread,
 * so a model that has not caught up with a count change still draws a sensible banner.
 */
export function boundaryFractionsOf(declaration: SegmentDeclaration, source: BindingSource, count: number): readonly number[] {
  const even = Array.from({ length: count - 1 }, (_, index) => (index + 1) / count);
  if (declaration.boundaries === undefined) {
    return even;
  }

  const given = valueAtPath(declaration.boundaries, source);
  if (!Array.isArray(given) || given.length !== count - 1) {
    return even;
  }

  const fractions = given.map((value) => (typeof value === "number" ? value : Number(value)));
  const usable = fractions.every((value, index) =>
    Number.isFinite(value) && value > 0 && value < 1 && (index === 0 || value > fractions[index - 1]!));
  return usable ? fractions : even;
}

/** A boundary being dragged: which one, and where it is now, in canvas units. */
export interface MovedBoundary {
  index: number;
  x: number;
}

/**
 * The layout for a declaration against one element - with one boundary carried to a new place
 * while a drag is in flight, so the banner redraws under the pointer before anything is raised.
 */
export function resolveSegments(
  declaration: SegmentDeclaration,
  source: BindingSource,
  bounds: ShapeBounds,
  moved?: MovedBoundary,
): SegmentLayout {
  const count = segmentCountOf(declaration, source);
  const fractions = [...boundaryFractionsOf(declaration, source, count)];
  if (moved !== undefined && moved.index >= 0 && moved.index < fractions.length && bounds.width > 0) {
    fractions[moved.index] = (moved.x - bounds.x) / bounds.width;
  }

  return segmentLayoutOf(bounds, count, fractions, declaration.divider ?? "chevron");
}

/**
 * Where a dragged boundary may come to rest: snapped to the step, then kept at least one step
 * from its neighbours - the boundary either side of it, or the element's ends.
 *
 * Clamped AFTER snapping, because the clamp is the rule that must hold: a snap that pushed a
 * boundary onto its neighbour would make a segment vanish.
 */
export function boundaryLanding(layout: SegmentLayout, bounds: ShapeBounds, index: number, x: number, step: number, origin: number): number {
  const unit = step > 0 ? step : 1;
  const previous = index === 0 ? bounds.x : layout.dividers[index - 1]!.x;
  const next = index === layout.dividers.length - 1 ? bounds.x + bounds.width : layout.dividers[index + 1]!.x;
  const steps = (x - origin) / unit;
  // Halves away from zero, as every snap in the library rounds (see `snapToStep`).
  const snapped = origin + ((steps >= 0 ? Math.floor(steps + 0.5) : -Math.floor(-steps + 0.5)) * unit);
  const low = previous + unit;
  const high = next - unit;
  return low > high ? (previous + next) / 2 : Math.min(high, Math.max(low, snapped));
}

/**
 * The segments of a banner over `bounds`, split at `fractions` of its width.
 *
 * Pure arithmetic over the outline: `(0,0) (w - p, 0) (w, h/2) (w - p, h) (0, h)`. A chevron
 * divider is a right-pointing V whose tip lies on the boundary and whose tails reach back by the
 * point's depth `p`, or less where the previous boundary is nearer than that.
 */
export function segmentLayoutOf(
  bounds: ShapeBounds,
  count: number,
  fractions: readonly number[],
  divider: "chevron" | "line" = "chevron",
): SegmentLayout {
  const depth = arrowBannerPointDepth(bounds);
  const left = bounds.x;
  const top = bounds.y;
  const bottom = bounds.y + bounds.height;
  const middle = bounds.y + (bounds.height / 2);
  const shoulder = bounds.x + bounds.width - depth;
  const tip = bounds.x + bounds.width;

  // Each inner boundary as its tip and its tail: where it lies on the middle line, and where it
  // meets the top and bottom edges. A straight divider's tail IS its tip.
  const boundaries = fractions.map((fraction) => left + (fraction * bounds.width));
  const tails = boundaries.map((at, index) => {
    if (divider === "line") {
      return Math.min(at, shoulder);
    }

    const previous = index === 0 ? left : boundaries[index - 1]!;
    return Math.min(at - Math.min(depth, at - previous), shoulder);
  });

  const segments: SegmentGeometry[] = [];
  for (let index = 0; index < count; index++) {
    const first = index === 0;
    const last = index === count - 1;
    const leftTail = first ? left : tails[index - 1]!;
    const leftTip = first ? left : boundaries[index - 1]!;
    const rightTail = last ? shoulder : tails[index]!;
    const rightTip = last ? tip : boundaries[index]!;

    const polygon: ShapePoint[] = [
      { x: leftTail, y: top },
      { x: rightTail, y: top },
      { x: rightTip, y: middle },
      { x: rightTail, y: bottom },
      { x: leftTail, y: bottom },
    ];
    if (!first && leftTip !== leftTail) {
      // The notch the previous chevron's tip cuts into this segment's left side.
      polygon.push({ x: leftTip, y: middle });
    }

    segments.push({ index, polygon, top: { from: leftTail, to: rightTail }, bottom: { from: leftTail, to: rightTail } });
  }

  const dividers = boundaries.map((at, index): SegmentDivider => ({
    index,
    x: at,
    points: divider === "line" || tails[index] === at
      ? [{ x: at, y: top }, { x: at, y: bottom }]
      : [{ x: tails[index]!, y: top }, { x: at, y: middle }, { x: tails[index]!, y: bottom }],
  }));

  return { count, segments, dividers };
}

/**
 * Where an attachment lies on an element, from its CURRENT bounds and segments.
 *
 * `at` is a fraction of the region, so a resize moves the end proportionally. A region the element
 * is not drawing, or no layout at all, falls back to the whole edge.
 */
export function attachmentPointOf(attachment: EdgeAttachment, bounds: ShapeBounds, layout?: SegmentLayout): ShapePoint {
  const at = Math.min(1, Math.max(0, attachment.at));
  const segment = attachment.region !== undefined ? layout?.segments[attachment.region] : undefined;
  switch (attachment.edge) {
    case "top":
    case "bottom": {
      const span = segment !== undefined ? segment[attachment.edge] : { from: bounds.x, to: bounds.x + bounds.width };
      return { x: span.from + (at * (span.to - span.from)), y: attachment.edge === "top" ? bounds.y : bounds.y + bounds.height };
    }
    case "left":
      return { x: bounds.x, y: bounds.y + (at * bounds.height) };
    case "right":
      return { x: bounds.x + bounds.width, y: bounds.y + (at * bounds.height) };
  }
}

/**
 * The attachment nearest a point, on the edges an element declares - what a connect gesture
 * records where it starts and where it ends.
 */
export function nearestAttachment(
  point: ShapePoint,
  bounds: ShapeBounds,
  edges: readonly EdgeName[],
  layout?: SegmentLayout,
): EdgeAttachment | undefined {
  let best: { attachment: EdgeAttachment; distance: number } | undefined;
  const consider = (attachment: EdgeAttachment) => {
    const on = attachmentPointOf(attachment, bounds, layout);
    const distance = Math.hypot(on.x - point.x, on.y - point.y);
    if (best === undefined || distance < best.distance) {
      best = { attachment, distance };
    }
  };

  for (const edge of edges) {
    if ((edge === "top" || edge === "bottom") && layout !== undefined) {
      for (const segment of layout.segments) {
        const span = segment[edge];
        const length = span.to - span.from;
        consider({ edge, region: segment.index, at: roundedFraction(length > 0 ? (point.x - span.from) / length : 0) });
      }
      continue;
    }

    const horizontal = edge === "top" || edge === "bottom";
    const fraction = horizontal
      ? (bounds.width > 0 ? (point.x - bounds.x) / bounds.width : 0)
      : (bounds.height > 0 ? (point.y - bounds.y) / bounds.height : 0);
    consider({ edge, at: roundedFraction(fraction) });
  }

  return best?.attachment;
}

/**
 * The attachment a point makes on ONE stretch of one edge - what a press on an edge strip records.
 * The region is the strip's own, so a press near a boundary belongs to the segment it was drawn for.
 */
export function attachmentAlong(point: ShapePoint, bounds: ShapeBounds, edge: EdgeName, region: number | undefined, layout?: SegmentLayout): EdgeAttachment {
  const segment = region !== undefined ? layout?.segments[region] : undefined;
  if (edge === "top" || edge === "bottom") {
    const span = segment !== undefined ? segment[edge] : { from: bounds.x, to: bounds.x + bounds.width };
    const length = span.to - span.from;
    const at = roundedFraction(length > 0 ? (point.x - span.from) / length : 0);
    return segment !== undefined ? { edge, region, at } : { edge, at };
  }

  return { edge, at: roundedFraction(bounds.height > 0 ? (point.y - bounds.y) / bounds.height : 0) };
}

/** A fraction kept within 0..1 and to two decimals, which is what a document stores. */
function roundedFraction(value: number): number {
  const clamped = Math.min(1, Math.max(0, value));
  return Math.round(clamped * 100) / 100;
}

export type { EdgeAttachment, EdgeName };
