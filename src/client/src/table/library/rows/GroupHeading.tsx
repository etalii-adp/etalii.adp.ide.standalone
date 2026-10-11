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
 * The heading of a group of rows: the value the rows share, how many they are, a toggle that
 * folds them away, and the controls that move the group among the others or hide it. Folding is the view's - it is stored with the view - so the toggle raises a
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
      {editable && (
        // What is done to this one group: its place among the groups, and whether it is shown. The view's, like the fold.
        <span className="table-group-actions">
          <button type="button" className="table-group-action" aria-label={`Move ${row.label} up`} onClick={() => raise({ kind: "moveGroup", targetId: row.id, settings: { direction: "up" } })}>
            <span className="mdi mdi-arrow-up" aria-hidden="true" />
          </button>
          <button type="button" className="table-group-action" aria-label={`Move ${row.label} down`} onClick={() => raise({ kind: "moveGroup", targetId: row.id, settings: { direction: "down" } })}>
            <span className="mdi mdi-arrow-down" aria-hidden="true" />
          </button>
          <button type="button" className="table-group-action" aria-label={`Hide ${row.label}`} onClick={() => raise({ kind: "hideGroup", targetId: row.id })}>
            <span className="mdi mdi-eye-off-outline" aria-hidden="true" />
          </button>
        </span>
      )}
    </div>
  );
}
