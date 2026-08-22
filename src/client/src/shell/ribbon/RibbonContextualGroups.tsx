import { useRef } from "react";
import type { ContextAction, ContextActionGroup, ContextShortcut } from "../../generated/context_pb";
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

  // Between a selection and its actions arriving, keep showing what was there - disabled -
  // rather than flashing empty and refilling on every arrow-key step.
  const lastShownRef = useRef<{ key: string | undefined; groups: ContextActionGroup[] }>({ key: undefined, groups: [] });
  if (selectionKey === undefined) {
    lastShownRef.current = { key: undefined, groups: [] };
    return null;
  }
  const current = actions.length > 0 || lastShownRef.current.key === selectionKey;
  if (current) {
    lastShownRef.current = { key: selectionKey, groups: actions };
  }
  const groups = current ? actions : lastShownRef.current.groups;
  if (groups.length === 0) {
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
              <RibbonDropdownButton key={action.id} action={action} title={tooltipFor(action)} selectionKey={selectionKey} onSelect={run} />
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
