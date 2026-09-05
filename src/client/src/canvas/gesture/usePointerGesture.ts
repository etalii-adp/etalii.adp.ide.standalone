import { useMemo, useRef } from "react";

/**
 * The one movement threshold separating a click from a drag, in client pixels, by
 * hypotenuse. Strictly greater-than: a press that moves exactly this far is still a click.
 *
 * It lives here because the threshold IS the click/drag boundary this module owns. Before
 * it, the same intent existed as 4px-by-hypotenuse on one canvas and 3px-by-Manhattan on
 * another - hand-copied apart, as duplicated rules do.
 */
export const GESTURE_MOVEMENT_THRESHOLD_PX = 4;

/** What the owning module does with each verdict. The target is the module's own type. */
export interface PointerGestureCallbacks<Target> {
  /** An unmoved release: whatever pressing means here - select, focus, deselect. */
  onPress: (target: Target) => void;
  /** Live feedback while dragging. Client-pixel deltas; unit conversion is the module's. */
  onDragMove?: (target: Target, dx: number, dy: number) => void;
  /** The commit: move to a coordinate, reparent - module semantics, module's call. */
  onDragEnd?: (target: Target, dx: number, dy: number) => void;
  /** The gesture dissolved (capture lost, Escape): undo whatever onDragMove showed. */
  onDragAbandon?: (target: Target) => void;
}

/** The pointer wiring for one pressable thing. Spread onto the element. */
export interface PointerPressWiring {
  onPointerDown: (event: React.PointerEvent) => void;
  onPointerMove: (event: React.PointerEvent) => void;
  onPointerUp: (event: React.PointerEvent) => void;
  onPointerCancel: (event: React.PointerEvent) => void;
  onLostPointerCapture: (event: React.PointerEvent) => void;
}

export interface PointerGesture<Target> {
  /** Wiring for a pressable target - an element, a relation. */
  press(target: Target): PointerPressWiring;
  /**
   * Wiring for the surface itself, deselect-on-click and pan included. Identical to
   * {@link press} except that a press is only begun when it lands on the surface element
   * itself: every press that began on a child arrives here by bubbling too, and without the
   * guard a label or decoration that is not itself a target would start a background
   * gesture.
   */
  background(target: Target): PointerPressWiring;
  /** Ends the active gesture with the abandon verdict - the module's Escape handler calls this. */
  abandon(): void;
}

/**
 * One gesture at a time, held in a ref rather than in React state: a pointer move and its
 * pointerup can batch into a single render, and state read there is a frame behind.
 */
interface ActiveGesture<Target> {
  target: Target;
  pointerId: number;
  /** The element that captured the pointer, kept so abandon() can release it. */
  capture: Element;
  startX: number;
  startY: number;
  moved: boolean;
}

/**
 * Decides, once per pointer gesture and at its end, whether that gesture was a click or a
 * drag - from what the gesture itself recorded, never from the browser's trailing `click`
 * event. That refusal is the point, not an omission: the click event's target depends on
 * DOM geometry after the drop (it fires on the common ancestor when press and release
 * targets differ, and not at all when the pointer is released outside the window), which is
 * how a "suppress the next click" flag ends up armed with no consumer and silently swallows
 * the next legitimate click. There is no flag here because there is no click to suppress.
 *
 * The pointer is captured on the PRESSED element, never on the surface. Capture retargets
 * every later pointer event - and with it the browser's own click - to the capturing
 * element, so capturing to the surface silently moves clicks off the element; AnsibleCanvas
 * learned that in a manual pass after every unit test stayed green (its onNodePointerDown
 * comment tells the story). Capturing on the element keeps the events where the wiring is,
 * and makes a release outside the surface or the window end the gesture through the
 * ordinary pointerup path - no mouseleave hacks.
 *
 * What a target IS stays the module's business: it is threaded through untouched, the way
 * selection.ts threads element ids.
 */
export function usePointerGesture<Target>(callbacks: PointerGestureCallbacks<Target>): PointerGesture<Target> {
  const active = useRef<ActiveGesture<Target> | null>(null);
  // Callbacks are read through a ref so the wiring stays stable while the module re-renders.
  const callbacksRef = useRef(callbacks);
  callbacksRef.current = callbacks;

  return useMemo(() => {
    const begin = (target: Target, event: React.PointerEvent) => {
      if (event.button !== 0) {
        return; // the right button is the menu's gesture, not ours
      }
      if (active.current !== null) {
        return; // one gesture at a time; a second pointer starts nothing
      }

      // The pressed target owns this gesture exclusively - the press must not also reach an
      // enclosing target (a node press bubbling into a background pan).
      event.stopPropagation();
      const capture = event.currentTarget as Element;
      capture.setPointerCapture?.(event.pointerId);
      active.current = {
        target,
        pointerId: event.pointerId,
        capture,
        startX: event.clientX,
        startY: event.clientY,
        moved: false,
      };
    };

    const move = (event: React.PointerEvent) => {
      const gesture = active.current;
      if (gesture === null || event.pointerId !== gesture.pointerId) {
        return;
      }

      const dx = event.clientX - gesture.startX;
      const dy = event.clientY - gesture.startY;
      if (!gesture.moved && Math.hypot(dx, dy) <= GESTURE_MOVEMENT_THRESHOLD_PX) {
        return; // wobble while clicking is not a drag
      }

      gesture.moved = true;
      callbacksRef.current.onDragMove?.(gesture.target, dx, dy);
    };

    const up = (event: React.PointerEvent) => {
      const gesture = active.current;
      if (gesture === null || event.pointerId !== gesture.pointerId) {
        return;
      }

      // Idle before the verdict's callback runs: the implicit lostpointercapture that
      // follows a pointerup must find nothing to abandon.
      active.current = null;
      gesture.capture.releasePointerCapture?.(gesture.pointerId);

      if (gesture.moved) {
        callbacksRef.current.onDragEnd?.(gesture.target, event.clientX - gesture.startX, event.clientY - gesture.startY);
      } else {
        callbacksRef.current.onPress(gesture.target);
      }
    };

    const dissolve = (event: React.PointerEvent) => {
      const gesture = active.current;
      if (gesture === null || event.pointerId !== gesture.pointerId) {
        return; // after a pointerup this is the implicit capture release: nothing to do
      }

      active.current = null;
      callbacksRef.current.onDragAbandon?.(gesture.target);
    };

    const wiring = (down: (event: React.PointerEvent) => void): PointerPressWiring => ({
      onPointerDown: down,
      onPointerMove: move,
      onPointerUp: up,
      onPointerCancel: dissolve,
      onLostPointerCapture: dissolve,
    });

    return {
      press: (target: Target) => wiring((event) => begin(target, event)),
      background: (target: Target) =>
        wiring((event) => {
          if (event.target === event.currentTarget) {
            begin(target, event);
          }
        }),
      abandon: () => {
        const gesture = active.current;
        if (gesture === null) {
          return;
        }
        active.current = null;
        gesture.capture.releasePointerCapture?.(gesture.pointerId);
        callbacksRef.current.onDragAbandon?.(gesture.target);
      },
    };
  }, []);
}
