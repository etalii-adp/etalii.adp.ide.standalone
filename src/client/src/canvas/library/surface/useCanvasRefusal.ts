import { useMemo } from "react";
import { useCanvasRefusalReporter } from "./canvasRefusals";

/**
 * The canvas's refusal line, for the rare gesture a module carries itself.
 *
 * Almost every refusal needs nothing from a module: `executeAction`, `executeShortcut`,
 * `setProperty` and the stream's `moveElementTo` report their own, and so does every action the
 * library sends. Two gestures are not carried by any of those - `causal-loop` refusing a drop that
 * landed on nothing, before anything is sent, and `mindmap`'s re-parenting move, which it builds
 * itself - and this is how they reach the same line rather than one of the module's own, or none.
 * Outside a canvas both do nothing.
 */
export function useCanvasRefusal(): {
  /** A gesture of the module's own has been sent to the backend: the line clears, as for any other. */
  attempted: () => void;
  /** That gesture was refused - by the backend, or by the module before anything was sent. */
  refuse: (message: string) => void;
} {
  const reporter = useCanvasRefusalReporter();
  return useMemo(
    () => ({
      attempted: () => reporter?.attempted(),
      refuse: (message: string) => reporter?.refused(message),
    }),
    [reporter],
  );
}
