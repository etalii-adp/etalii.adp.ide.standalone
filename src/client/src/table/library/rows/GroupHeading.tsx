import type { TableGesture } from "../api/tableEvents";
import type { TableRow } from "../api/tableModel";

export interface GroupHeadingProps {
  /** The heading's line: its id is the group's key. */
  row: TableRow;
  /** How many columns the heading spans. */
  columnCount: number;
  editable: boolean;
  raise: (gesture: TableGesture) => void;
}

/**
 * The heading of a group of rows: the value the rows share, how many they are, and a toggle that
 * folds them away. Folding is the view's - it is stored with the view - so the toggle raises a
 * gesture and the heading shows what the model says, never a state of its own.
 */
export function GroupHeading({ row, columnCount, editable, raise }: GroupHeadingProps) {
  return (
    <div className="table-group-cell" role="gridcell" aria-colspan={Math.max(1, columnCount)}>
      <button
        type="button"
        className="table-row-toggle"
        aria-expanded={!row.collapsed}
        aria-label={`${row.collapsed ? "Expand" : "Collapse"} ${row.label}`}
        disabled={!editable}
        onClick={() => raise({ kind: "toggleGroup", targetId: row.id, settings: { collapsed: row.collapsed ? "false" : "true" } })}
      >
        <span className={`mdi ${row.collapsed ? "mdi-chevron-right" : "mdi-chevron-down"}`} aria-hidden="true" />
      </button>
      <span className="table-group-label">{row.label}</span>
      <span className="table-group-count">{row.count}</span>
    </div>
  );
}
