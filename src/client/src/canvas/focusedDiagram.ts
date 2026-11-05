import { useSyncExternalStore } from "react";

/**
 * The diagram whose canvas is showing: what an empty selection shows the properties of (etalii.adp
 * spec 074, User Story 8). A canvas click on nothing clears the selection, so the selection cannot
 * say which diagram it was; the canvas says it here instead.
 *
 * `version` changes whenever the diagram's model does, so that the property grid reads the diagram's
 * rows again after an edit, an undo or a change from outside, as it does for a selected element.
 */
export interface FocusedDiagram {
  entryId: Uint8Array;
  version: number;
}

let current: FocusedDiagram | null = null;
let version = 0;
const listeners = new Set<() => void>();

function publish(next: FocusedDiagram | null) {
  current = next;
  for (const listener of [...listeners]) {
    listener();
  }
}

/** Says that the diagram at `entryId` is showing, with a model that may have changed. */
export function showFocusedDiagram(entryId: Uint8Array) {
  version += 1;
  publish({ entryId, version });
}

/** Says that the diagram at `entryId` no longer shows; another canvas's claim is left alone. */
export function clearFocusedDiagram(entryId: Uint8Array) {
  if (current?.entryId === entryId) {
    publish(null);
  }
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** The diagram showing, or null when none is. */
export function useFocusedDiagram(): FocusedDiagram | null {
  return useSyncExternalStore(subscribe, () => current);
}
