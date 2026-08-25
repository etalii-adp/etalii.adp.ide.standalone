import { useEffect, useRef, useState } from "react";
import { ContextPropertyEditor, type ContextProperty } from "../../generated/context_pb";

export interface PropertyRowProps {
  property: ContextProperty;
  /** Resolves to an error to show, or an empty string when the value was taken. */
  onCommit: (value: string) => Promise<string>;
}

/**
 * One row of the property grid: a label, and a value that can be edited unless its owner said
 * otherwise.
 *
 * The editing rule is the whole point of this component. **A value is written on Enter or on
 * losing focus, and never while typing.** Every write is a command on the project history, so
 * per-keystroke writes would put a document through one undo entry per character - and, for a
 * type whose file is executable configuration, through one state per character on disk. Escape
 * abandons the edit and puts the value back.
 *
 * While the field is focused it holds what the user typed; the moment it is not, it shows what
 * the backend last said. That is what makes a rejected value visibly snap back rather than sit
 * there looking accepted.
 */
export function PropertyRow({ property, onCommit }: PropertyRowProps) {
  const [draft, setDraft] = useState(property.value);
  const [editing, setEditing] = useState(false);
  const [error, setError] = useState("");
  const committing = useRef(false);

  // The pushed value wins whenever the row is not being edited - another connection may have
  // changed it, and an undo certainly will have.
  useEffect(() => {
    if (!editing) {
      setDraft(property.value);
    }
  }, [property.value, editing]);

  const editable = property.readOnlyReason.length === 0;

  async function commit() {
    setEditing(false);
    if (draft === property.value) {
      // Focus left a field nobody changed. Writing anyway would put an entry on the history
      // for having clicked somewhere.
      setError("");
      return;
    }

    committing.current = true;
    const failure = await onCommit(draft);
    committing.current = false;
    setError(failure);
    if (failure.length > 0) {
      setDraft(property.value);
    }
  }

  function abandon() {
    setDraft(property.value);
    setEditing(false);
    setError("");
  }

  if (!editable) {
    return (
      <div className="property-grid-row property-grid-row-readonly">
        <dt>{property.label}</dt>
        <dd>
          <span className="property-grid-value">{property.value}</span>
          {/* Why, not merely that: a reader who cannot change a value here deserves to know
              what would have to change instead. */}
          <span className="property-grid-readonly-reason">{property.readOnlyReason}</span>
        </dd>
      </div>
    );
  }

  const shared = {
    value: draft,
    onFocus: () => setEditing(true),
    onChange: (event: { target: { value: string } }) => setDraft(event.target.value),
    onBlur: () => void commit(),
    "aria-label": property.label,
  };

  return (
    <div className="property-grid-row">
      <dt>{property.label}</dt>
      <dd>
        {property.editor === ContextPropertyEditor.TEXT ? (
          <textarea
            className="property-grid-input property-grid-input-text"
            rows={3}
            {...shared}
            onKeyDown={(event) => {
              // Enter makes a new line in a multi-line value, so committing is Ctrl+Enter here.
              // Blur still commits, which is how most edits to a description actually end.
              if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
                event.preventDefault();
                event.currentTarget.blur();
              } else if (event.key === "Escape") {
                abandon();
              }
            }}
          />
        ) : property.editor === ContextPropertyEditor.TOGGLE ? (
          <input
            type="checkbox"
            className="property-grid-input property-grid-input-toggle"
            checked={draft === "true"}
            aria-label={property.label}
            onChange={(event) => {
              // A checkbox has no "finished typing": the click *is* the commit.
              const next = event.target.checked ? "true" : "false";
              setDraft(next);
              setEditing(false);
              void onCommit(next).then((failure) => {
                setError(failure);
                if (failure.length > 0) {
                  setDraft(property.value);
                }
              });
            }}
          />
        ) : (
          <input
            type="text"
            className="property-grid-input"
            {...shared}
            onKeyDown={(event) => {
              if (event.key === "Enter") {
                // Blur rather than commit directly, so Enter and clicking away take exactly
                // one path - there is no second way for a value to reach the backend.
                event.currentTarget.blur();
              } else if (event.key === "Escape") {
                abandon();
              }
            }}
          />
        )}
        {error.length > 0 && <span className="property-grid-error">{error}</span>}
      </dd>
    </div>
  );
}
