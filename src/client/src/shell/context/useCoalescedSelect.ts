import { useCallback, useEffect, useRef } from "react";

/**
 * Collapses a burst of plain selections into one trailing send, while a gesture (a
 * right-click, an activation, a clear) goes out at once and takes any pending plain
 * selection with it. Holding an arrow key in the explorer therefore costs one round trip
 * for the row it comes to rest on, and a right-click is never delayed by the rows
 * passed on the way there.
 *
 * Trailing-edge like `useDebouncedValue`, but imperative: a debounced *value* cannot be
 * flushed early, and flushing early is the whole point here.
 */
export function useCoalescedSelect<T>(send: (value: T) => void, delayMs: number): (value: T, immediate: boolean) => void {
  const sendRef = useRef(send);
  sendRef.current = send;
  const pendingRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  useEffect(() => () => clearTimeout(pendingRef.current), []);

  return useCallback(
    (value: T, immediate: boolean) => {
      clearTimeout(pendingRef.current);
      pendingRef.current = undefined;
      if (immediate) {
        sendRef.current(value);
        return;
      }
      pendingRef.current = setTimeout(() => {
        pendingRef.current = undefined;
        sendRef.current(value);
      }, delayMs);
    },
    [delayMs],
  );
}
