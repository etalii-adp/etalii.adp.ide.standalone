import { type KeyboardEvent, type MouseEvent, useEffect, useRef } from "react";

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
}

/**
 * The field a node's title or description is typed in, in place on the page (spec 029 R11). What it holds is the one
 * thing on the page that can be unsaved: a gesture is written as soon as it is made, a field only when it is committed.
 */
export function EntryTextField({ text, multiline, label, onChange, onCommit, onCancel }: EntryTextFieldProps) {
  const ref = useRef<HTMLInputElement & HTMLTextAreaElement>(null);
  // Leaving the field by Escape is no commit, though the blur that follows looks like one.
  const cancelled = useRef(false);

  useEffect(() => {
    ref.current?.focus();
    ref.current?.select();
  }, []);

  const shared = {
    ref,
    className: `hs-field${multiline ? " hs-field-multiline" : ""}`,
    "aria-label": label,
    value: text,
    onClick: (event: MouseEvent) => event.stopPropagation(),
    onDoubleClick: (event: MouseEvent) => event.stopPropagation(),
    onBlur: () => {
      if (!cancelled.current) {
        onCommit();
      }
    },
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

  return multiline ? (
    <textarea {...shared} rows={Math.max(2, text.split("\n").length)} onChange={(event) => onChange(event.target.value)} />
  ) : (
    <input {...shared} type="text" onChange={(event) => onChange(event.target.value)} />
  );
}
