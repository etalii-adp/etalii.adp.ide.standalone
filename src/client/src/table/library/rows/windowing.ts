/**
 * Which rows of a table are worth drawing: the ones in sight and a margin either side.
 *
 * A table may hold ten thousand rows and is never drawn whole. Every row has the same height, so
 * what is in sight follows from the scroll offset by arithmetic alone - no row is measured, and
 * nothing here touches the DOM, which is what lets it be tested as plain functions.
 */

/** A run of rows by index in the view's order: the first, and how many from there. */
export interface RowWindow {
  first: number;
  count: number;
}

export interface WindowInput {
  /** How far the rows are scrolled, in pixels. */
  scrollTop: number;
  /** The height the rows are seen through, in pixels. */
  viewportHeight: number;
  /** The height of every row, in pixels. */
  rowHeight: number;
  /** How many rows the view has. */
  rowCount: number;
  /** Rows kept ready above and below what is in sight, so a scroll shows rows and not blanks. */
  margin: number;
}

/** The height of a row unless a table's definition says otherwise, in pixels. */
export const DEFAULT_ROW_HEIGHT = 33;

/** The rows kept ready either side of what is in sight unless a table's definition says otherwise. */
export const DEFAULT_MARGIN = 10;

/**
 * The window for a scroll position: the rows in sight, widened by the margin and kept inside the
 * view. A view with no rows has the empty window at zero.
 */
export function windowOf({ scrollTop, viewportHeight, rowHeight, rowCount, margin }: WindowInput): RowWindow {
  if (rowCount <= 0 || rowHeight <= 0) {
    return { first: 0, count: 0 };
  }

  const firstInSight = Math.floor(Math.max(0, scrollTop) / rowHeight);
  // A row that is partly in sight is in sight: the last one is found from the last pixel shown.
  const lastInSight = Math.floor((Math.max(0, scrollTop) + Math.max(0, viewportHeight) - 1) / rowHeight);
  const first = Math.min(Math.max(0, firstInSight - margin), rowCount - 1);
  const last = Math.min(Math.max(first, lastInSight + margin), rowCount - 1);
  return { first, count: last - first + 1 };
}

/**
 * The space to leave above and below the drawn rows, in pixels, so the scroll bar is as long as
 * the whole view although only a window of it is in the document.
 */
export function spacersOf(window: RowWindow, rowCount: number, rowHeight: number): { before: number; after: number } {
  const drawnTo = window.first + window.count;
  return { before: window.first * rowHeight, after: Math.max(0, rowCount - drawnTo) * rowHeight };
}

/** Whether two windows are the same rows: a scroll within a row changes nothing worth reporting. */
export function sameWindow(a: RowWindow, b: RowWindow): boolean {
  return a.first === b.first && a.count === b.count;
}

/** The indexes of a window's rows, in order. */
export function indexesOf(window: RowWindow): number[] {
  return Array.from({ length: window.count }, (_unused, offset) => window.first + offset);
}
