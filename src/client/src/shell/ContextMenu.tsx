import {
  Fragment,
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
} from "react";

interface ContextMenuItemBase {
  id: string;
  label: string;
  icon?: string;
  disabled?: boolean;
  disabledReason?: string;
}

export interface ContextMenuActionItem extends ContextMenuItemBase {
  onSelect: () => void;
  items?: undefined;
}

export interface ContextMenuSubmenuItem extends ContextMenuItemBase {
  items: ContextMenuGroup[];
  onSelect?: undefined;
}

export type ContextMenuItem = ContextMenuActionItem | ContextMenuSubmenuItem;
export type ContextMenuGroup = ContextMenuItem[];

export interface ContextMenuProps {
  open: boolean;
  groups: ContextMenuGroup[];
  position: { x: number; y: number };
  onClose: () => void;
}

function clampAxis(preferred: number, size: number, viewportSize: number): number {
  const value = preferred + size > viewportSize ? preferred - size : preferred;
  return Math.max(0, value);
}

/** Flips a preferred corner to the opposite edge when it would overflow that axis, then floors at 0. */
export function computeClampedPosition(
  preferred: { x: number; y: number },
  size: { width: number; height: number },
  viewport: { width: number; height: number },
): { x: number; y: number } {
  return {
    x: clampAxis(preferred.x, size.width, viewport.width),
    y: clampAxis(preferred.y, size.height, viewport.height),
  };
}

type Anchor = { point: { x: number; y: number } } | { rect: DOMRect };

function preferredPositionFor(anchor: Anchor): { x: number; y: number } {
  return "point" in anchor ? anchor.point : { x: anchor.rect.right, y: anchor.rect.top };
}

function hasSubmenu(item: ContextMenuItem): item is ContextMenuSubmenuItem {
  return item.items !== undefined;
}

function nonEmptyGroups(groups: ContextMenuGroup[]): ContextMenuGroup[] {
  return groups.filter((group) => group.length > 0);
}

function enabledItemIds(groups: ContextMenuGroup[]): string[] {
  return groups.flat().filter((item) => !item.disabled).map((item) => item.id);
}

function findItem(groups: ContextMenuGroup[], id: string): ContextMenuItem | undefined {
  return groups.flat().find((item) => item.id === id);
}

interface ContextMenuLevelProps {
  groups: ContextMenuGroup[];
  anchor: Anchor;
  onSelectItem: (item: ContextMenuActionItem) => void;
  onRequestCloseSubmenu?: () => void;
}

/**
 * Renders one menu level - the root menu or any submenu, recursing on itself for a nested
 * submenu (design.md's Modular Design principle: one code path for every nesting depth).
 */
