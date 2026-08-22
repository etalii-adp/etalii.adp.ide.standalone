import { useCallback, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";
import { Dialog } from "../../components/Dialog";
import type { ChoiceDialogPrompt, ContextOption } from "../../generated/context_pb";
import type { ContextPromptSubmission } from "./ContextPromptHost";

export interface ChoicePromptDialogProps {
  prompt: ChoiceDialogPrompt;
  onSubmit: (value: string) => Promise<ContextPromptSubmission>;
  onCancel: () => void;
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
export function ChoicePromptDialog({ prompt, onSubmit, onCancel }: ChoicePromptDialogProps) {
  const [expanded, setExpanded] = useState<Set<string>>(() => initialExpansion(prompt.options));
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [focusedId, setFocusedId] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const rowRefs = useRef(new Map<string, HTMLElement>());

  const rows = useMemo(() => visibleRows(prompt.options, expanded), [prompt.options, expanded]);
  const isEmpty = prompt.options.length === 0;
  // One Tab stop: the focused row, or failing that the first one.
  const tabbableId = focusedId ?? rows[0]?.option.id ?? null;
  const canSubmit = selectedId !== null && !submitting;

  const focusRow = useCallback((id: string | undefined) => {
    if (id === undefined) {
      return;
    }
    setFocusedId(id);
    rowRefs.current.get(id)?.focus();
  }, []);

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
      const result = await onSubmit(selectedId);
      if (!result.completed) {
        // Left open on purpose: the choice is still there to reconsider or cancel.
        setSubmitError(result.error);
      }
    } finally {
      setSubmitting(false);
    }
  }, [onSubmit, selectedId]);

  const handleKeyDown = useCallback(
    (event: ReactKeyboardEvent<HTMLUListElement>) => {
      const index = rows.findIndex((row) => row.option.id === focusedId);
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
              setSelectedId(row.option.id);
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
            setSelectedId(row.option.id);
          } else if (row.expandable) {
            toggle(row.option.id);
          }
          return;

        default:
          return;
      }
    },
    [canSubmit, focusRow, focusedId, handleSubmit, rows, selectedId, toggle],
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
              onChoose={() => setSelectedId(row.option.id)}
              onConfirm={() => {
                // A double click on a leaf chooses it and confirms in one go.
                setSelectedId(row.option.id);
                if (!submitting) {
                  void onSubmit(row.option.id).then((result) => {
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
