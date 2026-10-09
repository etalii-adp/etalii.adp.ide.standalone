import { useEffect, useMemo, useRef, useState } from "react";
import type { LayoutPlacement, LayoutPositions } from "./layoutAlgorithm";

/** How long a change of layout takes to show, in milliseconds. Short: it explains a move, it is not a show. */
export const PLACEMENT_MOTION_MS = 220;

/**
 * Whether the reader's system allows motion. False where it asks for less of it, and false where
 * the question cannot be asked at all - a test environment has no display to animate on, and a
 * layout that jumps there is the honest answer.
 */
function motionAllowed(): boolean {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function" || typeof window.requestAnimationFrame !== "function") {
    return false;
  }
  return !window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

/** One frame of the way from `from` to `to`, eased so the move settles rather than stops. */
function between(from: LayoutPlacement, to: LayoutPlacement, progress: number): LayoutPlacement {
  const eased = 1 - (1 - progress) * (1 - progress);
  return { x: from.x + (to.x - from.x) * eased, y: from.y + (to.y - from.y) * eased, width: to.width };
}

/**
 * The placements to draw while a layout's answer changes: the previous ones moving to the new.
 *
 * A layout that settles by steps moves elements when the model changes, and an element that is
 * simply somewhere else on the next frame reads as a different diagram. So the canvas draws the
 * way there: each element that had a place and has a new one travels between them over
 * {@link PLACEMENT_MOTION_MS}. An element that had no place appears where it belongs, and one
 * that lost its place - it was locked, or removed - is no longer placed at all.
 *
 * Nothing moves where the reader's system asks for reduced motion: the new placements are drawn
 * at once.
 *
 * @param target What the layout answers now. Null - the model's own positions - is passed through.
 * @param animated Whether this layout's changes are shown as motion at all.
 */
export function useAnimatedPlacements(target: LayoutPositions, animated: boolean): LayoutPositions {
  const [drawn, setDrawn] = useState<LayoutPositions>(target);
  const drawnRef = useRef<LayoutPositions>(target);
  const targetRef = useRef<LayoutPositions>(target);

  useEffect(() => {
    if (targetRef.current === target) {
      return;
    }
    targetRef.current = target;
    const from = drawnRef.current;
    if (!animated || target === null || from === null || !motionAllowed()) {
      drawnRef.current = target;
      setDrawn(target);
      return;
    }

    const started = performance.now();
    let frame = 0;
    const step = (now: number) => {
      const progress = Math.min(1, (now - started) / PLACEMENT_MOTION_MS);
      const next: LayoutPositions =
        progress >= 1
          ? target
          : new Map([...target].map(([id, to]) => [id, from.has(id) ? between(from.get(id) as LayoutPlacement, to, progress) : to]));
      drawnRef.current = next;
      setDrawn(next);
      if (progress < 1) {
        frame = window.requestAnimationFrame(step);
      }
    };
    frame = window.requestAnimationFrame(step);
    return () => window.cancelAnimationFrame(frame);
  }, [target, animated]);

  // Before the effect has run for a new target there is one render with the old placements, which
  // is the first frame of the way there. An element the old ones do not know is drawn where it
  // belongs even in that frame, and one the new ones do not place is not placed: the keys are
  // always the target's. Where nothing is animated the target itself is drawn, in the same render.
  const moving = animated && target !== null && drawn !== null && drawn !== target && motionAllowed();
  return useMemo(
    () => (moving ? new Map([...(target as ReadonlyMap<string, LayoutPlacement>)].map(([id, to]) => [id, (drawn as ReadonlyMap<string, LayoutPlacement>).get(id) ?? to])) : target),
    [moving, target, drawn],
  );
}
