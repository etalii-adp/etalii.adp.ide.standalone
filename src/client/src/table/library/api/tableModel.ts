/**
 * The table model: what the table library draws, and what a designer's backend pushes.
 *
 * It names no designer type. A designer whose document is a table - rows, columns and views of
 * them - maps its document onto this on the backend, as a diagram module maps onto the canvas
 * library's diagram model, and the library draws whatever arrives.
 *
 * These are the library's own shapes, not the wire's: the stream hook converts, so nothing in
 * the library imports a generated message and a wire change is one file's problem.
 */

export interface TableOption {
  id: string;
  name: string;
  /** A colour name of the theme, never a colour value. */
  color: string;
}

export interface TableColumn {
  id: string;
  name: string;
  /** The kind of value its cells hold, in the designer's own vocabulary. */
  kind: string;
  options: readonly TableOption[];
  /** In pixels; 0 for the table's default. */
  width: number;
  visible: boolean;
  /** The column that names a row. */
  isTitle: boolean;
  wraps: boolean;
  settings: Readonly<Record<string, string>>;
}

export interface TableView {
  id: string;
  name: string;
}

export interface TableSort {
  columnId: string;
  descending: boolean;
}

export interface TableCondition {
  kind: "condition";
  columnId: string;
  comparison: string;
  values: readonly string[];
}

export interface TableFilterGroup {
  kind: "group";
  /** True when one holding item is enough; false when all must hold. */
  any: boolean;
  items: readonly (TableCondition | TableFilterGroup)[];
}

export interface TableViewSettings {
  viewId: string;
  sorts: readonly TableSort[];
  /** Absent when the view filters nothing. */
  filter?: TableFilterGroup;
  /** A column id, or empty. */
  groupBy: string;
  hidesEmptyGroups: boolean;
  settings: Readonly<Record<string, string>>;
}

export interface TableCell {
  columnId: string;
  /** The value in its written form; several for a kind that holds several. */
  values: readonly string[];
  /** What to show per value where that is not the value itself. */
  labels: readonly string[];
  /** True while the value shown is an edit that has not been written yet. */
  pending: boolean;
}

/** One line of a view: a row of the table, or the heading of a group of rows. */
export interface TableRow {
  id: string;
  depth: number;
  cells: readonly TableCell[];
  isGroup: boolean;
  label: string;
  count: number;
  collapsed: boolean;
  hasChildren: boolean;
}

export interface TableFinding {
  code: string;
  severity: "error" | "warning" | "info";
  message: string;
  rowId: string;
  columnId: string;
}

export interface TableModel {
  title: string;
  /** Every column, in the active view's order. */
  columns: readonly TableColumn[];
  views: readonly TableView[];
  settings: TableViewSettings;
  /** The lines of the active view: rows and group headings. */
  rowCount: number;
  /** The lines the client holds, by index in the view's order: its window, and no more. */
  rows: ReadonlyMap<number, TableRow>;
  findings: readonly TableFinding[];
  /** Why the table cannot be edited, or empty when it can. */
  readOnlyReason: string;
}

/** Everything about a table but its rows, as a baseline or a structure change carries it. */
export type TableStructure = Pick<TableModel, "title" | "columns" | "views" | "settings" | "rowCount" | "readOnlyReason">;

/** Something a table's stream delivers, in the library's shapes. */
export type TableEvent =
  | { kind: "baseline"; structure: TableStructure; findings: readonly TableFinding[] }
  | { kind: "rows"; first: number; rows: readonly TableRow[]; rowCount: number }
  | { kind: "structure"; structure: TableStructure }
  | { kind: "findings"; findings: readonly TableFinding[] };

export const EMPTY_TABLE: TableModel = {
  title: "",
  columns: [],
  views: [],
  settings: { viewId: "", sorts: [], groupBy: "", hidesEmptyGroups: false, settings: {} },
  rowCount: 0,
  rows: new Map(),
  findings: [],
  readOnlyReason: "",
};

/**
 * The model after an event. A baseline is the whole truth and starts over. Rows replace what the
 * client holds: the lines given are the window, and a line outside it is not kept - it would be
 * a copy nothing keeps current.
 */
export function applyTableEvent(model: TableModel, event: TableEvent): TableModel {
  switch (event.kind) {
    case "baseline":
      return { ...event.structure, rows: new Map(), findings: event.findings };
    case "rows":
      return { ...model, rowCount: event.rowCount, rows: new Map(event.rows.map((row, offset) => [event.first + offset, row])) };
    case "structure":
      return { ...model, ...event.structure };
    case "findings":
      return { ...model, findings: event.findings };
  }
}

/** The text a cell shows: each value's label where it has one, else the value, with commas between. */
export function cellText(cell: TableCell | undefined): string {
  if (cell === undefined) {
    return "";
  }
  return cell.values.map((value, index) => cell.labels[index] || value).join(", ");
}
