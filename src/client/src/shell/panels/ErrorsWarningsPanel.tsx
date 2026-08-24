import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type FocusEvent as ReactFocusEvent,
  type KeyboardEvent as ReactKeyboardEvent,
  type MouseEvent as ReactMouseEvent,
} from "react";
import { create } from "@bufbuild/protobuf";
import { ContextSelectionAction, ContextSelectionSchema, ContextSelectionSource, ProblemSetState, ProblemSeverity } from "../../generated/context_pb";
import type { ContextSelection, Problem } from "../../generated/context_pb";
import { ContextMenu } from "../context/ContextMenu";
import { toMenuGroups } from "../context/toMenuGroups";
import { matchShortcut } from "./ExplorerTreePanel";
import {
  NONE_DETAIL,
  PROBLEMS_SOURCE,
  innermostKey,
  useContextConnection,
  useContextProblems,
  useContextSelection,
} from "../context/ContextConnectionProvider";

/** The rows the current severity filter leaves visible. Exported for unit testing. */
export function visibleProblems(problems: Problem[], showErrors: boolean, showWarnings: boolean): Problem[] {
  return problems.filter((problem) =>
    problem.severity === ProblemSeverity.ERROR ? showErrors : showWarnings,
  );
}

/** "folder/flow.adp" - the project-relative path as the panel prints it. */
function pathText(problem: Problem): string {
  return problem.path?.segments.join("/") ?? "";
}

/** ":12" for a line, "" for an element or the file itself - an element shows through the reveal, not the text. */
function locationText(problem: Problem): string {
  return problem.location?.location.case === "line" ? `:${problem.location.location.value}` : "";
}

/** A row's identity within one pushed list - stable enough for React keys and focus. */
function rowKeyOf(problem: Problem, index: number): string {
  return `${index}:${problem.ruleId}:${pathText(problem)}`;
}

/** The panel selecting itself: what routes Validate all into the ribbon (Requirement 7.4). */
function panelSelection(detail: ContextSelection["detail"]): ContextSelection {
  return create(ContextSelectionSchema, {
    source: ContextSelectionSource.PROBLEMS,
    path: { segments: [] },
    id: PROBLEMS_SOURCE,
    detail,
  });
}

/**
 * The errors-and-warnings panel: one list per project, straight from the backend's pushed
 * ProjectProblems - the panel filters what it shows but never recounts, re-sorts or
 * re-derives anything (design Deviation 3). On focus it selects itself, which is what puts
 * Validate all in the ribbon; its menu and shortcuts render pushed actions it does not
 * understand.
 */
