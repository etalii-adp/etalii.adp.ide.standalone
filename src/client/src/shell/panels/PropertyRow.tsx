import { useEffect, useRef, useState } from "react";
import { ContextPropertyEditor } from "../../generated/context-contract_pb";
import { type ContextProperty } from "../../generated/context_pb";
import { TagInput } from "../../components/TagInput";

export interface PropertyRowProps {
  property: ContextProperty;
  /** Resolves to an error to show, or an empty string when the value was taken. */
  onCommit: (value: string) => Promise<string>;
}

/** The editors this client can draw. Anything else is shown, never edited - see below. */
const KNOWN_EDITORS = new Set<ContextPropertyEditor>([
  ContextPropertyEditor.LINE,
  ContextPropertyEditor.TEXT,
  ContextPropertyEditor.TOGGLE,
  ContextPropertyEditor.CHOICE,
  ContextPropertyEditor.SLIDER,
  ContextPropertyEditor.TAGS,
]);

/** A TAGS value's tags: separated by commas, trimmed, blanks dropped. */
const tagsOf = (value: string) =>
  value
    .split(",")
    .map((tag) => tag.trim())
    .filter((tag) => tag.length > 0);

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

  // A slider writes while it is dragged, one write in flight at a time: `sliding` is the value the
  // row is waiting to see pushed back, and `queued` the newest stop reached while a write was out.
  const sliding = useRef<string | null>(null);
  const queued = useRef<string | null>(null);
  const writing = useRef(false);
  const pushed = useRef(property.value);
  pushed.current = property.value;

  // The pushed value wins whenever the row is not being edited - another connection may have
  // changed it, and an undo certainly will have. While a slider's writes are still coming back,
  // a push is an echo of an earlier stop and not an answer: taking it would move the thumb back
  // under the pointer, and the next pointer move would write that stop again.
  useEffect(() => {
    if (sliding.current !== null) {
      if (property.value !== sliding.current) {
        return;
      }
      sliding.current = null;
    }
    if (!editing) {
      setDraft(property.value);
    }
  }, [property.value, editing]);

  const editable = property.readOnlyReason.length === 0;

  async function commit() {
    if (committing.current) {
      // A write is already in flight for this row. Until it lands, `property.value` still holds
      // the old value, so a second blur would compare the draft against it, find them different
      // and write again - one edit, two entries on the project history.
      return;
    }

    setEditing(false);
    if (draft === property.value) {
      // Focus left a field nobody changed. Writing anyway would put an entry on the history
      // for having clicked somewhere.
      setError("");
      return;
    }

    committing.current = true;
    try {
      const failure = await onCommit(draft);
      setError(failure);
      if (failure.length > 0) {
        setDraft(property.value);
      }
    } finally {
      // Whatever happened, this row accepts the next edit. `onCommit` is a prop: nothing
      // here can promise it will not reject, and one that does would otherwise latch this
      // guard for the life of the component - every later edit returning early at the top
      // of this function, writing nothing and saying nothing.
      committing.current = false;
    }
  }

  /**
   * Writes a slider's stop, live, without flooding the history: while a write is in flight only
   * the newest stop is kept, and it is written when that one lands. The row then waits for the
   * last stop to be pushed back before it takes a pushed value again.
   */
  function slide(value: string) {
    sliding.current = value;
    if (writing.current) {
      queued.current = value;
      return;
    }

    writing.current = true;
    void onCommit(value)
      .then((failure) => {
        setError(failure);
        if (failure.length > 0) {
          queued.current = null;
          sliding.current = null;
          setDraft(property.value);
        }
      })
      .finally(() => {
        writing.current = false;
        const next = queued.current;
        queued.current = null;
        if (next !== null && next !== value) {
          slide(next);
        } else if (sliding.current !== null && pushed.current === sliding.current) {
          // Already pushed before this write returned: there is no echo left to wait for.
          sliding.current = null;
        }
      });
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

  // An editor this client does not know: show the value, offer no field. The enum is widened
  // by whichever type first needs a new editor, so a client older than the backend meets one
  // eventually - and showing a value it cannot edit properly is right, while editing it
  // through the wrong control is how a value gets mangled by a client that guessed.
  if (!KNOWN_EDITORS.has(property.editor)) {
    return (
      <div className="property-grid-row property-grid-row-readonly">
        <dt>{property.label}</dt>
        <dd>
          <span className="property-grid-value">{property.value}</span>
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
        ) : property.editor === ContextPropertyEditor.CHOICE ? (
          <select
            className="property-grid-input property-grid-input-choice"
            value={draft}
            aria-label={property.label}
            onChange={(event) => {
              // Picking from a list has no "finished typing" either: the selection is the commit.
              const next = event.target.value;
              setDraft(next);
              setEditing(false);
              void onCommit(next).then((failure) => {
                setError(failure);
                if (failure.length > 0) {
                  setDraft(property.value);
                }
              });
            }}
          >
            {/* The current value first, and included even when the provider did not list it -
                a value the file already holds must remain selectable, or opening the list would
                silently offer to change it. */}
            {(property.candidates.includes(property.value)
              ? property.candidates
              : [property.value, ...property.candidates]
            ).map((candidate) => (
              <option key={candidate} value={candidate}>
                {candidate.length > 0 ? candidate : "(none)"}
              </option>
            ))}
          </select>
        ) : property.editor === ContextPropertyEditor.SLIDER ? (
          <span className="property-grid-slider">
            {/* One stop per candidate, in the order the provider gave them. The range's own value
                is an INDEX, which is the input's business and never the backend's: what is
                committed is the candidate at that index, exactly as CHOICE commits one. */}
            <input
              type="range"
              className="property-grid-input property-grid-input-slider"
              min={0}
              max={Math.max(0, property.candidates.length - 1)}
              step={1}
              value={Math.max(0, property.candidates.indexOf(draft))}
              list={`${property.id}-stops`}
              aria-label={property.label}
              aria-valuetext={draft}
              onChange={(event) => {
                const next = property.candidates[Number(event.target.value)];
                if (next === undefined || next === draft) {
                  return;
                }
                setDraft(next);
                setEditing(false);
                slide(next);
              }}
            />
            <datalist id={`${property.id}-stops`}>
              {property.candidates.map((candidate, index) => (
                <option key={candidate} value={index} label={candidate} />
              ))}
            </datalist>
            <span className="property-grid-slider-value">{draft}</span>
          </span>
        ) : property.editor === ContextPropertyEditor.TAGS ? (
          <TagInput
            className="property-grid-input-tags"
            label={property.label}
            tags={tagsOf(draft)}
            suggestions={property.candidates}
            onChange={(tags) => {
              // Adding or removing a chip is a finished edit, as picking from a list is: the
              // change is the commit, one history entry per chip.
              const next = tags.join(", ");
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
