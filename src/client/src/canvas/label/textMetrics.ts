/**
 * The one character metric and the one fit for text on a canvas.
 *
 * <b>One metric, because a label sized by one metric and trimmed by another is the drift behind
 * the overflow reports</b> on databricks, c4 and shacl: the library sized content boxes at 8 per
 * character and trimmed at 7, c4 wrapped at 5.5 and the OWL canvas trimmed at 6.2. Every width
 * estimate and every ellipsis in the client comes from here, and a guard fails on a copy
 * anywhere else.
 *
 * <b>It is the backend's metric</b> - characters × font size × average advance, the values the
 * backend's modules size boxes with (backend-centralization Requirement 10) - so a box the
 * backend sized for a text is fitted by the same estimate on this side of the wire (the user's
 * chat ruling, 2026-09-27). The backend's golden fixture pins the two together.
 */

/** The average advance of one character, as a fraction of the font size. */
export const AVERAGE_ADVANCE = 0.55;

/** The size the library draws an element's label at when none is declared - `.canvas-node-label` in canvas.css. */
export const LABEL_FONT_SIZE = 12;

/** One character's estimated width at a font size. */
function advanceOf(fontSize: number): number {
  return fontSize * AVERAGE_ADVANCE;
}

/** The width a string is estimated to take at a font size - where no measured width exists. */
export function widthOf(text: string, fontSize: number): number {
  return text.length * advanceOf(fontSize);
}

/**
 * Whole characters of one advance in a width. The tolerance is for floating point alone: a width
 * made by {@link widthOf} for n characters must hold n, and `n * a / a` can land a hair under n.
 */
function charactersIn(width: number, fontSize: number): number {
  return Math.floor(width / advanceOf(fontSize) + 1e-9);
}

/** How many characters fit a width at a font size. At least one, so a line wrapped at it always advances. */
export function capacityOf(width: number, fontSize: number): number {
  return Math.max(1, charactersIn(width, fontSize));
}

/** What a box keeps free inside its outline on one line of fitted text. */
const FIT_PADDING = 8;

/**
 * The text as drawn in a box `width` wide: whole when it fits, cut with an ellipsis when it does
 * not. The padding comes off first, so text sized by {@link widthOf} plus that padding is never
 * trimmed - the sizing and the fit agree by construction.
 */
export function fitToWidth(text: string, width: number, fontSize: number): string {
  return fitToCapacity(text, charactersIn(Math.max(width - FIT_PADDING, 0), fontSize));
}

/** The text cut to so many characters, the last of them an ellipsis when anything was cut. */
export function fitToCapacity(text: string, capacity: number): string {
  if (text.length <= capacity) {
    return text;
  }

  return capacity <= 1 ? "…" : `${text.slice(0, capacity - 1)}…`;
}

/**
 * A line that has more text after it which did not fit, marked so with an ellipsis inside the
 * same capacity - the last visible line of a wrapped label that ran out of room.
 */
export function continuedWithin(line: string, capacity: number): string {
  return line.length >= capacity ? `${line.slice(0, Math.max(0, capacity - 1))}…` : `${line}…`;
}
