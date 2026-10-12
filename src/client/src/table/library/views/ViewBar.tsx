import { useRef, useState } from "react";
import { ContextMenu } from "../../../shell/context/ContextMenu";
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

/** The setting a view says under what its rows are searched for. */
const SEARCH = "search";

type Panel = "filter" | "sort" | "group" | "properties";

/** What the view's settings menu lists, in Notion's order and under Notion's names. */
const SETTINGS: readonly { panel: Panel; label: string; icon: string }[] = [
  { panel: "properties", label: "Property visibility", icon: "mdi-eye-outline" },
  { panel: "filter", label: "Filter", icon: "mdi-filter-outline" },
  { panel: "sort", label: "Sort", icon: "mdi-sort" },
  { panel: "group", label: "Group", icon: "mdi-format-list-group" },
];

/**
 * The bar between the views and the table. As in Notion, a view's settings are behind one
 * control at its right end: its menu lists property visibility, filter, sort and group, and
 * choosing one opens its editor. Beside it, one removable item per filter and per sort the view
 * has; pressing an item opens the editor it belongs to. One editor is open at a time. Before it,
 * where the table can be searched, the magnifying glass that opens the search box.
 */
export function ViewBar({ model, definition, editable, raise }: ViewBarProps) {
  const [open, setOpen] = useState<Panel | null>(null);
  const { settings, columns } = model;
  const filters = settings.filter?.items ?? [];
  const settingsRef = useRef<HTMLButtonElement>(null);
  const [menuAt, setMenuAt] = useState<{ x: number; y: number } | null>(null);
  // What the rows are searched for. It is typed here and kept by the connection, in no file: the
  // box is open while there is something in it, or once its button was pressed.
  const sought = settings.settings[SEARCH] ?? "";
  const [search, setSearch] = useState<string | null>(sought === "" ? null : sought);
  const searchFor = (text: string | null) => {
    setSearch(text);
    if ((text ?? "") !== (search ?? "")) {
      raise({ kind: "search", values: [text ?? ""] });
    }
  };

  return (
    <div className="table-view-bar">
      <div className="table-view-controls" role="toolbar" aria-label="View">
        {definition.searchable === true && (
          // Searching changes nothing, so a table that cannot be edited is searched all the same.
          <>
            <button type="button" className="table-view-control" aria-label="Search" aria-expanded={search !== null} onClick={() => searchFor(search === null ? "" : null)}>
              <span className="mdi mdi-magnify" aria-hidden="true" />
            </button>
            {search !== null && (
              <input
                type="search"
                className="table-view-search"
                aria-label="Search rows"
                placeholder="Type to search…"
                autoFocus
                value={search}
                onChange={(event) => searchFor(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === "Escape") {
                    event.preventDefault();
                    searchFor(null);
                  }
                }}
              />
            )}
          </>
        )}
        <button
          ref={settingsRef}
          type="button"
          className="table-view-control"
          aria-label="View settings"
          aria-haspopup="menu"
          aria-expanded={menuAt !== null}
          disabled={!editable}
          onClick={() => {
            const rect = settingsRef.current?.getBoundingClientRect();
            setMenuAt({ x: rect?.left ?? 0, y: rect?.bottom ?? 0 });
          }}
        >
          <span className="mdi mdi-tune-variant" aria-hidden="true" />
        </button>
        <ContextMenu
          open={menuAt !== null}
          groups={[SETTINGS.map((setting) => ({ id: setting.panel, label: setting.label, icon: setting.icon, onSelect: () => setOpen(setting.panel) }))]}
          position={menuAt ?? { x: 0, y: 0 }}
          onClose={() => setMenuAt(null)}
        />
        {open !== null && (
          <button type="button" className="table-view-control" aria-label="Close view settings" onClick={() => setOpen(null)}>
            <span className="mdi mdi-close" aria-hidden="true" />
          </button>
        )}
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
