/**
 * What a key does in a table, as a plain function of the key, the cell it was pressed in and the
 * table's size. Nothing here touches the DOM, so every keyboard path is a line in a test.
 */

/** A cell by where it is: its line in the view's order, and its place among the visible columns. */
export interface CellPosition {
  row: number;
  column: number;
}

export interface TableBounds {
  rows: number;
  columns: number;
}

/** The parts of a key event the table reads. */
export interface TableKey {
  key: string;
  shiftKey?: boolean;
  ctrlKey?: boolean;
  metaKey?: boolean;
  altKey?: boolean;
}

export type KeyOutcome =
  /** The focus moves to another cell. */
  | { kind: "move"; to: CellPosition }
  /** The cell opens for editing; `replace` holds the character typed, when typing is what opened it. */
  | { kind: "edit"; replace?: string }
  /** A new row is asked for, after the row the key was pressed in. */
  | { kind: "newRow" }
  /** The cell's value is cleared. */
  | { kind: "clear" }
  /** The key is not the table's. */
  | { kind: "none" };

const clamp = (value: number, low: number, high: number) => Math.min(Math.max(value, low), high);

/**
 * The cell a Tab leaves for: the next one in reading order, the first of the next line after a
 * line's last, and the cell itself at either end of the table.
 */
export function tabTarget(at: CellPosition, bounds: TableBounds, backwards: boolean): CellPosition {
  if (bounds.rows <= 0 || bounds.columns <= 0) {
    return at;
  }
  const flat = at.row * bounds.columns + at.column + (backwards ? -1 : 1);
  if (flat < 0 || flat >= bounds.rows * bounds.columns) {
    return at;
  }
  return { row: Math.floor(flat / bounds.columns), column: flat % bounds.columns };
}

/** What a key pressed in a cell that is not being edited does. */
export function keyOutcome(key: TableKey, at: CellPosition, bounds: TableBounds): KeyOutcome {
  if (bounds.rows <= 0 || bounds.columns <= 0 || key.altKey) {
    return { kind: "none" };
  }

  const lastRow = bounds.rows - 1;
  const lastColumn = bounds.columns - 1;
  const move = (row: number, column: number): KeyOutcome => ({ kind: "move", to: { row: clamp(row, 0, lastRow), column: clamp(column, 0, lastColumn) } });
  const command = key.ctrlKey === true || key.metaKey === true;

  switch (key.key) {
    case "ArrowUp":
      return move(command ? 0 : at.row - 1, at.column);
    case "ArrowDown":
      return move(command ? lastRow : at.row + 1, at.column);
    case "ArrowLeft":
      return move(at.row, command ? 0 : at.column - 1);
    case "ArrowRight":
      return move(at.row, command ? lastColumn : at.column + 1);
    case "Home":
      return move(command ? 0 : at.row, 0);
    case "End":
      return move(command ? lastRow : at.row, lastColumn);
    case "PageUp":
      return move(at.row - 10, at.column);
    case "PageDown":
      return move(at.row + 10, at.column);
    case "Tab":
      return { kind: "move", to: tabTarget(at, bounds, key.shiftKey === true) };
    case "Enter":
      return key.shiftKey === true ? { kind: "newRow" } : { kind: "edit" };
    case "F2":
      return { kind: "edit" };
    case "Delete":
    case "Backspace":
      return { kind: "clear" };
    default:
      // A character typed over a cell starts an edit that replaces what is there.
      return key.key.length === 1 && !command ? { kind: "edit", replace: key.key } : { kind: "none" };
  }
}
