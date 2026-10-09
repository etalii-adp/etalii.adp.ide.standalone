import { useRef } from "react";
import { usePointerGesture } from "../../../canvas/gesture/usePointerGesture";
import type { TableGesture } from "../api/tableEvents";
import type { TableColumn, TableSort } from "../api/tableModel";
import { kindOf, type TableDefinition } from "../definition/tableDefinition";
import { dropIndex } from "../header/columnActions";

/** The columns a view can be sorted by: those whose kind does not say it cannot be. */
export function sortableColumns(columns: readonly TableColumn[], definition: TableDefinition): TableColumn[] {
  return columns.filter((column) => kindOf(definition, column.kind).sortable !== false);
}

/** A sort in words, for the item that stands for it in the bar. */
export function describeSort(sort: TableSort, columns: readonly TableColumn[]): string {
  const name = columns.find((column) => column.id === sort.columnId)?.name ?? sort.columnId;
  return `${name} ${sort.descending ? "descending" : "ascending"}`;
}

export interface SortEditorProps {
  sorts: readonly TableSort[];
  columns: readonly TableColumn[];
  definition: TableDefinition;
  raise: (gesture: TableGesture) => void;
}

/**
 * The view's sorts, open for editing: which properties, each ascending or descending, in the
 * order they decide - the first deciding first, and the order set by dragging.
 */
export function SortEditor({ sorts, columns, definition, raise }: SortEditorProps) {
  const listRef = useRef<HTMLUListElement>(null);
  const unused = sortableColumns(columns, definition).filter((column) => !sorts.some((sort) => sort.columnId === column.id));

  const drag = usePointerGesture<TableSort>({
    onPress: () => {},
    onDragEnd: (sort, _dx, dy) => {
      // The same arithmetic a column drag uses, turned on its side: tops and bottoms for lefts and rights.
      const rows = Array.from(listRef.current?.querySelectorAll<HTMLElement>("li") ?? [], (row) => {
        const rect = row.getBoundingClientRect();
        return { left: rect.top, right: rect.bottom };
      });
      const from = sorts.findIndex((candidate) => candidate.columnId === sort.columnId);
      const row = rows[from];
      if (row === undefined) {
        return;
      }
      const to = dropIndex(from, (row.left + row.right) / 2 + dy, rows);
      if (to !== from) {
        raise({ kind: "moveSort", columnId: sort.columnId, index: to });
      }
    },
  });

  return (
    <div className="table-sort-editor" role="group" aria-label="Sort">
      <ul className="table-sort-list" ref={listRef}>
        {sorts.map((sort) => {
          const name = columns.find((column) => column.id === sort.columnId)?.name ?? sort.columnId;
          return (
            <li key={sort.columnId} className="table-sort-row" data-column-id={sort.columnId}>
              <span className="mdi mdi-drag-vertical table-sort-handle" role="img" aria-label={`Move sort ${name}`} {...drag.press(sort)} />
              <span className="table-sort-name">{name}</span>
              <select
                aria-label={`Direction of sort ${name}`}
                value={sort.descending ? "descending" : "ascending"}
                onChange={(event) => raise({ kind: "setSort", columnId: sort.columnId, settings: { direction: event.target.value } })}
              >
                <option value="ascending">Ascending</option>
                <option value="descending">Descending</option>
              </select>
              <button type="button" className="table-sort-remove" aria-label={`Remove sort ${name}`} onClick={() => raise({ kind: "removeSort", columnId: sort.columnId })}>
                <span className="mdi mdi-close" aria-hidden="true" />
              </button>
            </li>
          );
        })}
      </ul>
      {unused.length > 0 && (
        <select
          aria-label="Add a sort"
          value=""
          onChange={(event) => {
            if (event.target.value !== "") {
              raise({ kind: "addSort", columnId: event.target.value, settings: { direction: "ascending" } });
            }
          }}
        >
          <option value="">Add a sort…</option>
          {unused.map((column) => (
            <option key={column.id} value={column.id}>
              {column.name}
            </option>
          ))}
        </select>
      )}
    </div>
  );
}
