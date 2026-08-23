import { useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";
import type { ContextAction } from "../../generated/context_pb";
import { ContextMenu } from "../context/ContextMenu";
import { toMenuGroups } from "../context/toMenuGroups";

export interface RibbonDropdownButtonProps {
  action: ContextAction;
  title: string;
  /** Changes when the selection does; an open drop-down closes then, since its items may no longer apply. */
  selectionKey: string | undefined;
  /** Held over from a selection that is gone or not answered yet: shown, but inert. */
  stale?: boolean;
  onSelect: (action: ContextAction) => void;
}

/**
 * A ribbon button for an action that has children: the parent's icon and label with a
 * chevron, opening the children as the very same ContextMenu the right-click menu uses -
 * anchored beneath the button instead of at a pointer - so nesting, separators, disabled
 * items and keyboard navigation behave identically in both places.
 */
export function RibbonDropdownButton({ action, title, selectionKey, stale = false, onSelect }: RibbonDropdownButtonProps) {
  const [open, setOpen] = useState(false);
  const buttonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    setOpen(false);
  }, [selectionKey]);

  const handleKeyDown = (event: ReactKeyboardEvent<HTMLButtonElement>) => {
    if (stale) {
      return;
    }
    if (event.key === "ArrowDown" || event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      setOpen(true);
    }
  };

  const rect = buttonRef.current?.getBoundingClientRect();

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        className="ribbon-button ribbon-button-dropdown"
        title={title}
        // A disabled parent may still open its children (reusable-context-menu Requirement 4.3),
        // so the disabled look is a class here rather than the attribute.
        aria-disabled={stale || !action.available || undefined}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen(!stale)}
        onKeyDown={handleKeyDown}
      >
        <span className={`mdi ${action.icon}`} aria-hidden="true" />
        <span className="ribbon-button-label">
          {action.label}
          <span className="mdi mdi-chevron-down ribbon-chevron" aria-hidden="true" />
        </span>
      </button>
      <ContextMenu
        open={open}
        groups={open ? toMenuGroups(action.items, onSelect) : []}
        position={{ x: rect?.left ?? 0, y: rect?.bottom ?? 0 }}
        onClose={() => setOpen(false)}
      />
    </>
  );
}
