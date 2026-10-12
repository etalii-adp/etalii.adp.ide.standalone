import { useRef } from "react";
import { usePointerGesture } from "../../../canvas/gesture/usePointerGesture";
import type { TableGesture } from "../api/tableEvents";
import type { TableColumn, TableViewSettings } from "../api/tableModel";
import { kindOf, type TableDefinition } from "../definition/tableDefinition";
import { dropIndex } from "../header/columnActions";

/** The columns a view can be grouped by: those whose kind says it can be. */
export function groupableColumns(columns: readonly TableColumn[], definition: TableDefinition): TableColumn[] {
  return columns.filter((column) => kindOf(definition, column.kind).groupable === true);
}

/** The setting a view says under how many of its groups are hidden. */
const HIDDEN_GROUPS = "hiddenGroups";

/** The setting a view lists its groups under: each with its key, what it is called and whether it is hidden, in the view's order. */
const GROUPS = "groups";

/** One group of a grouped view, as the view lists it. */
export interface ListedGroup {
  key: string;
  label: string;
  hidden: boolean;
}

/** The groups a view says it has; none when it says nothing, or nothing that reads as a list of groups. */
export function listedGroups(settings: TableViewSettings): ListedGroup[] {
  try {
    const parsed: unknown = JSON.parse(settings.settings[GROUPS] ?? "[]");
    return Array.isArray(parsed)
      ? parsed.filter((group): group is ListedGroup => typeof group === "object" && group !== null && typeof (group as ListedGroup).key === "string" && typeof (group as ListedGroup).label === "string")
      : [];
  } catch {
    return [];
  }
}

export interface GroupEditorProps {
  settings: TableViewSettings;
  columns: readonly TableColumn[];
  definition: TableDefinition;
  raise: (gesture: TableGesture) => void;
}

/**
 * The view's grouping, open for editing: which property its rows are grouped by, if any,
 * whether a group without rows is left out, and its groups one by one: each shown or hidden by
 * its eye, put in its place by dragging, or all of them put in the order of their names.
 */
export function GroupEditor({ settings, columns, definition, raise }: GroupEditorProps) {
  const offered = groupableColumns(columns, definition);
  // How many groups the view hides, as its settings say it; nothing said is none.
  const hidden = Number(settings.settings[HIDDEN_GROUPS] ?? "0") || 0;
  const groups = listedGroups(settings);
  const listRef = useRef<HTMLUListElement>(null);

  // A group is put in its place by dragging its handle: one gesture, when the drag ends, as a sort's is.
  const drag = usePointerGesture<ListedGroup>({
    onPress: () => {},
    onDragEnd: (group, _dx, dy) => {
      const lines = Array.from(listRef.current?.querySelectorAll<HTMLElement>("li") ?? [], (line) => {
        const rect = line.getBoundingClientRect();
        return { left: rect.top, right: rect.bottom };
      });
      const from = groups.findIndex((candidate) => candidate.key === group.key);
      const line = lines[from];
      if (line === undefined) {
        return;
      }
      const to = dropIndex(from, (line.left + line.right) / 2 + dy, lines);
      if (to !== from) {
        raise({ kind: "moveGroup", targetId: group.key, index: to });
      }
    },
  });
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
      {groups.length > 0 && (
        <>
          <label>
            <span>Order of groups</span>
            {/* Putting the groups in the order of their names is something done, not a state kept: the order is the view's own from then on. */}
            <select aria-label="Order of groups" value="" onChange={(event) => event.target.value !== "" && raise({ kind: "sortGroups", settings: { order: event.target.value } })}>
              <option value="">As arranged</option>
              <option value="ascending">A to Z</option>
              <option value="descending">Z to A</option>
            </select>
          </label>
          <ul className="table-group-list" ref={listRef}>
            {groups.map((group) => (
              <li key={group.key} className={group.hidden ? "table-group-line table-group-line-hidden" : "table-group-line"} data-group-key={group.key}>
                <span className="mdi mdi-drag-vertical table-sort-handle" role="img" aria-label={`Move group ${group.label}`} {...drag.press(group)} />
                <span className="table-group-line-name">{group.label}</span>
                <button
                  type="button"
                  className="table-group-action"
                  aria-label={group.hidden ? `Show ${group.label}` : `Hide ${group.label}`}
                  aria-pressed={!group.hidden}
                  onClick={() => raise({ kind: group.hidden ? "showGroup" : "hideGroup", targetId: group.key })}
                >
                  <span className={`mdi ${group.hidden ? "mdi-eye-off-outline" : "mdi-eye-outline"}`} aria-hidden="true" />
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
      {hidden > 0 && (
        // Groups hidden one by one are brought back together: the view does not say which they were by name.
        <button type="button" className="table-group-show-hidden" onClick={() => raise({ kind: "showGroups" })}>
          {hidden === 1 ? "Show 1 hidden group" : `Show ${hidden} hidden groups`}
        </button>
      )}
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
