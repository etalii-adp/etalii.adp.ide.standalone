import { useCallback, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";
import { Dialog } from "../../components/Dialog";
import type { ChoiceDialogPrompt, ContextOption } from "../../generated/context_pb";
import { useDebouncedValue } from "../useDebouncedValue";
import type { ContextPromptSubmission, ContextPromptVerdict } from "./ContextPromptHost";

/** How long the name must sit still before its validation round trip is worth making. */
const VALIDATION_DEBOUNCE_MS = 200;

export interface ChoicePromptDialogProps {
  prompt: ChoiceDialogPrompt;
  onPropose: (revision: number, value: string) => Promise<ContextPromptVerdict>;
  onSubmit: (value: string, text?: string) => Promise<ContextPromptSubmission>;
  onCancel: () => void;
}

/** The suggestion a selectable option carries for the text field, if the prompt has one. */
export function suggestionFor(options: ContextOption[], id: string | null): string {
  if (id === null) {
    return "";
  }
  for (const option of options) {
    if (option.id === id) {
      return option.suggestedValue;
    }
    const nested = suggestionFor(option.children, id);
    if (nested !== "") {
      return nested;
    }
  }
  return "";
}

/** The first selectable option, depth first - what the text field starts out describing. */
function firstSelectable(options: ContextOption[]): ContextOption | undefined {
  for (const option of options) {
    if (option.selectable) {
      return option;
    }
    const nested = firstSelectable(option.children);
    if (nested !== undefined) {
      return nested;
    }
  }
  return undefined;
}

/** One rendered row of the tree: which option, how deep, and whether it is open. */
interface VisibleRow {
  option: ContextOption;
  depth: number;
  parentId: string | null;
  expandable: boolean;
  expanded: boolean;
}

/**
 * Which groups start open. With one top-level group there is nothing to choose between, so
 * it opens; with several, they stay closed so the vendors read as a list first.
 */
function initialExpansion(options: ContextOption[]): Set<string> {
  return options.length === 1 && options[0].children.length > 0 ? new Set([options[0].id]) : new Set();
}

/**
 * The rows currently on screen, top to bottom - a closed group's children are not among
 * them. This is the order the arrow keys walk, so what the keyboard moves through is
 * exactly what the eye sees. Exported for unit testing.
 */
export function visibleRows(options: ContextOption[], expanded: Set<string>): VisibleRow[] {
  const rows: VisibleRow[] = [];
  const walk = (nodes: ContextOption[], depth: number, parentId: string | null) => {
    for (const option of nodes) {
      const expandable = option.children.length > 0;
      const isExpanded = expandable && expanded.has(option.id);
      rows.push({ option, depth, parentId, expandable, expanded: isExpanded });
      if (isExpanded) {
        walk(option.children, depth + 1, option.id);
      }
    }
  };
  walk(options, 0, null);
  return rows;
}

/**
 * A dialog for picking one option out of a tree. It renders whatever tree the prompt
 * carries and knows nothing about what the options represent - today diagram types,
 * later anything else that is a grouped list of labelled choices.
 *
 * Groups (non-selectable nodes) expand and collapse; only a selectable node can be the
 * answer, so the confirm button stays disabled until one is chosen. A failed submission
 * keeps the dialog open with the error shown under the tree, so the user can pick again
 * or cancel rather than losing their place.
 */
export function ChoicePromptDialog({ prompt, onPropose, onSubmit, onCancel }: ChoicePromptDialogProps) {
  const nameField = prompt.nameField;
  const nameInputId = useId();
  const [expanded, setExpanded] = useState<Set<string>>(() => initialExpansion(prompt.options));
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [focusedId, setFocusedId] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const rowRefs = useRef(new Map<string, HTMLElement>());

  // Revision 0 is the name the backend suggested: it computed that against the target folder,
  // so it is free by construction and needs no round trip - which is what lets a user who
  // just wants "one of these, here" press the confirm button without typing anything. Every
  // edit bumps the revision and is judged; a verdict for an older revision is stale.
  const [name, setName] = useState(() => nameField?.initialValue || firstSelectable(prompt.options)?.suggestedValue || "");
  const [nameRevision, setNameRevision] = useState(0);
  const [touched, setTouched] = useState(false);
  const [verdict, setVerdict] = useState<ContextPromptVerdict | null>(null);

  const debounced = useDebouncedValue({ revision: nameRevision, value: name }, VALIDATION_DEBOUNCE_MS);
  useEffect(() => {
    if (debounced.revision === 0) {
      return;
    }
    void onPropose(debounced.revision, debounced.value).then(setVerdict);
  }, [debounced, onPropose]);

  const verdictIsCurrent = verdict !== null && verdict.revision === nameRevision;
  const nameIsAcceptable =
    nameField === undefined ? true : nameRevision === 0 ? name.length > 0 : verdictIsCurrent && verdict.valid;
  const nameError = nameRevision > 0 && verdictIsCurrent && !verdict.valid ? verdict.reason : "";

  const rows = useMemo(() => visibleRows(prompt.options, expanded), [prompt.options, expanded]);
  const isEmpty = prompt.options.length === 0;
  // One Tab stop: the focused row, or failing that the first one.
  const tabbableId = focusedId ?? rows[0]?.option.id ?? null;
  const canSubmit = selectedId !== null && !submitting && nameIsAcceptable;

  const focusRow = useCallback((id: string | undefined) => {
    if (id === undefined) {
      return;
    }
    setFocusedId(id);
    rowRefs.current.get(id)?.focus();
  }, []);

  /** Choosing an option names the file too - until the user has named it themselves. */
  const choose = useCallback(
    (id: string) => {
      setSelectedId(id);
      if (nameField !== undefined && !touched) {
        setName(suggestionFor(prompt.options, id));
        setNameRevision(0);
        setVerdict(null);
      }
    },
    [nameField, prompt.options, touched],
  );

  const toggle = useCallback((id: string) => {
    setExpanded((previous) => {
      const next = new Set(previous);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }, []);

  const handleSubmit = useCallback(async () => {
    if (selectedId === null) {
      return;
    }
    setSubmitting(true);
    setSubmitError("");
    try {
      // A prompt without a name field submits exactly as it always did: one answer, no second.
      const result = nameField === undefined ? await onSubmit(selectedId) : await onSubmit(selectedId, name);
      if (!result.completed) {
        // Left open on purpose: the choice is still there to reconsider or cancel.
        setSubmitError(result.error);
      }
    } finally {
      setSubmitting(false);
    }
  }, [name, nameField, onSubmit, selectedId]);

  const handleKeyDown = useCallback(
    (event: ReactKeyboardEvent<HTMLUListElement>) => {
      // Which row the key applies to is read from the DOM, not from focusedId state: a key
      // can arrive in the same tick as the focus that moved there (scripted input does this,
      // a fast hand can too), before React has committed the onFocus state update - and the
      // stale closure would then act on the previously focused row. The element with focus
      // is the truth; the state only drives which row is tabbable.
      const focusedElement = document.activeElement;
      const currentId =
        [...rowRefs.current.entries()].find(([, element]) => element === focusedElement)?.[0] ?? focusedId;
      const index = rows.findIndex((row) => row.option.id === currentId);
      const row = index >= 0 ? rows[index] : undefined;

      switch (event.key) {
        case "ArrowDown":
          event.preventDefault();
          focusRow(rows[Math.min(index + 1, rows.length - 1)]?.option.id);
          return;

        case "ArrowUp":
          event.preventDefault();
          focusRow(rows[Math.max(index - 1, 0)]?.option.id);
          return;

        case "ArrowRight":
          if (!row) {
            return;
          }
          event.preventDefault();
          if (row.expandable && !row.expanded) {
            toggle(row.option.id);
          } else if (row.expandable) {
            focusRow(row.option.children[0]?.id);
          }
          // A leaf: nothing to open, nothing to move into.
          return;

        case "ArrowLeft":
          if (!row) {
            return;
          }
          event.preventDefault();
          if (row.expandable && row.expanded) {
            toggle(row.option.id);
          } else if (row.parentId !== null) {
            focusRow(row.parentId);
          }
          return;

        case "Enter":
          if (!row) {
            return;
          }
          event.preventDefault();
          if (row.option.selectable) {
            // Enter on the chosen leaf confirms; on another leaf it chooses it first.
            if (row.option.id === selectedId && canSubmit) {
              void handleSubmit();
            } else {
              choose(row.option.id);
            }
          } else if (row.expandable) {
            toggle(row.option.id);
          }
          return;

        case " ":
          if (!row) {
            return;
          }
          event.preventDefault();
          if (row.option.selectable) {
            choose(row.option.id);
          } else if (row.expandable) {
            toggle(row.option.id);
          }
          return;

        default:
          return;
      }
    },
    [canSubmit, choose, focusRow, focusedId, handleSubmit, rows, selectedId, toggle],
  );

  return (
    <Dialog
      open
      icon={prompt.icon}
      title={prompt.title}
      onClose={onCancel}
      buttons={[
        { key: "cancel", label: "Cancel", color: "neutral", onClick: onCancel },
        { key: "confirm", label: prompt.confirmLabel, color: "primary", disabled: !canSubmit, onClick: () => void handleSubmit() },
      ]}
    >
      {isEmpty ? (
        <p className="choice-tree-empty">{prompt.emptyMessage}</p>
      ) : (
        <ul className="choice-tree" role="tree" aria-label={prompt.title} onKeyDown={handleKeyDown}>
          {rows.map((row) => (
            <ChoiceRow
              key={row.option.id}
              row={row}
              selected={row.option.id === selectedId}
              tabbable={row.option.id === tabbableId}
              rowRefs={rowRefs.current}
              onFocus={() => setFocusedId(row.option.id)}
              onToggle={() => toggle(row.option.id)}
              onChoose={() => choose(row.option.id)}
              onConfirm={() => {
                // A double click on a leaf chooses it and confirms in one go - with whatever
                // name is in the field, which is the suggestion for this option unless the
                // user has typed one.
                choose(row.option.id);
                const submittedName = touched ? name : suggestionFor(prompt.options, row.option.id);
                if (!submitting && (nameField === undefined || submittedName.length > 0)) {
                  const submission =
                    nameField === undefined ? onSubmit(row.option.id) : onSubmit(row.option.id, submittedName);
                  void submission.then((result) => {
                    if (!result.completed) {
                      setSubmitError(result.error);
                    }
                  });
                }
              }}
            />
          ))}
        </ul>
      )}
      {!isEmpty && nameField !== undefined && (
        <div className="field choice-name-field">
          <label htmlFor={nameInputId}>{nameField.label}</label>
          <input
            id={nameInputId}
            type="text"
            value={name}
            onChange={(event) => {
              setTouched(true);
              setName(event.target.value);
              setNameRevision((previous) => previous + 1);
            }}
            onKeyDown={(event) => {
              // Accepting the suggestion should not require reaching for the mouse.
              if (event.key === "Enter" && canSubmit) {
                event.preventDefault();
                void handleSubmit();
              }
            }}
          />
          {nameError && (
            <p className="context-prompt-error" role="alert">
              {nameError}
            </p>
          )}
        </div>
      )}
      {submitError && (
        <p className="context-prompt-error" role="alert">
          {submitError}
        </p>
      )}
    </Dialog>
  );
}

interface ChoiceRowProps {
  row: VisibleRow;
  selected: boolean;
  tabbable: boolean;
  rowRefs: Map<string, HTMLElement>;
  onFocus: () => void;
  onToggle: () => void;
  onChoose: () => void;
  onConfirm: () => void;
}

function ChoiceRow({ row, selected, tabbable, rowRefs, onFocus, onToggle, onChoose, onConfirm }: ChoiceRowProps) {
  const { option, depth, expandable, expanded } = row;
  const isGroup = !option.selectable;

  // The whole tree is flat in the DOM - rows carry aria-level - so the keyboard order and
  // the visual order are one list, as in the explorer.
  return (
    <li
      role="treeitem"
      aria-level={depth + 1}
      aria-expanded={expandable ? expanded : undefined}
      aria-selected={option.selectable ? selected : undefined}
      className={`choice-tree-row${isGroup ? " choice-tree-row-group" : ""}${selected ? " choice-tree-row-selected" : ""}`}
      style={{ paddingLeft: `${depth * 16 + 8}px` }}
      onDoubleClick={() => {
        if (option.selectable) {
          onConfirm();
        } else if (expandable) {
          onToggle();
        }
      }}
    >
      {expandable ? (
        <button
          type="button"
          className="choice-tree-chevron-button"
          aria-label={`${expanded ? "Collapse" : "Expand"} ${option.label}`}
          tabIndex={-1}
          onClick={(event) => {
            event.stopPropagation();
            if (event.detail > 1) {
              return;
            }
            onToggle();
          }}
          onDoubleClick={(event) => event.stopPropagation()}
        >
          <span className={`mdi choice-tree-chevron ${expanded ? "mdi-chevron-down" : "mdi-chevron-right"}`} aria-hidden="true" />
        </button>
      ) : (
        <span className="mdi choice-tree-chevron" aria-hidden="true" />
      )}
      <button
        type="button"
        ref={(element) => {
          if (element) {
            rowRefs.set(option.id, element);
          } else {
            rowRefs.delete(option.id);
          }
        }}
        className="choice-tree-label"
        tabIndex={tabbable ? 0 : -1}
        onFocus={onFocus}
        onClick={() => {
          if (option.selectable) {
            onChoose();
          } else if (expandable) {
            onToggle();
          }
        }}
      >
        <span className="choice-tree-label-text">{option.label}</span>
      </button>
    </li>
  );
}
