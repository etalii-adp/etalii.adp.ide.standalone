import { useId, useMemo, useState, type KeyboardEvent } from "react";
import "./SearchList.css";

export interface SearchListItem {
  id: string;
  label: string;
}

export interface SearchListProps {
  /** What the list is for, read out with the search field. */
  label: string;
  items: readonly SearchListItem[];
  /** The ids that are chosen now. */
  selected: readonly string[];
  /** Shown in the search field while it is empty. */
  placeholder?: string;
  /** An item was picked - or, when it was already chosen, picked again. */
  onPick: (id: string) => void;
  /**
   * The typed text matches no item and the user asked for it to exist. Left out, the list only
   * picks: nothing offers to create.
   */
  onCreate?: (text: string) => void;
  /** Escape was pressed in the list. */
  onClose?: () => void;
}

/** What a row of the list is: an item to pick, or the offer to create what was typed. */
type Row = { kind: "item"; item: SearchListItem } | { kind: "create"; text: string };

/** The items whose label holds the typed text, case ignored; all of them for no text. */
export function matching(items: readonly SearchListItem[], text: string): SearchListItem[] {
  const wanted = text.trim().toLowerCase();
  return wanted === "" ? [...items] : items.filter((item) => item.label.toLowerCase().includes(wanted));
}

/**
 * A list that is searched by typing, picks with Enter or a click, and - when its owner allows -
 * creates an item from text that matches none.
 *
 * It holds no choice of its own: what is chosen comes in as `selected` and a pick goes out as an
 * event, so the same list serves one value and several. It is a combobox over a listbox to
 * assistive technology, with the row the arrow keys are on announced as the active one.
 */
export function SearchList({ label, items, selected, placeholder = "", onPick, onCreate, onClose }: SearchListProps) {
  const [text, setText] = useState("");
  const [active, setActive] = useState(0);
  const listId = useId();

  const rows = useMemo<Row[]>(() => {
    const found: Row[] = matching(items, text).map((item) => ({ kind: "item", item }));
    const typed = text.trim();
    // Offered only for text that names no item exactly: creating a second "Capital" beside the
    // first is never what was meant.
    if (onCreate !== undefined && typed !== "" && !items.some((item) => item.label.toLowerCase() === typed.toLowerCase())) {
      found.push({ kind: "create", text: typed });
    }
    return found;
  }, [items, text, onCreate]);

  const current = Math.min(active, Math.max(0, rows.length - 1));

  const choose = (row: Row | undefined) => {
    if (row === undefined) {
      return;
    }
    if (row.kind === "item") {
      onPick(row.item.id);
    } else {
      onCreate?.(row.text);
      setText("");
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActive(Math.min(current + 1, rows.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActive(Math.max(current - 1, 0));
    } else if (event.key === "Enter") {
      event.preventDefault();
      event.stopPropagation();
      choose(rows[current]);
    } else if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      onClose?.();
    }
  };

  return (
    <div className="search-list">
      <input
        className="search-list-field"
        type="text"
        role="combobox"
        aria-label={label}
        aria-expanded="true"
        aria-controls={listId}
        aria-activedescendant={rows.length > 0 ? `${listId}-${current}` : undefined}
        placeholder={placeholder}
        value={text}
        data-dialog-autofocus=""
        onChange={(event) => {
          setText(event.target.value);
          setActive(0);
        }}
        onKeyDown={onKeyDown}
      />
      <ul className="search-list-rows" id={listId} role="listbox" aria-label={label}>
        {rows.map((row, index) => {
          const id = `${listId}-${index}`;
          const isActive = index === current;
          if (row.kind === "create") {
            return (
              <li key="create" id={id} className={`search-list-row search-list-create${isActive ? " search-list-row-active" : ""}`} role="option" aria-selected="false" onMouseDown={(event) => event.preventDefault()} onClick={() => choose(row)}>
                <span className="mdi mdi-plus" aria-hidden="true" />
                <span>Create “{row.text}”</span>
              </li>
            );
          }
          const chosen = selected.includes(row.item.id);
          return (
            <li
              key={row.item.id}
              id={id}
              className={`search-list-row${isActive ? " search-list-row-active" : ""}`}
              role="option"
              aria-selected={chosen}
              // The field keeps the focus: a click that took it would end whatever the list is part of.
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => choose(row)}
            >
              <span className={`mdi ${chosen ? "mdi-check" : "mdi-blank"}`} aria-hidden="true" />
              <span>{row.item.label}</span>
            </li>
          );
        })}
        {rows.length === 0 && (
          <li className="search-list-empty" role="presentation">
            Nothing matches.
          </li>
        )}
      </ul>
    </div>
  );
}
