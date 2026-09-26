import type { LabelPlacement } from "../../shell/panels/InlineLabelPlacementContext";

/**
 * Where an inline editor goes, for the three shapes a canvas actually draws. Pure arithmetic:
 * no React, no DOM, and no vocabulary from any diagram type - a box is a box whether it holds a
 * C4 container, a timeline span or a Wardley component.
 *
 * These were C4's private helpers first. They are here because the fourth canvas to adopt
 * inline renaming would otherwise have copied them, and because placement is the half of
 * adoption that is genuinely the same everywhere - what differs between modules is which
 * element a prompt refers to, not where a label sits inside a box.
 */

/** A box the canvas draws, given by its CENTRE and its size - the form these canvases store. */
export interface LabelBox {
  /** Centre, not corner. */
  x: number;
  /** Centre, not corner. */
  y: number;
  width: number;
  height: number;
}

/** A point in canvas units. */
export interface LabelPoint {
  x: number;
  y: number;
}

/**
 * Per-character width estimate and floor for a label with no box of its own. Generic text
 * metrics rather than any module's vocabulary, and deliberately the same numbers the shared
 * element components already use for the same purpose.
 */
const ESTIMATED_CHAR_WIDTH = 7;
const MINIMUM_ESTIMATED_WIDTH = 80;
const TEXT_LABEL_HEIGHT = 16;

/**
 * A label filling its element's box: the editor covers exactly what the element draws. For a
 * canvas whose element is one line of text in a rectangle, this is the whole answer.
 */
export function centredLabelPlacement(box: LabelBox, text: string): LabelPlacement {
  return {
    x: box.x - box.width / 2,
    y: box.y - box.height / 2,
    width: box.width,
    height: box.height,
    text,
  };
}

/**
 * One named line inside a composite box - a card whose first line is the name, with a type line
 * and a description under it. An editor covering the whole box would sit over three lines of
 * text to edit one of them.
 *
 * <paramref name="top"/> is the offset from the box's top to the line, and
 * <paramref name="height"/> the line's own height. A caller wanting horizontal padding passes a
 * box already narrowed by it: the box is centred, so narrowing the width insets both sides
 * evenly and keeps the arithmetic here free of anyone's padding constant.
 */
export function insetLabelPlacement(box: LabelBox, top: number, height: number, text: string): LabelPlacement {
  return {
    x: box.x - box.width / 2,
    y: box.y - box.height / 2 + top,
    width: box.width,
    height,
    text,
  };
}

/**
 * A bare text on a connection: centred on the midpoint of the line, offset by
 * <paramref name="dy"/>, and sitting above that offset rather than below it.
 *
 * The width is the caller's MEASURED width where it has one, because a connection label has no
 * box - it is a text node, and its width is whatever the font made it. Measuring needs the DOM,
 * which does not belong in a pure function, so the caller measures and passes the result;
 * passing nothing falls back to a per-character estimate.
 *
 * **jsdom implements no `getBBox`, so every unit test takes the estimate branch by
 * construction.** The measured branch is not covered by any unit test and cannot be - it is
 * verified by the manual check in `tests.md`. Do not read a green suite as evidence that
 * measuring works.
 */
export function midpointLabelPlacement(
  from: LabelPoint,
  to: LabelPoint,
  dy: number,
  text: string,
  measuredWidth: number | null = null,
): LabelPlacement {
  const middle = { x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 };
  const width = measuredWidth ?? Math.max(text.length * ESTIMATED_CHAR_WIDTH, MINIMUM_ESTIMATED_WIDTH);

  return {
    x: middle.x - width / 2,
    y: middle.y + dy - TEXT_LABEL_HEIGHT,
    width,
    height: TEXT_LABEL_HEIGHT,
    text,
  };
}

/**
 * A label drawn BESIDE a point marker rather than inside a box: start-anchored, a fixed gap to
 * the right of the marker's centre, and vertically centred on it.
 *
 * This is the fourth shape, and it exists because a canvas drawing both spans and instants
 * draws their labels differently: a span's label is centred in its box, an instant's sits
 * outside the marker because there is no box to put it in. Centring an editor on the marker
 * would open it over the marker rather than over the text it replaces.
 *
 * <paramref name="gap"/> is measured from the marker's CENTRE, so a caller passes its radius
 * plus whatever spacing it draws - the same sum it already gives its text element's x.
 *
 * Width follows the same rule as a connection label: the caller's measured width where it has
 * one, the per-character estimate otherwise, and jsdom takes the estimate branch always.
 */
export function asideLabelPlacement(
  at: LabelPoint,
  gap: number,
  text: string,
  measuredWidth: number | null = null,
): LabelPlacement {
  const width = measuredWidth ?? Math.max(text.length * ESTIMATED_CHAR_WIDTH, MINIMUM_ESTIMATED_WIDTH);

  return {
    x: at.x + gap,
    y: at.y - TEXT_LABEL_HEIGHT / 2,
    width,
    height: TEXT_LABEL_HEIGHT,
    text,
  };
}

/**
 * The mirror of {@link asideLabelPlacement}: a label drawn to the LEFT of its element, end-anchored,
 * so its right edge sits `gap` before `at` - the left edge's midpoint. Same width rule.
 */
export function beforeLabelPlacement(
  at: LabelPoint,
  gap: number,
  text: string,
  measuredWidth: number | null = null,
): LabelPlacement {
  const width = measuredWidth ?? Math.max(text.length * ESTIMATED_CHAR_WIDTH, MINIMUM_ESTIMATED_WIDTH);

  return {
    x: at.x - gap - width,
    y: at.y - TEXT_LABEL_HEIGHT / 2,
    width,
    height: TEXT_LABEL_HEIGHT,
    text,
  };
}
