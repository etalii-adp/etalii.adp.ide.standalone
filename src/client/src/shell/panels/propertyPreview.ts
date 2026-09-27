import { useSyncExternalStore } from "react";

/**
 * Values the property grid shows for one element while a canvas gesture is still deciding them -
 * a phase end being dragged, a span being resized - before anything is written.
 *
 * <b>Shown, never written.</b> The grid writes nothing for a preview; the gesture's release writes
 * once, the backend pushes, and the grid reads the properties again as it always has. The preview
 * only covers the time in between, which for a drag is all of it.
 *
 * <b>Kept until the answer arrives.</b> A released gesture's preview stays until the grid has read
 * the properties again, or for {@link SETTLE_TIMEOUT_MS} at most - dropping it at the release drew
 * the old value for the length of the round trip, the flicker this exists to remove.
 */
export interface PropertyPreview {
  /** The element the values belong to; the grid shows them only while that element is selected. */
  elementId: string;
  /** Property id to the value to show in its place. */
  values: Readonly<Record<string, string>>;
}

/** How long a released gesture's preview waits for the grid to read the properties again. */
export const SETTLE_TIMEOUT_MS = 2000;

let current: PropertyPreview | null = null;
let settling = false;
let timer: ReturnType<typeof setTimeout> | undefined;
const listeners = new Set<() => void>();

function publish(next: PropertyPreview | null) {
  current = next;
  for (const listener of [...listeners]) {
    listener();
  }
}

function stopTimer() {
  if (timer !== undefined) {
    clearTimeout(timer);
    timer = undefined;
  }
}

/** Shows `values` for `elementId` in place of what the backend last said. */
export function showPropertyPreview(elementId: string, values: Readonly<Record<string, string>>): void {
  stopTimer();
  settling = false;
  publish({ elementId, values });
}

/** The gesture wrote what it previewed: keep showing it until the grid has read the answer. */
export function settlePropertyPreview(): void {
  if (current !== null) {
    settling = true;
  }
}

/**
 * The gesture is over. A preview that was written stays until the answer is read (or the timeout);
 * one that was not - an abandoned drag, or a release where it started - goes now.
 */
export function endPropertyPreview(): void {
  stopTimer();
  if (current === null) {
    return;
  }
  if (settling) {
    timer = setTimeout(clearPropertyPreview, SETTLE_TIMEOUT_MS);
    return;
  }
  publish(null);
}

/** The grid has read the properties again after a write: a settling preview has been answered. */
export function answerPropertyPreview(): void {
  if (settling) {
    clearPropertyPreview();
  }
}

/** Drops any preview at once. */
export function clearPropertyPreview(): void {
  stopTimer();
  settling = false;
  if (current !== null) {
    publish(null);
  }
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** The preview to show, if any. */
export function usePropertyPreview(): PropertyPreview | null {
  return useSyncExternalStore(subscribe, () => current);
}
