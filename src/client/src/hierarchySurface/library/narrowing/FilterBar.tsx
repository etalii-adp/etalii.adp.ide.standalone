import { type KeyboardEvent, useMemo, useRef, useState } from "react";
import type { HierarchySurfaceFilter, HierarchySurfaceModel } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceFilters, HierarchySurfaceLabelDefinition } from "../definition/hierarchySurfaceDefinition";
import { DOCUMENT_TARGET } from "../labels/listEditing";
import { completionsOf, ITEM_PREFIX, wordAt } from "./filterQuery";
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
  /** The labels a word can name, with their prefixes, and the lists of their members, which the field completes from. */
  labels: readonly HierarchySurfaceLabelDefinition[];
  optionLists: HierarchySurfaceModel["optionLists"];
}

/** What the name field is open for: storing the applied query, or renaming the stored filter it is. */
type Naming = { kind: "store" } | { kind: "rename"; filter: HierarchySurfaceFilter };

/**
 * The bar above the page that narrows and sorts it (FR-019, FR-020, FR-048): a query typed in the notation of DESL
 * section 7.7.3, applied as it is typed, the sort menu, and the filters the document stores, each applied by picking it.
 * The applied query is stored under a name, and the stored filter it is renamed or removed; those three are gestures, the
 * rest writes nothing (FR-049). The word being typed is completed from a list under the field: the labels' members with
 * their prefixes, the prefixes, and the sort keys; Tab or Enter takes the one marked, a click any.
 */
export function FilterBar({ text, onChange, sort, onSort, sortKeys, stored, filters, shown, total, canEdit, onGesture, labels, optionLists }: FilterBarProps) {
  const [naming, setNaming] = useState<Naming | null>(null);
  const [name, setName] = useState("");
  const applied = stored.find((filter) => filter.query === text.trim());
  const { operations, params } = filters;
  const field = useRef<HTMLInputElement>(null);
  // Where the caret is while the field has the focus; null while it has not, when nothing is offered.
  const [caret, setCaret] = useState<number | null>(null);
  const [active, setActive] = useState(0);
  const [dismissed, setDismissed] = useState(false);
  const word = caret === null ? null : wordAt(text, caret);
  const offered = useMemo(
    () => (word === null || dismissed ? [] : completionsOf(word.typed, labels, optionLists, sortKeys).slice(0, 12)),
    [word?.typed, dismissed, labels, optionLists, sortKeys],
  );
  const marked = Math.min(active, offered.length - 1);

  /** The word being typed becomes `completion`; a prefix stays open for its name, a whole word gets a space after it. */
  const complete = (completion: string) => {
    if (word === null) {
      return;
    }
    const whole = !(labels.some((label) => label.prefix === completion) || completion === ITEM_PREFIX || completion.endsWith(":"));
    const inserted = whole ? `${completion} ` : completion;
    const rest = text.slice(word.end).replace(/^\s+/, "");
    // A whole word brings its own space; a prefix is parted from what follows it.
    const tail = rest === "" || whole ? rest : ` ${rest}`;
    const next = `${text.slice(0, word.start)}${inserted}${tail}`;
    const at = word.start + inserted.length;
    onChange(next);
    setCaret(at);
    setActive(0);
    requestAnimationFrame(() => field.current?.setSelectionRange(at, at));
  };

  const onQueryKey = (event: KeyboardEvent<HTMLInputElement>) => {
    if (offered.length === 0) {
      if (event.key === "ArrowDown" && dismissed) {
        setDismissed(false);
      }
      return;
    }
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      setActive((marked + (event.key === "ArrowDown" ? 1 : -1) + offered.length) % offered.length);
    } else if (event.key === "Enter" || (event.key === "Tab" && !event.shiftKey)) {
      event.preventDefault();
      complete(offered[marked]!.word);
    } else if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      setDismissed(true);
    }
  };
  const track = () => {
    setCaret(field.current?.selectionStart ?? text.length);
  };

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
      <span className="hs-filter-field">
        <input
          ref={field}
          type="search"
          className="hs-filter-query"
          aria-label="Filter"
          aria-autocomplete="list"
          aria-controls="hs-filter-completions"
          aria-activedescendant={offered.length > 0 ? `hs-filter-completion-${marked}` : undefined}
          placeholder={[...labels.map((label) => `${label.prefix}name`), `${ITEM_PREFIX}back*`].join(" ")}
          value={text}
          spellCheck={false}
          autoComplete="off"
          onChange={(event) => {
            onChange(event.target.value);
            setCaret(event.target.selectionStart ?? event.target.value.length);
            setActive(0);
            setDismissed(false);
          }}
          onKeyDown={onQueryKey}
          onKeyUp={track}
          onClick={track}
          onFocus={track}
          onBlur={() => setCaret(null)}
        />
        {offered.length > 0 && (
          <ul id="hs-filter-completions" className="hs-filter-completions" role="listbox" aria-label="Completions">
            {offered.map((completion, at) => (
              <li
                key={completion.word}
                id={`hs-filter-completion-${at}`}
                role="option"
                aria-selected={at === marked}
                className={`hs-filter-completion${at === marked ? " hs-filter-completion-active" : ""}`}
                // Pressed before the field loses the focus, so the field keeps it and the word is taken.
                onPointerDown={(event) => {
                  event.preventDefault();
                  complete(completion.word);
                }}
                onPointerEnter={() => setActive(at)}
              >
                <span className="hs-filter-completion-word">{completion.word}</span>
                <span className="hs-filter-completion-hint">{completion.hint}</span>
              </li>
            ))}
          </ul>
        )}
      </span>
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
