import { type KeyboardEvent, useState } from "react";
import type { HierarchySurfaceFilter } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceFilters } from "../definition/hierarchySurfaceDefinition";
import { DOCUMENT_TARGET } from "../labels/listEditing";
import { SortMenu, type SortKey } from "./SortMenu";

export interface FilterBarProps {
  /** The query applied, as typed. */
  text: string;
  onChange: (text: string) => void;
  /** The sort key the query holds, or empty. */
  sort: string;
  onSort: (key: string) => void;
  sortKeys: readonly SortKey[];
  /** The filters the document stores, in their order. */
  stored: readonly HierarchySurfaceFilter[];
  filters: HierarchySurfaceFilters;
  /** How many entries the page shows, and of how many; equal when nothing narrows. */
  shown: number;
  total: number;
  /** Whether a filter can be stored, renamed and removed: false while the document cannot be changed. */
  canEdit: boolean;
  onGesture: (operation: string, targetId: string, params: Readonly<Record<string, string>>) => unknown;
}

/** What the name field is open for: storing the applied query, or renaming the stored filter it is. */
type Naming = { kind: "store" } | { kind: "rename"; filter: HierarchySurfaceFilter };

/**
 * The bar above the page that narrows and sorts it (FR-019, FR-020, FR-048): a query typed in the notation of DESL
 * section 7.7.3, applied as it is typed, the sort menu, and the filters the document stores, each applied by picking it.
 * The applied query is stored under a name, and the stored filter it is renamed or removed; those three are gestures, the
 * rest writes nothing (FR-049).
 */
export function FilterBar({ text, onChange, sort, onSort, sortKeys, stored, filters, shown, total, canEdit, onGesture }: FilterBarProps) {
  const [naming, setNaming] = useState<Naming | null>(null);
  const [name, setName] = useState("");
  const applied = stored.find((filter) => filter.query === text.trim());
  const { operations, params } = filters;

  const startNaming = (next: Naming) => {
    setNaming(next);
    setName(next.kind === "rename" ? next.filter.name : "");
  };
  const commitName = () => {
    const typed = name.trim();
    if (naming !== null && typed !== "") {
      if (naming.kind === "store") {
        onGesture(operations.store, DOCUMENT_TARGET, { [params.name]: typed, [params.query]: text.trim() });
      } else if (typed !== naming.filter.name) {
        onGesture(operations.rename, naming.filter.id, { [params.name]: typed });
      }
    }
    setNaming(null);
  };
  const onNameKey = (event: KeyboardEvent) => {
    event.stopPropagation();
    if (event.key === "Enter") {
      event.preventDefault();
      commitName();
    } else if (event.key === "Escape") {
      event.preventDefault();
      setNaming(null);
    }
  };

  return (
    <div className="hs-filter-bar" role="search" aria-label="Narrow and sort">
      <span className="mdi mdi-filter-variant" aria-hidden="true" />
      <input
        type="search"
        className="hs-filter-query"
        aria-label="Filter"
        placeholder="#tag category:name state:name item:back*"
        value={text}
        spellCheck={false}
        onChange={(event) => onChange(event.target.value)}
      />
      <SortMenu keys={sortKeys} value={sort} onChange={onSort} />
      {stored.length > 0 && (
        <select
          aria-label="Stored filters"
          className="hs-stored-filters"
          value={applied?.id ?? ""}
          onChange={(event) => {
            const filter = stored.find((candidate) => candidate.id === event.target.value);
            if (filter !== undefined) {
              onChange(filter.query);
            }
          }}
        >
          <option value="">Stored filters</option>
          {stored.map((filter) => (
            <option key={filter.id} value={filter.id}>
              {filter.name}
            </option>
          ))}
        </select>
      )}
      {naming !== null ? (
        <input
          type="text"
          className="hs-filter-name"
          aria-label={naming.kind === "store" ? "Name of the filter" : `New name of ${naming.filter.name}`}
          placeholder="Name"
          // The field opens on a press of its button, for typing at once.
          autoFocus
          value={name}
          onChange={(event) => setName(event.target.value)}
          onKeyDown={onNameKey}
          onBlur={commitName}
        />
      ) : (
        canEdit && (
          <>
            {text.trim() !== "" && applied === undefined && (
              <button type="button" className="hs-filter-button" onClick={() => startNaming({ kind: "store" })} title="Store this filter under a name">
                <span className="mdi mdi-bookmark-plus-outline" aria-hidden="true" /> Store
              </button>
            )}
            {applied !== undefined && (
              <>
                <button type="button" className="hs-filter-button" aria-label={`Rename ${applied.name}`} title="Rename" onClick={() => startNaming({ kind: "rename", filter: applied })}>
                  <span className="mdi mdi-pencil-outline" aria-hidden="true" />
                </button>
                <button type="button" className="hs-filter-button" aria-label={`Remove ${applied.name}`} title="Remove" onClick={() => onGesture(operations.remove, applied.id, {})}>
                  <span className="mdi mdi-bookmark-remove-outline" aria-hidden="true" />
                </button>
              </>
            )}
          </>
        )
      )}
      {text !== "" && (
        <button type="button" className="hs-filter-button" aria-label="Clear the filter" title="Clear" onClick={() => onChange("")}>
          <span className="mdi mdi-close" aria-hidden="true" />
        </button>
      )}
      <span className="hs-filter-count" role="status">
        {shown === total ? `${total} ${filters.entries}` : `${shown} of ${total} ${filters.entries}`}
      </span>
    </div>
  );
}
