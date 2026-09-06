import { useRef } from "react";
import type { ContextAction, ContextActionGroup, ContextShortcut } from "../../generated/context-contract_pb";
import { innermostKey, useContextConnection, useContextSelection } from "../context/ContextConnectionProvider";
import { RibbonDropdownButton } from "./RibbonDropdownButton";

/** "Ctrl+Shift+X" - the shortcut as a tooltip reads it. */
export function formatShortcut(shortcut: ContextShortcut): string {
  const parts: string[] = [];
  if (shortcut.ctrl) {
    parts.push("Ctrl");
  }
  if (shortcut.alt) {
    parts.push("Alt");
  }
  if (shortcut.shift) {
    parts.push("Shift");
  }
  if (shortcut.meta) {
    parts.push("Meta");
  }
  parts.push(shortcut.key.length === 1 ? shortcut.key.toUpperCase() : shortcut.key);
  return parts.join("+");
}

/** The tooltip: the reason when unavailable, else the label with its shortcut. */
export function tooltipFor(action: ContextAction): string {
  if (!action.available && action.unavailableReason) {
    return action.unavailableReason;
  }
  return action.shortcut ? `${action.label} (${formatShortcut(action.shortcut)})` : action.label;
}

/**
 * The ribbon's contextual part: one group per pushed action group of the current
 * selection, after the static groups. A pure subscriber - it holds no selection of its
 * own and makes no call but ExecuteAction, exactly like the right-click menu.
 */
export function RibbonContextualGroups() {
  const { executeAction } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const selectionKey = innermostKey(selection);

  // A contextual button, once shown, stays where it is: between a selection and its actions
  // arriving, and equally when the selection goes away entirely, the buttons remain in place
  // and go disabled rather than disappearing. A ribbon that changes shape under the pointer
  // is worse than one holding greyed-out buttons that say what would apply.
  const lastShownRef = useRef<{ key: string | undefined; groups: ContextActionGroup[] }>({ key: undefined, groups: [] });
  const current = selectionKey !== undefined && (actions.length > 0 || lastShownRef.current.key === selectionKey);
  if (current) {
    lastShownRef.current = { key: selectionKey, groups: actions };
  }

  const groups = current ? actions : lastShownRef.current.groups;
  if (groups.length === 0) {
    // Nothing has ever been shown here, so there is nothing to keep: the ribbon is simply
    // its static self until the first selection arrives.
    return null;
  }

  const run = (action: ContextAction) => {
    void executeAction(action.id);
  };

  return (
    <>
      {groups.map((group, groupIndex) => (
        <div className="ribbon-group ribbon-group-contextual" key={groupIndex} aria-busy={!current || undefined}>
          {group.actions.map((action) =>
            action.items.length > 0 ? (
              <RibbonDropdownButton
                key={action.id}
                action={action}
                title={tooltipFor(action)}
                selectionKey={selectionKey}
                // Held over from a selection that is gone or not answered yet: its children
                // would be about something no longer selected, so it does not open either.
                stale={!current}
                onSelect={run}
              />
            ) : (
              <button
                key={action.id}
                type="button"
                className="ribbon-button"
                title={tooltipFor(action)}
                disabled={!current || !action.available}
                onClick={() => run(action)}
              >
                <span className={`mdi ${action.icon}`} aria-hidden="true" />
                <span className="ribbon-button-label">{action.label}</span>
              </button>
            ),
          )}
        </div>
      ))}
    </>
  );
}
