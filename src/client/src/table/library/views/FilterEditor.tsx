import { useState } from "react";
import type { TableGesture } from "../api/tableEvents";
import type { TableColumn, TableCondition, TableFilterGroup } from "../api/tableModel";
import { kindOf, type TableComparison, type TableDefinition } from "../definition/tableDefinition";

/** How deep a filter's groups may be nested, the outermost counted as the first. */
export const MAXIMUM_FILTER_DEPTH = 3;

/** The comparisons every kind has, whatever else its definition gives it. */
export const EMPTINESS: readonly TableComparison[] = [
  { id: "isEmpty", label: "is empty", takesValue: false },
  { id: "isNotEmpty", label: "is not empty", takesValue: false },
];

/** What a column of this kind can be compared by: its definition's comparisons, then the two every kind has. */
export function comparisonsOf(definition: TableDefinition, kind: string): readonly TableComparison[] {
  const own = kindOf(definition, kind).comparisons ?? [];
  return [...own, ...EMPTINESS.filter((empty) => !own.some((comparison) => comparison.id === empty.id))];
}

/**
 * Where a filter's item is: the indexes that lead to it from the outermost group, joined by a
 * slash. The outermost group itself is the empty path. A condition has no identity beyond its
 * place, so its place is how a gesture names it.
 */
export function childPath(parent: string, index: number): string {
  return parent === "" ? String(index) : `${parent}/${index}`;
}

/** A condition in words, for the item that stands for it in the bar. */
export function describeCondition(condition: TableCondition, columns: readonly TableColumn[], definition: TableDefinition): string {
  const column = columns.find((candidate) => candidate.id === condition.columnId);
  const comparison = column === undefined ? undefined : comparisonsOf(definition, column.kind).find((candidate) => candidate.id === condition.comparison);
  const value = condition.values.join(", ");
  return [column?.name ?? condition.columnId, comparison?.label ?? condition.comparison, value].filter((part) => part !== "").join(" ");
}

export interface FilterEditorProps {
  /** The view's filter, or undefined when it has none yet. */
  filter: TableFilterGroup | undefined;
  columns: readonly TableColumn[];
  definition: TableDefinition;
  raise: (gesture: TableGesture) => void;
}

/**
 * The view's filter, open for editing: conditions joined by *all* or *any*, in groups nested up
 * to three deep. Every change is one gesture naming the item by its place; the editor holds no
 * copy of the filter, so what it shows is always what the document says.
 */
export function FilterEditor({ filter, columns, definition, raise }: FilterEditorProps) {
  return (
    <div className="table-filter-editor" role="group" aria-label="Filter">
      <FilterGroupEditor group={filter ?? { kind: "group", any: false, items: [] }} path="" depth={1} columns={columns} definition={definition} raise={raise} />
    </div>
  );
}

interface FilterGroupEditorProps {
  group: TableFilterGroup;
  path: string;
  /** How deep this group is, the outermost being 1. */
  depth: number;
  columns: readonly TableColumn[];
  definition: TableDefinition;
  raise: (gesture: TableGesture) => void;
}

