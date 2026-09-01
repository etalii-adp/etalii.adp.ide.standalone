import { useEffect, useRef, useState } from "react";

/**
 * The right-click menu discipline every canvas shares: the menu opens once the pushed
 * selection for that element arrives with its actions, so what it shows is the backend's
 * answer rather than a guess. When the element is *already* the pushed selection, re-selecting
 * would push the same key, the effect waiting for a key change would never fire, and the menu
 * never opened - so that case opens at once from the actions already at hand.
 *
 * The hook owns the position state and the pending machinery; the caller supplies how a
 * selection is requested, and renders the shared ContextMenu from what it returns.
 */
export function useElementContextMenu(
  selectionKey: string | undefined,
  hasActions: boolean,
  requestSelection: (elementId: string) => void,
): {
  menuPosition: { x: number; y: number } | null;
  openMenuAt: (event: React.MouseEvent, elementId: string) => void;
  closeMenu: () => void;
} {
  const [menuPosition, setMenuPosition] = useState<{ x: number; y: number } | null>(null);
  const pendingRef = useRef<{ id: string; position: { x: number; y: number } } | null>(null);

  useEffect(() => {
    const pending = pendingRef.current;
    if (pending && selectionKey === `element:${pending.id}`) {
      pendingRef.current = null;
      setMenuPosition(pending.position);
    }
  }, [selectionKey, hasActions]);

  const openMenuAt = (event: React.MouseEvent, elementId: string) => {
    event.preventDefault();
    event.stopPropagation();
    const position = { x: event.clientX, y: event.clientY };
    if (selectionKey === `element:${elementId}` && hasActions) {
      pendingRef.current = null;
      setMenuPosition(position);
      return;
    }

    pendingRef.current = { id: elementId, position };
    requestSelection(elementId);
  };

  return { menuPosition, openMenuAt, closeMenu: () => setMenuPosition(null) };
}
