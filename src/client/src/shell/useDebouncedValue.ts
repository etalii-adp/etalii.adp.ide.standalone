import { useEffect, useState } from "react";

/**
 * The last value that stayed unchanged for `delayMs` — a trailing-edge debounce.
 *
 * Trailing, not leading, because the thing being debounced here is a validation round
 * trip: validating the first character and then a half-typed name would show verdicts
 * about text the user has already moved past. Built on React's own effect model with a
 * `clearTimeout` cleanup, so every new value cancels the pending emission and an unmount
 * mid-window emits nothing at all — no extra dependency needed for one debounced input.
 */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timeout = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timeout);
  }, [value, delayMs]);

  return debounced;
}