function FilterGroupEditor({ group, path, depth, columns, definition, raise }: FilterGroupEditorProps) {
  const label = depth === 1 ? "Rows match" : `Group ${path} matches`;
  return (
    <div className="table-filter-group" data-filter-depth={depth}>
      <label className="table-filter-match">
        <span>{label}</span>
        <select aria-label={label} value={group.any ? "any" : "all"} onChange={(event) => raise({ kind: "setFilterMatch", targetId: path, settings: { match: event.target.value } })}>
          <option value="all">all of</option>
          <option value="any">any of</option>
        </select>
      </label>
      {group.items.map((item, index) => {
        const itemPath = childPath(path, index);
        return item.kind === "condition" ? (
          <ConditionEditor key={itemPath} condition={item} path={itemPath} columns={columns} definition={definition} raise={raise} />
        ) : (
          <div key={itemPath} className="table-filter-nested">
            <FilterGroupEditor group={item} path={itemPath} depth={depth + 1} columns={columns} definition={definition} raise={raise} />
            <button type="button" className="table-filter-remove" aria-label={`Remove group ${itemPath}`} onClick={() => raise({ kind: "removeFilter", targetId: itemPath })}>
              <span className="mdi mdi-close" aria-hidden="true" />
            </button>
          </div>
        );
      })}
      <div className="table-filter-add">
        <label>
          <span className="table-visually-hidden">{`Add a condition to ${path === "" ? "the filter" : `group ${path}`}`}</span>
          <select
            aria-label={`Add a condition to ${path === "" ? "the filter" : `group ${path}`}`}
            value=""
            onChange={(event) => {
              const column = columns.find((candidate) => candidate.id === event.target.value);
              if (column !== undefined) {
                raise({ kind: "addFilter", columnId: column.id, targetId: path, settings: { comparison: comparisonsOf(definition, column.kind)[0]!.id } });
              }
            }}
          >
            <option value="">Add a condition…</option>
            {columns.map((column) => (
              <option key={column.id} value={column.id}>
                {column.name}
              </option>
            ))}
          </select>
        </label>
        {
          // A group inside the deepest group would be a fourth level, which a filter does not have.
          depth < MAXIMUM_FILTER_DEPTH && (
            <button type="button" className="table-filter-add-group" onClick={() => raise({ kind: "addFilterGroup", targetId: path })}>
              Add a group
            </button>
          )
        }
      </div>
    </div>
  );
}

interface ConditionEditorProps {
  condition: TableCondition;
  path: string;
  columns: readonly TableColumn[];
  definition: TableDefinition;
  raise: (gesture: TableGesture) => void;
}

function ConditionEditor({ condition, path, columns, definition, raise }: ConditionEditorProps) {
  const column = columns.find((candidate) => candidate.id === condition.columnId);
  const comparisons = column === undefined ? EMPTINESS : comparisonsOf(definition, column.kind);
  const comparison = comparisons.find((candidate) => candidate.id === condition.comparison);
  // The value is the one thing typed, so it is held here until it is committed; the rest is chosen and raised at once.
  const [value, setValue] = useState(condition.values[0] ?? "");

  const set = (next: { columnId?: string; comparison?: string; values?: readonly string[] }) =>
    raise({
      kind: "setFilter",
      targetId: path,
      columnId: next.columnId ?? condition.columnId,
      settings: { comparison: next.comparison ?? condition.comparison },
      values: next.values ?? condition.values,
    });

  const commitValue = () => {
    if (value !== (condition.values[0] ?? "")) {
      set({ values: value === "" ? [] : [value] });
    }
  };

  return (
    <div className="table-filter-condition" data-filter-path={path}>
      <select
        aria-label={`Property of condition ${path}`}
        value={condition.columnId}
        onChange={(event) => {
          const next = columns.find((candidate) => candidate.id === event.target.value);
          if (next !== undefined) {
            // Another property may not have this comparison: it starts on that property's first.
            const offered = comparisonsOf(definition, next.kind);
            set({ columnId: next.id, comparison: offered.some((candidate) => candidate.id === condition.comparison) ? condition.comparison : offered[0]!.id });
          }
        }}
      >
        {column === undefined && <option value={condition.columnId}>{condition.columnId}</option>}
        {columns.map((candidate) => (
          <option key={candidate.id} value={candidate.id}>
            {candidate.name}
          </option>
        ))}
      </select>
      <select aria-label={`Comparison of condition ${path}`} value={condition.comparison} onChange={(event) => set({ comparison: event.target.value })}>
        {comparison === undefined && <option value={condition.comparison}>{condition.comparison}</option>}
        {comparisons.map((candidate) => (
          <option key={candidate.id} value={candidate.id}>
            {candidate.label}
          </option>
        ))}
      </select>
      {comparison?.takesValue !== false && (
        <input
          type="text"
          aria-label={`Value of condition ${path}`}
          value={value}
          onChange={(event) => setValue(event.target.value)}
          onBlur={commitValue}
          onKeyDown={(event) => {
            if (event.key === "Enter") {
              event.preventDefault();
              commitValue();
            }
          }}
        />
      )}
      <button type="button" className="table-filter-remove" aria-label={`Remove condition ${path}`} onClick={() => raise({ kind: "removeFilter", targetId: path })}>
        <span className="mdi mdi-close" aria-hidden="true" />
      </button>
    </div>
  );
}
