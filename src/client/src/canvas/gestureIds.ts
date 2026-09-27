/**
 * The ids a canvas sends the backend for a gesture that names no existing element: a placement
 * on empty canvas, `new:x,y`, and a relation between two ends, `rel:from->to`.
 *
 * <b>Built here and nowhere else</b>, because the grammar is the backend's - it parses these -
 * and ten canvases writing the same template by hand is ten places for it to drift. The
 * backend's golden fixture (backend-centralization task 21) pins the two together.
 *
 * <b>A prefix is checked before it is removed.</b> causal-loop stripped `variable:` by length
 * with no check, so an id of exactly `variable:` became an empty end and an id without the
 * prefix lost its first nine characters, silently. {@link withoutPrefix} answers null instead.
 */

/** A placement on empty canvas. `y` is a coordinate or a row index, as the module's backend reads it. */
export function placementId(x: number, y: number): string {
  return `new:${x},${y}`;
}

/** A relation drawn from one end to another. */
export function relationId(from: string, to: string): string {
  return `rel:${from}->${to}`;
}

/** The id with its prefix removed, or null when it does not carry the prefix or nothing follows it. */
export function withoutPrefix(id: string, prefix: string): string | null {
  if (!id.startsWith(prefix) || id.length === prefix.length) {
    return null;
  }

  return id.slice(prefix.length);
}
