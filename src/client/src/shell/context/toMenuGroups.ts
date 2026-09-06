import type { ContextAction, ContextActionGroup } from "../../generated/context-contract_pb";
import type { ContextMenuGroup, ContextMenuItem } from "./ContextMenu";

/**
 * Maps backend-reported actions onto the menu's own vocabulary; the menu learns nothing
 * about files. Shared by every surface that renders context actions as a menu - the
 * explorer's right-click menu and the ribbon's drop-down buttons alike.
 */
export function toMenuGroups(groups: ContextActionGroup[], onSelect: (action: ContextAction) => void): ContextMenuGroup[] {
  return groups.map((group) =>
    group.actions.map((action): ContextMenuItem => {
      const base = {
        id: action.id,
        label: action.label,
        icon: action.icon,
        disabled: !action.available,
        disabledReason: action.unavailableReason,
      };
      return action.items.length > 0
        ? { ...base, items: toMenuGroups(action.items, onSelect) }
        : { ...base, onSelect: () => onSelect(action) };
    }),
  );
}
