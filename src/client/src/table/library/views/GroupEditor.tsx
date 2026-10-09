import type { TableGesture } from "../api/tableEvents";
import type { TableColumn, TableViewSettings } from "../api/tableModel";
import { kindOf, type TableDefinition } from "../definition/tableDefinition";

/** The columns a view can be grouped by: those whose kind says it can be. */
export function groupableColumns(columns: readonly TableColumn[], definition: TableDefinition): TableColumn[] {
  return columns.filter((column) => kindOf(definition, column.kind).groupable === true);
}

export interface GroupEditorProps {
  settings: TableViewSettings;
  columns: readonly TableColumn[];
  definition: TableDefinition;
  raise: (gesture: TableGesture) => void;
}

/**
 * The view's grouping, open for editing: which property its rows are grouped by, if any, and
 * whether a group without rows is left out.
 */
export function GroupEditor({ settings, columns, definition, raise }: GroupEditorProps) {
  const offered = groupableColumns(columns, definition);
  return (
    <div className="table-group-editor" role="group" aria-label="Group">
      <label>
        <span>Group by</span>
        <select aria-label="Group by" value={settings.groupBy} onChange={(event) => raise({ kind: "groupBy", columnId: event.target.value })}>
          <option value="">Nothing</option>
          {
            // A grouping by a property that can no longer be grouped by is still shown as what it is.
            settings.groupBy !== "" && !offered.some((column) => column.id === settings.groupBy) && (
              <option value={settings.groupBy}>{columns.find((column) => column.id === settings.groupBy)?.name ?? settings.groupBy}</option>
            )
          }
          {offered.map((column) => (
            <option key={column.id} value={column.id}>
              {column.name}
            </option>
          ))}
        </select>
      </label>
      <label className="table-group-hide-empty">
        <input
          type="checkbox"
          checked={settings.hidesEmptyGroups}
          disabled={settings.groupBy === ""}
          onChange={(event) => raise({ kind: "setHideEmptyGroups", settings: { hide: event.target.checked ? "true" : "false" } })}
        />
        <span>Hide empty groups</span>
      </label>
    </div>
  );
}

export interface PropertyVisibilityProps {
  /** Every column of the table, shown or not. */
  columns: readonly TableColumn[];
  raise: (gesture: TableGesture) => void;
}

/** Which properties the view shows: a tick per property, with the one that names a row always ticked. */
export function PropertyVisibility({ columns, raise }: PropertyVisibilityProps) {
  return (
    <div className="table-property-visibility" role="group" aria-label="Properties">
      {columns.map((column) => (
        <label key={column.id} className="table-property-row">
          <input
            type="checkbox"
            checked={column.visible}
            disabled={column.isTitle}
            onChange={(event) => raise({ kind: event.target.checked ? "showColumn" : "hideColumn", columnId: column.id })}
          />
          <span>{column.name}</span>
        </label>
      ))}
    </div>
  );
}
