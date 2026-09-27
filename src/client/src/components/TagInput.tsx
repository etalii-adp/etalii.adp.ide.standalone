import { useId, useMemo, useState, type KeyboardEvent } from "react";

export interface TagInputProps {
  /** The tags shown as chips, in order. */
  tags: readonly string[];
  /** The tags the field looks typed text up among: those already used elsewhere. */
  suggestions: readonly string[];
  /** Called with the whole new list whenever a tag is added or removed. */
  onChange: (tags: string[]) => void;
  /** What the field is called, for a screen reader and as its placeholder. */
  label: string;
  className?: string;
  /** How many suggestions the list shows at most. */
  maxSuggestions?: number;
}

const same = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: "accent" }) === 0;

/**
 * A set of tags as removable chips, and a field that looks what is typed up among the tags
 * already in use.
 *
 * Enter, Tab with text or a comma adds the highlighted suggestion, else the tag spelled as typed
 * - an existing tag in its existing spelling when only the case differs, so one tag never becomes
 * two - and a new tag when none matches. A click on a suggestion adds it. Backspace in an empty
 * field removes the last chip; each chip's x removes that one. A comma is never part of a tag,
 * because a list of tags travels as text separated by commas.
 */
export function TagInput({ tags, suggestions, onChange, label, className, maxSuggestions = 8 }: TagInputProps) {
  const [text, setText] = useState("");
  const [highlight, setHighlight] = useState(0);
  const [open, setOpen] = useState(false);
  // Whether the arrows picked a suggestion, so Enter in an empty field adds only a chosen one.
  const [navigated, setNavigated] = useState(false);
  const listId = useId();

  const typed = text.trim();
  const matches = useMemo(() => {
    const wanted = typed.toLowerCase();
    const unique = [...new Set(suggestions)].filter((tag) => !tags.some((chosen) => same(chosen, tag)));
    return unique
      .filter((tag) => tag.toLowerCase().includes(wanted))
      // Those starting with what was typed first - the one spelled as typed among them - then
      // alphabetically.
      .sort((a, b) => Number(!a.toLowerCase().startsWith(wanted)) - Number(!b.toLowerCase().startsWith(wanted)) || a.localeCompare(b))
      .slice(0, maxSuggestions);
  }, [suggestions, tags, typed, maxSuggestions]);
  const shown = open && matches.length > 0;

  function add(tag: string) {
    const clean = tag.replaceAll(",", " ").trim();
    setText("");
    setHighlight(0);
    setNavigated(false);
    if (clean.length === 0) {
      return;
    }
    const existing = suggestions.find((candidate) => same(candidate, clean)) ?? clean;
    if (!tags.some((chosen) => same(chosen, existing))) {
      onChange([...tags, existing]);
    }
  }

  function remove(index: number) {
    onChange(tags.filter((_, at) => at !== index));
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown" && matches.length > 0) {
      event.preventDefault();
      setOpen(true);
      setNavigated(true);
      setHighlight((at) => (navigated ? (at + 1) % matches.length : 0));
    } else if (event.key === "ArrowUp" && matches.length > 0) {
      event.preventDefault();
      setOpen(true);
      setNavigated(true);
      setHighlight((at) => (at - 1 + matches.length) % matches.length);
    } else if (event.key === "Enter" || event.key === "," || (event.key === "Tab" && typed.length > 0)) {
      event.preventDefault();
      // The suggestion the list offers for what was typed - an existing tag spelled as typed sorts
      // first - or the one the arrows chose; and only then a new tag, as typed.
      const offered = shown && (typed.length > 0 || navigated) ? matches[highlight] : undefined;
      add(offered ?? typed);
    } else if (event.key === "Backspace" && text.length === 0 && tags.length > 0) {
      remove(tags.length - 1);
    } else if (event.key === "Escape") {
      setText("");
      setOpen(false);
    }
  }

  return (
    <div className={["tag-input", className ?? ""].filter(Boolean).join(" ")}>
      <ul className="tag-input-chips" aria-label={label}>
        {tags.map((tag, index) => (
          <li key={tag} className="tag-input-chip">
            <span className="tag-input-chip-text">{tag}</span>
            <button type="button" className="tag-input-chip-remove" aria-label={`Remove ${tag}`} onClick={() => remove(index)}>
              ×
            </button>
          </li>
        ))}
      </ul>
      <input
        type="text"
        className="tag-input-field"
        placeholder={tags.length === 0 ? label : ""}
        aria-label={label}
        role="combobox"
        aria-expanded={shown}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={shown ? `${listId}-${highlight}` : undefined}
        value={text}
        onChange={(event) => {
          setText(event.target.value);
          setHighlight(0);
          setNavigated(false);
          setOpen(true);
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onKeyDown={onKeyDown}
      />
      {shown && (
        <ul id={listId} className="tag-input-suggestions" role="listbox">
          {matches.map((tag, index) => (
            <li
              key={tag}
              id={`${listId}-${index}`}
              role="option"
              aria-selected={index === highlight}
              className={`tag-input-suggestion${index === highlight ? " tag-input-suggestion-active" : ""}`}
              // Before the field's blur closes the list, so the click lands.
              onMouseDown={(event) => {
                event.preventDefault();
                add(tag);
              }}
            >
              {tag}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
