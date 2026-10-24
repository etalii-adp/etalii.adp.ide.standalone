import { type FocusEvent, type KeyboardEvent, type MouseEvent, useEffect, useLayoutEffect, useRef, useState } from "react";
import type { HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import { CONFLICT_BAR } from "../ConflictBar";
import { REFERENCE_LOOKUP, ReferenceLookup } from "../references/ReferenceLookup";

export interface EntryTextFieldProps {
  /** The text being typed. */
  text: string;
  /** A description holds lines; a title is one. */
  multiline: boolean;
  /** What the field is, for a screen reader: "Title of REQ-0004". */
  label: string;
  onChange: (text: string) => void;
  /** Keeps the text: Enter for a title, Ctrl+Enter for a description, or leaving the field. */
  onCommit: () => void;
  /** Drops the text: Escape. */
  onCancel: () => void;
  /** What `#` offers to refer to (FR-017); absent where the text holds no references. */
  references?: readonly HierarchySurfaceNode[];
}

/**
 * The field a node's title or description is typed in, in place on the page (spec 029 R11). What it holds is the one
 * thing on the page that can be unsaved: a gesture is written as soon as it is made, a field only when it is committed.
 *
 * Typing `#` where references are offered opens the lookup in place of the `#`; the pick is written as its bracketed
 * number, which the page then shows as a box. While the lookup is open the field is not left: the text is committed
 * only once the focus is outside both.
 *
 * It stands where the text stood, in its font, and is as high as its text wraps to, so a title or description opened
 * for typing keeps the size of what it replaced. A title is one line however it wraps: a line break typed or pasted
 * into it is taken out.
 */
export function EntryTextField({ text, multiline, label, onChange, onCommit, onCancel, references }: EntryTextFieldProps) {
  const ref = useRef<HTMLTextAreaElement>(null);
  // Leaving the field by Escape is no commit, though the blur that follows looks like one.
  const cancelled = useRef(false);
  // Where the `#` that opened the lookup stands, while it is open.
  const [lookupAt, setLookupAt] = useState<number | null>(null);
  // Where the caret goes once the text that a pick or a closed lookup leaves has arrived.
  const caret = useRef<number | null>(null);

  useEffect(() => {
    ref.current?.focus();
    ref.current?.select();
  }, []);

  // As high as the text wraps to, as the text it replaced was; where it stands in a column that leaves it more, it grows
  // into that, so it is measured without growing.
  useLayoutEffect(() => {
    const field = ref.current;
    if (field !== null) {
      field.style.flexGrow = "0";
      field.style.height = "auto";
      field.style.height = `${field.scrollHeight + field.offsetHeight - field.clientHeight}px`;
      field.style.flexGrow = "";
    }
  }, [text]);

  useEffect(() => {
    if (caret.current !== null && lookupAt === null) {
      ref.current?.focus();
      ref.current?.setSelectionRange(caret.current, caret.current);
      caret.current = null;
    }
  }, [text, lookupAt]);

  /** Leaving the field and its lookup keeps the text, unless it was dropped or the bar of an outside change took the focus. */
  const leave = (event: FocusEvent) => {
    const to = event.relatedTarget instanceof Element ? event.relatedTarget : null;
    // A lookup that is closing gives the focus back to the field; its going is no leaving.
    if (cancelled.current || caret.current !== null || to?.closest(`.${CONFLICT_BAR}`) || to?.closest(`.${REFERENCE_LOOKUP}`) || (to !== null && to === ref.current)) {
      return;
    }
    setLookupAt(null);
    onCommit();
  };

  const change = (typed: string, typedAt: number) => {
    const value = multiline ? typed : typed.replace(/\r?\n/g, " ");
    const selection = Math.min(typedAt, value.length);
    // One `#` typed into the text, and nothing else changed.
    const at = selection - 1;
    if (references !== undefined && value.length === text.length + 1 && value[at] === "#" && value.slice(0, at) + value.slice(at + 1) === text) {
      setLookupAt(at);
    }
    onChange(value);
  };

  const shared = {
    ref,
    className: `hs-field hs-field-inplace${multiline ? " hs-field-multiline" : " hs-field-title"}`,
    "aria-label": label,
    value: text,
    onClick: (event: MouseEvent) => event.stopPropagation(),
    onDoubleClick: (event: MouseEvent) => event.stopPropagation(),
    onBlur: leave,
    onKeyDown: (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        cancelled.current = true;
        onCancel();
      } else if (event.key === "Enter" && (!multiline || event.ctrlKey || event.metaKey)) {
        event.preventDefault();
        onCommit();
      }
    },
  };

  const lookup = lookupAt !== null && references !== undefined && (
    <ReferenceLookup
      candidates={references}
      onPick={(number) => {
        // The `#` becomes the reference.
        const written = `[${number}]`;
        caret.current = lookupAt + written.length;
        setLookupAt(null);
        onChange(text.slice(0, lookupAt) + written + text.slice(lookupAt + 1));
      }}
      onClose={() => {
        caret.current = lookupAt + 1;
        setLookupAt(null);
      }}
      onLeave={leave}
    />
  );

  return (
    <>
      <textarea {...shared} rows={1} onChange={(event) => change(event.target.value, event.target.selectionStart)} />
      {lookup}
    </>
  );
}
