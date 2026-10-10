import type { TableGesture } from "../api/tableEvents";
import type { TableRow } from "../api/tableModel";

export interface NewRowProps {
  /** The line a new row is added at: its id is the key of the group it ends, or empty at the bottom of an ungrouped table. */
  row: TableRow;
  columnCount: number;
  raise: (gesture: TableGesture) => void;
}

/**
 * The place a new row is added: at the bottom of the table, and of every group.
 *
 * <b>It always says which group.</b> A row added at the bottom of a group belongs to that group -
 * it gets the group's value - so the gesture carries the group's key, and carries an empty key
 * at the bottom of a table that is not grouped. A gesture without it would add every new row to
 * no group at all, wherever it was asked for.
 */
export function NewRow({ row, columnCount, raise }: NewRowProps) {
  return (
    <div className="table-new-row-cell" role="gridcell" aria-colspan={Math.max(1, columnCount)}>
      <button type="button" className="table-new-row-button" onClick={() => raise({ kind: "addRow", settings: { group: row.id } })}>
        <span className="mdi mdi-plus" aria-hidden="true" />
        <span>New</span>
      </button>
    </div>
  );
}
