/**
 * What a designer module declares about its table. A module's client holds this and its
 * handlers, and renders nothing itself: the library draws the table from the model and the
 * declaration, as the canvas library draws a diagram from its definition.
 */

/**
 * How a kind of value is edited: in one of the browser's own inputs, by a tick, or from a
 * searchable list - one option, several options, or rows of another table.
 */
export type TableEditorKind = "text" | "number" | "checkbox" | "date" | "datetime" | "time" | "option" | "options" | "rows";

/** What a column's menu may offer. A module's definition lists the ones its table has. */
export type ColumnAction =
  | "rename"
  | "changeType"
  | "filter"
  | "sortAscending"
  | "sortDescending"
  | "group"
  | "hide"
  | "wrap"
  | "insertLeft"
  | "insertRight"
  | "duplicate"
  | "delete";

/** How one kind of value shows itself. */
export interface TableKindDefinition {
  /** The @mdi/font class of the kind's icon, shown in a column's header. */
  icon: string;
  /** The kind's name, read out where the icon is shown. */
  label: string;
  /** How a cell of this kind is edited. Left out, its cells are shown and never edited. */
  editor?: TableEditorKind;
  /** False for a kind a column cannot be given by hand: it is read, shown, and never offered. */
  addable?: boolean;
}

export interface TableDefinition {
  /** The kinds of value the table's columns may hold, by the name the model uses. */
  kinds: Readonly<Record<string, TableKindDefinition>>;
  /** The entries a column's menu offers. Left out, a header names its column and has no menu. */
  columnActions?: readonly ColumnAction[];
  /** The kind a column inserted beside another starts as; the first that can be added when left out. */
  defaultKind?: string;
  /** The height of every row in pixels; the library's default when left out. */
  rowHeight?: number;
  /** A column's width in pixels when the model gives none; the library's default when left out. */
  columnWidth?: number;
}

/** The icon and name of a kind the definition does not know: shown, never hidden. */
export const UNKNOWN_KIND: TableKindDefinition = { icon: "mdi-help-circle-outline", label: "Unknown type" };

/** A column's width in pixels unless the definition or the model says otherwise. */
export const DEFAULT_COLUMN_WIDTH = 180;

export function kindOf(definition: TableDefinition, kind: string): TableKindDefinition {
  return definition.kinds[kind] ?? UNKNOWN_KIND;
}
