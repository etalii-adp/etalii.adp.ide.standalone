import { useState } from "react";
import type { TableGesture } from "../api/tableEvents";
import type { TableColumn, TableModel } from "../api/tableModel";
import type { TableDefinition } from "../definition/tableDefinition";
import { childPath, describeCondition, FilterEditor } from "./FilterEditor";
import { GroupEditor, PropertyVisibility } from "./GroupEditor";
import { describeSort, SortEditor } from "./SortEditor";

export interface ViewBarProps {
  model: TableModel;
  definition: TableDefinition;
  /** False for a table that cannot be edited: the bar then says what the view does and changes nothing. */
  editable: boolean;
  raise: (gesture: TableGesture) => void;
}

type Panel = "filter" | "sort" | "group" | "properties";

const CONTROLS: readonly { panel: Panel; label: string; icon: string }[] = [
  { panel: "filter", label: "Filter", icon: "mdi-filter-outline" },
  { panel: "sort", label: "Sort", icon: "mdi-sort" },
  { panel: "group", label: "Group", icon: "mdi-format-list-group" },
  { panel: "properties", label: "Properties", icon: "mdi-eye-outline" },
];

/**
 * The bar between the views and the table: the controls for filter, sort, group and which
 * properties are shown, and under them one removable item per filter and per sort the view has.
 * Pressing an item, or its control, opens the editor it belongs to; one is open at a time.
 */
export function ViewBar({ model, definition, editable, raise }: ViewBarProps) {
  const [open, setOpen] = useState<Panel | null>(null);
  const { settings, columns } = model;
  const filters = settings.filter?.items ?? [];
  const toggle = (panel: Panel) => setOpen((current) => (current === panel ? null : panel));

  return (
    <div className="table-view-bar">
      <div className="table-view-controls" role="toolbar" aria-label="View">
        {CONTROLS.map((control) => (
          <button
            key={control.panel}
            type="button"
            className="table-view-control"
            aria-expanded={open === control.panel}
            disabled={!editable}
            onClick={() => toggle(control.panel)}
          >
            <span className={`mdi ${control.icon}`} aria-hidden="true" />
            <span>{control.label}</span>
          </button>
        ))}
      </div>
      {(filters.length > 0 || settings.sorts.length > 0) && (
        <ul className="table-view-items" aria-label="Filters and sorts">
          {settings.sorts.map((sort) => {
            const text = describeSort(sort, columns);
            return (
              <li key={`sort:${sort.columnId}`} className="table-view-item" data-item="sort">
                <button type="button" className="table-view-item-open" disabled={!editable} onClick={() => setOpen("sort")}>
                  <span className={`mdi ${sort.descending ? "mdi-sort-descending" : "mdi-sort-ascending"}`} aria-hidden="true" />
                  <span>{text}</span>
                </button>
                {editable && (
                  <button type="button" className="table-view-item-remove" aria-label={`Remove sort ${text}`} onClick={() => raise({ kind: "removeSort", columnId: sort.columnId })}>
                    <span className="mdi mdi-close" aria-hidden="true" />
                  </button>
                )}
              </li>
            );
          })}
          {filters.map((item, index) => {
            const path = childPath("", index);
            const text = item.kind === "condition" ? describeCondition(item, columns, definition) : `${item.any ? "Any" : "All"} of ${item.items.length}`;
            return (
              <li key={`filter:${path}`} className="table-view-item" data-item="filter">
                <button type="button" className="table-view-item-open" disabled={!editable} onClick={() => setOpen("filter")}>
                  <span className="mdi mdi-filter-outline" aria-hidden="true" />
                  <span>{text}</span>
                </button>
                {editable && (
                  <button type="button" className="table-view-item-remove" aria-label={`Remove filter ${text}`} onClick={() => raise({ kind: "removeFilter", targetId: path })}>
                    <span className="mdi mdi-close" aria-hidden="true" />
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {editable && open !== null && <div className="table-view-panel">{panelOf(open, model, columns, definition, raise)}</div>}
    </div>
  );
}

function panelOf(panel: Panel, model: TableModel, columns: readonly TableColumn[], definition: TableDefinition, raise: (gesture: TableGesture) => void) {
  switch (panel) {
    case "filter":
      return <FilterEditor filter={model.settings.filter} columns={columns} definition={definition} raise={raise} />;
    case "sort":
      return <SortEditor sorts={model.settings.sorts} columns={columns} definition={definition} raise={raise} />;
    case "group":
      return <GroupEditor settings={model.settings} columns={columns} definition={definition} raise={raise} />;
    case "properties":
      return <PropertyVisibility columns={columns} raise={raise} />;
  }
}
