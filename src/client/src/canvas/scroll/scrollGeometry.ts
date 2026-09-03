/**
 * The two pure calculations a scroll view over an unbounded canvas needs, kept free of React
 * so they can be asserted without rendering anything.
 *
 * A canvas whose size is a transform cannot be described by a native scrollbar, so these bars
 * are windows onto an *extent* rather than bars onto a sized element. The extent is the
 * content's bounds plus a margin, and the margin is not decoration: a bar over an unbounded
 * plane must let the user drag a little past the content, or the plane stops feeling unbounded.
 * Its shape differs per canvas, which is why `scrollExtentOf` is parameterized rather than fixed
 * and why the units are always the caller's.
 */

/** One axis of a scroll view, in whatever units the canvas measures that axis in. */
export interface ScrollAxis {
  /** Where the view's window begins. */
  viewStart: number;
  /** How much content the view spans, in the same units. */
  viewSpan: number;
  /** The scrollable extent, margin already included. */
  extentStart: number;
  extentEnd: number;
}

/** A thumb's geometry, both values as fractions of the track. */
export interface ThumbGeometry {
  /** Fraction of the track the thumb covers, clamped to `[0.05, 1]`. */
  size: number;
  /** Fraction of the track before the thumb, clamped to `[0, 1 - size]`. */
  offset: number;
}

/**
 * Where the view's window sits inside its extent.
 *
 * The extent is held to at least one unit so a degenerate content box (no elements, or every
 * element at one point) cannot divide by zero; the size floor keeps a thumb draggable however
 * large the extent grows; and a view wider than its extent claims the whole track rather than
 * inviting a pan that cannot happen. An offset past the end - the view dragged beyond the
 * margin - sits the thumb at the end of its track and stops, without forcing the view back.
 */
export function thumbOf(axis: ScrollAxis): ThumbGeometry {
  const extent = Math.max(axis.extentEnd - axis.extentStart, 1);
  const size = Math.min(Math.max(axis.viewSpan / extent, 0.05), 1);
  const offset = Math.min(Math.max((axis.viewStart - axis.extentStart) / extent, 0), 1 - size);
  return { size, offset };
}

/** How much room `scrollExtentOf` keeps around the content, in the content's own units. */
export interface ScrollExtentOptions {
  /** Margin as a fraction of the content span. Default `0`. */
  factor?: number;
  /** A fixed margin the proportional one never drops below. Default `0`. */
  minimum?: number;
  /** The span the content is treated as at least, before the factor applies. Default `0`. */
  minimumSpan?: number;
}

/** A scrollable extent: the content's bounds padded on both sides. */
export interface ScrollExtent {
  extentStart: number;
  extentEnd: number;
}

/**
 * The content's bounds plus a margin on each side, where the margin is
 * `max((max - min, clamped up to minimumSpan) * factor, minimum)`.
 *
 * Three shapes fall out of the parameters, and the timeline uses two of them: a proportional
 * margin with a floor on the span it is proportional to (`{ factor: 0.5, minimumSpan: DAY }`,
 * so a one-element timeline still has room either side), and a fixed margin regardless of
 * content (`{ factor: 0, minimum: 2 * ROW_HEIGHT }`, two empty rows above and below). The third
 * - proportional with a fixed floor - is the general case a later canvas may want.
 */
export function scrollExtentOf(min: number, max: number, options: ScrollExtentOptions = {}): ScrollExtent {
  const factor = options.factor ?? 0;
  const minimum = options.minimum ?? 0;
  const minimumSpan = options.minimumSpan ?? 0;

  const span = Math.max(max - min, minimumSpan);
  const margin = Math.max(span * factor, minimum);
  return { extentStart: min - margin, extentEnd: max + margin };
}
