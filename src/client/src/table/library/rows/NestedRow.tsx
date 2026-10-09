import type { TableGesture } from "../api/tableEvents";
import type { TableRow } from "../api/tableModel";

export interface NestedRowToggleProps {
  row: TableRow;
  /** The text that names the row, read out with the toggle. */
  title: string;
  editable: boolean;
  raise: (gesture: TableGesture) => void;
}

/**
 * The toggle of a row that has rows nested under it, shown in the cell that names the row. It
 * folds the rows under it away or brings them back, to any depth; a row with nothing under it
 * has none. As with a group, the fold is the view's and is raised, not kept here.
 */
export function NestedRowToggle({ row, title, editable, raise }: NestedRowToggleProps) {
  if (!row.hasChildren) {
    return null;
  }

  return (
    <button
      type="button"
      className="table-row-toggle"
      aria-expanded={!row.collapsed}
      aria-label={`${row.collapsed ? "Expand" : "Collapse"} ${title}`}
      disabled={!editable}
      // The toggle is not the cell: pressing it must neither start an edit nor move the focus.
      onMouseDown={(event) => event.preventDefault()}
      onDoubleClick={(event) => event.stopPropagation()}
      onClick={() => raise({ kind: "toggleRow", rowId: row.id, settings: { collapsed: row.collapsed ? "false" : "true" } })}
    >
      <span className={`mdi ${row.collapsed ? "mdi-chevron-right" : "mdi-chevron-down"}`} aria-hidden="true" />
    </button>
  );
}