export function ErrorsWarningsPanel() {
  const problems = useContextProblems();
  const { select, executeAction, revealPath } = useContextConnection();
  const { selection, actions } = useContextSelection();

  const [showErrors, setShowErrors] = useState(true);
  const [showWarnings, setShowWarnings] = useState(true);
  const [focusedKey, setFocusedKey] = useState<string | undefined>();
  const [menuPosition, setMenuPosition] = useState<{ x: number; y: number } | null>(null);
  const menuPendingRef = useRef<{ x: number; y: number } | null>(null);
  const listRef = useRef<HTMLUListElement>(null);
  const rowRefs = useRef(new Map<string, HTMLLIElement>());

  const selectionKey = innermostKey(selection);
  // The pushed actions answer for this panel only while the backend agrees the panel is
  // what is selected - a stale list never fires (the explorer's own rule).
  const panelActions = selectionKey === "problems" ? actions : [];

  const list = problems?.problems ?? [];
  const rows = visibleProblems(list, showErrors, showWarnings);
  const state = problems?.state ?? ProblemSetState.NEVER_VALIDATED;

  /** Focus arriving from outside the panel is the moment it becomes the selection. */
  const handleFocus = useCallback(
    (event: ReactFocusEvent<HTMLElement>) => {
      const from = event.relatedTarget;
      if (from instanceof Node && event.currentTarget.contains(from)) {
        return; // Focus moved within the panel; it is already the selection.
      }
      select(panelSelection(NONE_DETAIL));
    },
    [select],
  );

  const runAction = useCallback(
    (actionId: string) => {
      void executeAction(actionId, PROBLEMS_SOURCE);
    },
    [executeAction],
  );

  /**
   * Right-click selects the panel with CONTEXT_MENU, which makes the backend push its
   * actions. If they are already here the menu opens at once; otherwise it opens when
   * that push arrives (the explorer's own pattern).
   */
  const openMenu = useCallback(
    (position: { x: number; y: number }) => {
      select(panelSelection({ case: "action", value: ContextSelectionAction.CONTEXT_MENU }));
      if (selectionKey === "problems" && actions.length > 0) {
        menuPendingRef.current = null;
        setMenuPosition(position);
      } else {
        menuPendingRef.current = position;
      }
    },
    [actions.length, select, selectionKey],
  );

  useEffect(() => {
    const pending = menuPendingRef.current;
    if (pending && selectionKey === "problems" && actions.length > 0) {
      menuPendingRef.current = null;
      setMenuPosition(pending);
    }
  }, [actions.length, selectionKey]);

  const handleContextMenu = useCallback(
    (event: ReactMouseEvent) => {
      event.preventDefault();
      openMenu({ x: event.clientX, y: event.clientY });
    },
    [openMenu],
  );

  /** Activating a problem reveals its file in the hierarchy (Requirement 7.7). */
  const activate = useCallback(
    (problem: Problem) => {
      const segments = problem.path?.segments ?? [];
      if (segments.length > 0) {
        revealPath([...segments]);
      }
    },
    [revealPath],
  );

  const focusRow = useCallback((key: string | undefined) => {
    if (key === undefined) {
      return;
    }
    setFocusedKey(key);
    rowRefs.current.get(key)?.focus();
  }, []);

  const handleKeyDown = useCallback(
    (event: ReactKeyboardEvent<HTMLUListElement>) => {
      // Which row the key applies to is read from the DOM, not from focusedKey state: a key
      // can arrive in the same tick as the focus that moved there (the choice dialog's race).
      const focusedElement = document.activeElement;
      const currentKey = [...rowRefs.current.entries()].find(([, element]) => element === focusedElement)?.[0] ?? focusedKey;
      const keys = rows.map(rowKeyOf);
      const index = currentKey !== undefined ? keys.indexOf(currentKey) : -1;

      switch (event.key) {
        case "ArrowDown":
        case "ArrowUp": {
          event.preventDefault();
          const next = index < 0 ? (event.key === "ArrowDown" ? keys[0] : keys[keys.length - 1]) : keys[index + (event.key === "ArrowDown" ? 1 : -1)];
          focusRow(next);
          return;
        }

        case "Enter": {
          const problem = index >= 0 ? rows[index] : undefined;
          if (!problem) {
            return;
          }
          event.preventDefault();
          activate(problem);
          return;
        }

        case "ContextMenu": {
          event.preventDefault();
          const rect = (currentKey !== undefined ? rowRefs.current.get(currentKey) : listRef.current)?.getBoundingClientRect();
          openMenu({ x: rect?.left ?? 0, y: rect?.bottom ?? 0 });
          return;
        }

        case "F10": {
          if (!event.shiftKey) {
            return;
          }
          event.preventDefault();
          const rect = (currentKey !== undefined ? rowRefs.current.get(currentKey) : listRef.current)?.getBoundingClientRect();
          openMenu({ x: rect?.left ?? 0, y: rect?.bottom ?? 0 });
          return;
        }

        default:
          break;
      }

      // Anything else is only a shortcut if the backend said so for this panel - the client
      // holds no key-to-action mapping of its own, here or anywhere.
      const match = matchShortcut(panelActions, event);
      if (!match) {
        return;
      }
      event.preventDefault();
      runAction(match.id);
    },
    [activate, focusRow, focusedKey, openMenu, panelActions, rows, runAction],
  );

  // Roving tabindex: exactly one row is tabbable, so Tab enters the list once and the
  // arrows take over from there.
  const keys = rows.map(rowKeyOf);
  const tabbableKey = focusedKey !== undefined && keys.includes(focusedKey) ? focusedKey : keys[0];

  const truncated = problems !== undefined && problems !== null && problems.truncatedAt > 0
    ? Number(problems.errorCount) + Number(problems.warningCount) - list.length
    : 0;

  return (
    <div className="problems-panel" onFocus={handleFocus} onContextMenu={handleContextMenu}>
      <div className="problems-header">
        <button
          type="button"
          className={`problems-filter${showErrors ? " problems-filter-on" : ""}`}
          aria-pressed={showErrors}
          title={showErrors ? "Hide errors" : "Show errors"}
          onClick={() => setShowErrors((previous) => !previous)}
        >
          <span className="mdi mdi-close-circle-outline problems-icon-error" aria-hidden="true" />
          {/* The whole set's count, whatever the filter shows (Requirement 1.8). */}
          <span className="problems-count">{Number(problems?.errorCount ?? 0)}</span>
        </button>
        <button
          type="button"
          className={`problems-filter${showWarnings ? " problems-filter-on" : ""}`}
          aria-pressed={showWarnings}
          title={showWarnings ? "Hide warnings" : "Show warnings"}
          onClick={() => setShowWarnings((previous) => !previous)}
        >
          <span className="mdi mdi-alert-outline problems-icon-warning" aria-hidden="true" />
          <span className="problems-count">{Number(problems?.warningCount ?? 0)}</span>
        </button>
        {state === ProblemSetState.VALIDATING && (
          <span className="problems-validating">
            <span className="mdi mdi-progress-clock" aria-hidden="true" /> Checking…
          </span>
        )}
      </div>

      {rows.length === 0 ? (
        <p className="problems-empty">
          {state === ProblemSetState.VALIDATED
            ? list.length === 0
              ? "No problems found."
              : "Nothing to show with these filters."
            : state === ProblemSetState.VALIDATING
              ? "Checking…"
              : "Not checked yet."}
        </p>
      ) : (
        <ul ref={listRef} className="problems-list" role="listbox" aria-label="Problems" onKeyDown={handleKeyDown}>
          {rows.map((problem, index) => {
            const rowKey = rowKeyOf(problem, index);
            const isError = problem.severity === ProblemSeverity.ERROR;
            return (
              <li
                key={rowKey}
                ref={(element) => {
                  if (element) {
                    rowRefs.current.set(rowKey, element);
                  } else {
                    rowRefs.current.delete(rowKey);
                  }
                }}
                role="option"
                aria-selected={rowKey === focusedKey || undefined}
                className={`problems-row${rowKey === focusedKey ? " problems-row-focused" : ""}${problem.stale ? " problems-row-stale" : ""}`}
                tabIndex={rowKey === tabbableKey ? 0 : -1}
                onFocus={() => setFocusedKey(rowKey)}
                onDoubleClick={() => activate(problem)}
              >
                <span
                  className={`mdi ${isError ? "mdi-close-circle-outline problems-icon-error" : "mdi-alert-outline problems-icon-warning"}`}
                  aria-hidden="true"
                />
                <span className="problems-message">{problem.message}</span>
                <span className="problems-path">
                  {pathText(problem)}
                  {locationText(problem)}
                </span>
                {problem.stale && (
                  <span className="problems-stale" title="The file changed since this was found.">
                    stale
                  </span>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {truncated > 0 && (
        <p className="problems-truncated">
          {truncated} more {truncated === 1 ? "problem is" : "problems are"} not shown.
        </p>
      )}

      <ContextMenu
        open={menuPosition !== null}
        groups={menuPosition ? toMenuGroups(panelActions, (action) => runAction(action.id)) : []}
        position={menuPosition ?? { x: 0, y: 0 }}
        onClose={() => setMenuPosition(null)}
      />
    </div>
  );
}