function ContextMenuLevel({ groups, anchor, onSelectItem, onRequestCloseSubmenu }: ContextMenuLevelProps) {
  const groupsToRender = nonEmptyGroups(groups);
  const orderedIds = enabledItemIds(groupsToRender);

  const listRef = useRef<HTMLUListElement>(null);
  const itemRefs = useRef(new Map<string, HTMLButtonElement>());
  const [position, setPosition] = useState(() => preferredPositionFor(anchor));
  const [focusedId, setFocusedId] = useState<string | undefined>(orderedIds[0]);
  const [openSubmenuId, setOpenSubmenuId] = useState<string | undefined>();

  useLayoutEffect(() => {
    const element = listRef.current;
    if (!element) {
      return;
    }
    const rect = element.getBoundingClientRect();
    setPosition(
      computeClampedPosition(
        preferredPositionFor(anchor),
        { width: rect.width, height: rect.height },
        { width: window.innerWidth, height: window.innerHeight },
      ),
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [anchor, groups]);

  useEffect(() => {
    const id = focusedId ?? orderedIds[0];
    itemRefs.current.get(id ?? "")?.focus();
    // Runs once on mount only, to move focus into a freshly opened level
    // (Requirement 6.1 at the root, Requirement 6.3 for a freshly opened submenu).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (openSubmenuId && !findItem(groupsToRender, openSubmenuId)) {
      setOpenSubmenuId(undefined);
    }
    if (focusedId && !orderedIds.includes(focusedId)) {
      setFocusedId(orderedIds[0]);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [groups]);

  const moveFocus = useCallback(
    (direction: 1 | -1) => {
      if (orderedIds.length === 0) {
        return;
      }
      const currentIndex = focusedId ? orderedIds.indexOf(focusedId) : -1;
      const nextId = orderedIds[(currentIndex + direction + orderedIds.length) % orderedIds.length];
      setFocusedId(nextId);
      itemRefs.current.get(nextId)?.focus();
    },
    [orderedIds, focusedId],
  );

  const openSubmenuFor = useCallback((item: ContextMenuSubmenuItem) => {
    setOpenSubmenuId(item.id);
  }, []);

  const closeSubmenu = useCallback(
    (options?: { returnFocus?: boolean }) => {
      setOpenSubmenuId(undefined);
      if (options?.returnFocus && focusedId) {
        itemRefs.current.get(focusedId)?.focus();
      }
    },
    [focusedId],
  );

  const activate = useCallback(
    (item: ContextMenuItem) => {
      if (hasSubmenu(item)) {
        // Opening a submenu is independent of the item's own enabled state (Requirement 4.3).
        openSubmenuFor(item);
        return;
      }
      if (item.disabled) {
        return;
      }
      onSelectItem(item);
    },
    [onSelectItem, openSubmenuFor],
  );

  const handleKeyDown = useCallback(
    (event: ReactKeyboardEvent<HTMLUListElement>) => {
      if (!["ArrowDown", "ArrowUp", "ArrowRight", "ArrowLeft", "Enter", " "].includes(event.key)) {
        return;
      }
      // This event bubbles up through every ancestor level's own onKeyDown (a submenu is
      // nested inside its parent's <li>). Stop it here, in the innermost level - the one
      // that actually contains the focused element - so an outer level never also reacts
      // to the same keypress against its own (currently unfocused) item.
      event.stopPropagation();
      switch (event.key) {
        case "ArrowDown":
          event.preventDefault();
          moveFocus(1);
          break;
        case "ArrowUp":
          event.preventDefault();
          moveFocus(-1);
          break;
        case "ArrowRight": {
          const item = focusedId ? findItem(groupsToRender, focusedId) : undefined;
          if (item && hasSubmenu(item)) {
            event.preventDefault();
            openSubmenuFor(item);
          }
          break;
        }
        case "ArrowLeft":
          if (onRequestCloseSubmenu) {
            event.preventDefault();
            onRequestCloseSubmenu();
          }
          break;
        case "Enter":
        case " ": {
          const item = focusedId ? findItem(groupsToRender, focusedId) : undefined;
          if (item) {
            event.preventDefault();
            activate(item);
          }
          break;
        }
        default:
          break;
      }
    },
    [focusedId, groupsToRender, moveFocus, openSubmenuFor, onRequestCloseSubmenu, activate],
  );

  return (
    <ul
      ref={listRef}
      role="menu"
      className="context-menu"
      style={{ position: "fixed", left: position.x, top: position.y }}
      onKeyDown={handleKeyDown}
    >
      {groupsToRender.map((group, groupIndex) => (
        <Fragment key={groupIndex}>
          {groupIndex > 0 && <li role="separator" className="context-menu-separator" />}
          {group.map((item) => {
            const itemHasSubmenu = hasSubmenu(item);
            const isSubmenuOpen = itemHasSubmenu && item.id === openSubmenuId;
            return (
              <li
                key={item.id}
                role="none"
                onMouseLeave={() => {
                  if (isSubmenuOpen) {
                    closeSubmenu();
                  }
                }}
              >
                <button
                  type="button"
                  role="menuitem"
                  ref={(element) => {
                    if (element) {
                      itemRefs.current.set(item.id, element);
                    } else {
                      itemRefs.current.delete(item.id);
                    }
                  }}
                  tabIndex={item.id === focusedId ? 0 : -1}
                  className={`context-menu-item${item.disabled ? " context-menu-item-disabled" : ""}`}
                  aria-disabled={item.disabled || undefined}
                  aria-haspopup={itemHasSubmenu || undefined}
                  aria-expanded={itemHasSubmenu ? isSubmenuOpen : undefined}
                  title={item.disabled ? item.disabledReason : undefined}
                  onMouseEnter={() => {
                    if (!item.disabled) {
                      setFocusedId(item.id);
                    }
                    if (itemHasSubmenu) {
                      openSubmenuFor(item);
                    }
                  }}
                  onClick={() => activate(item)}
                >
                  <span className="context-menu-item-icon">
                    {item.icon && <span className={`mdi ${item.icon}`} aria-hidden="true" />}
                  </span>
                  <span className="context-menu-item-label">{item.label}</span>
                  {itemHasSubmenu && (
                    <span className="mdi mdi-chevron-right context-menu-submenu-indicator" aria-hidden="true" />
                  )}
                </button>
                {isSubmenuOpen && itemRefs.current.get(item.id) && (
                  <ContextMenuLevel
                    groups={item.items}
                    anchor={{ rect: itemRefs.current.get(item.id)!.getBoundingClientRect() }}
                    onSelectItem={onSelectItem}
                    onRequestCloseSubmenu={() => closeSubmenu({ returnFocus: true })}
                  />
                )}
              </li>
            );
          })}
        </Fragment>
      ))}
    </ul>
  );
}

/**
 * A composable, content-agnostic floating context menu: a consumer supplies one or more
 * independently-produced item groups (design.md's Requirement 5) and this component handles
 * positioning, nesting, disabled items, and keyboard navigation. Adds no consumer-specific logic.
 */
export function ContextMenu({ open, groups, position, onClose }: ContextMenuProps) {
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);
  const hasCapturedFocusRef = useRef(false);
  const isEmpty = nonEmptyGroups(groups).length === 0;

  // Captured during render, not in an effect: ContextMenuLevel's own child effect (which
  // moves focus onto the first item) runs before this component's effects do - React always
  // runs child effects ahead of parent effects - so capturing here in an effect would already
  // see focus moved into the menu instead of wherever it was beforehand.
  if (open && !isEmpty) {
    if (!hasCapturedFocusRef.current) {
      previouslyFocusedRef.current = document.activeElement as HTMLElement | null;
      hasCapturedFocusRef.current = true;
    }
  } else {
    hasCapturedFocusRef.current = false;
  }

  useEffect(() => {
    if (open && isEmpty && import.meta.env.DEV) {
      console.warn("ContextMenu: received an empty items list (no groups, or all groups empty) - rendering nothing.");
    }
  }, [open, isEmpty]);

  useEffect(() => {
    if (!open || isEmpty) {
      return;
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
      }
    };

    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      previouslyFocusedRef.current?.focus();
    };
  }, [open, isEmpty, onClose]);

  const handleSelectItem = useCallback(
    (item: ContextMenuActionItem) => {
      item.onSelect();
      onClose();
    },
    [onClose],
  );

  if (!open || isEmpty) {
    return null;
  }

  return (
    <div
      className="context-menu-catcher"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <ContextMenuLevel groups={groups} anchor={{ point: position }} onSelectItem={handleSelectItem} />
    </div>
  );
}
