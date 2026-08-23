import { useEffect } from "react";
import { PROJECT_SOURCE, useContextConnection, useContextPrompt, useProjectActions } from "./ContextConnectionProvider";
import { matchShortcut } from "../panels/ExplorerTreePanel";

/** A text-entry surface keeps its own keys - a rename field's Ctrl+Z must edit, not undo. */
function isTextEntry(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  const tag = target.tagName;
  return tag === "INPUT" || tag === "TEXTAREA" || target.isContentEditable;
}

/**
 * The global counterpart of the per-entry shortcut matching the explorer does: a shell-level
 * key listener that runs the project's own actions - undo and redo - from the very same
 * backend-pushed shortcut data, so there is no key-to-action table on the client
 * (diagram-undo-redo Requirement 5.4).
 *
 * Two guards leave the keypress alone: a text field or contenteditable, whose own editing keys
 * must win, and an open modal prompt, which owns the keyboard while it is up. The one mapping
 * the client is allowed is a key-to-key alias: Ctrl+Shift+Z, the widespread redo chord, is
 * normalised to Ctrl+Y before matching (design deviation 2).
 */
export function useProjectShortcuts(): void {
  const { executeAction } = useContextConnection();
  const projectActions = useProjectActions();
  const { prompt } = useContextPrompt();

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (prompt !== null || isTextEntry(event.target)) {
        return;
      }

      const isRedoAlias = event.ctrlKey && event.shiftKey && event.key.toLowerCase() === "z";
      const matchEvent = isRedoAlias
        ? { key: "y", ctrlKey: true, shiftKey: false, altKey: event.altKey, metaKey: event.metaKey }
        : { key: event.key, ctrlKey: event.ctrlKey, shiftKey: event.shiftKey, altKey: event.altKey, metaKey: event.metaKey };

      const action = matchShortcut(projectActions, matchEvent);
      if (!action) {
        // Not one of ours: leave it entirely alone, default behaviour and all.
        return;
      }

      event.preventDefault();
      void executeAction(action.id, PROJECT_SOURCE);
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [executeAction, projectActions, prompt]);
}
