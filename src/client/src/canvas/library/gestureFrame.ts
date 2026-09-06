/**
 * The gesture-frame scheduler: what runs between the pointer and the DOM while a gesture is
 * in flight, so that per-frame cost is a function of the gesture rather than of how many
 * elements the diagram draws (drag-and-drop-centralization Requirement 1.1).
 *
 * Three pieces, and no React dependency:
 *
 * - **The cached rect.** The surface rectangle is read once when the gesture begins and
 *   served from here for the gesture's life, so no code path performs a synchronous layout
 *   read per pointer frame (Requirement 1.3). The trade: a window resized mid-gesture makes
 *   the cached rect stale for the remainder of that gesture, and the next gesture reads
 *   afresh - the same trade every hand-rolled drag in this repository already made
 *   implicitly, stated here rather than implied.
 * - **The coalescer.** Pointer moves record the latest value and schedule one animation
 *   frame if none is pending; the frame applies the newest value and clears. Events arriving
 *   faster than the display refreshes collapse into one applied frame per displayed frame,
 *   last value winning (Requirement 1.2).
 * - **The live writes.** Each applied frame hands the latest value to the gesture's write
 *   handles - a DOM attribute write, a scrollbar thumb position, a publication to the
 *   gesture's own subscribers - and each handle knows how to undo itself, so an abandoned
 *   gesture leaves nothing behind (Requirement 2.3).
 */

/** The surface rectangle at gesture start - all a gesture needs of getBoundingClientRect. */
export interface SurfaceRect {
  left: number;
  top: number;
  width: number;
  height: number;
}

/** What schedules an applied frame: requestAnimationFrame, where the environment paints. */
export interface FrameProvider {
  request(callback: () => void): number;
  cancel(handle: number): void;
}

/**
 * One live write: applies the latest per-frame value while the gesture is in flight, and
 * undoes every write it made when the gesture ends or dissolves. The handle owns the undo
 * because it is the one that knows what it wrote over.
 */
export interface LiveWrite<T> {
  apply(value: T): void;
  revert(): void;
}

/** One gesture's frame scheduling, from first move to commit, revert or unmount. */
export interface GestureFrame<T> {
  /** The rect read once at gesture start; every conversion during the gesture uses it. */
  readonly rect: SurfaceRect;
  /** Record the newest value and schedule one applied frame if none is pending. */
  move(value: T): void;
  /**
   * The gesture completed: cancel any pending frame and undo the live writes. Mechanically
   * identical to {@link revert} - the difference is the caller's next line, which is the
   * single state write and dispatch. React then paints the final state through the same
   * code path as before the gesture, in the same batch as the undo, so nothing flickers.
   */
  commit(): void;
  /** The gesture dissolved: cancel any pending frame and undo the live writes. */
  revert(): void;
  /**
   * The surface is unmounting: cancel any pending frame without touching the DOM - the
   * nodes the writes went to are going away with the component.
   */
  cancel(): void;
}

/**
 * The environment's frame provider, or null where deferring to one buys nothing.
 *
 * Null in two environments, deliberately distinguished:
 * - no `requestAnimationFrame` at all, and
 * - jsdom, which *does* implement `requestAnimationFrame` - measured in this repository's
 *   test environment on 2026-09-06 - but as a bare 16ms timer, not a paint signal. Deferring
 *   writes to a timer that paints nothing only desynchronizes tests from the gestures they
 *   drive, so a non-painting environment takes the synchronous path and exercises the same
 *   write code the browser's frames do.
 */
export function environmentFrameProvider(): FrameProvider | null {
  if (typeof requestAnimationFrame !== "function" || typeof cancelAnimationFrame !== "function") {
    return null;
  }
  if (typeof navigator !== "undefined" && navigator.userAgent.includes("jsdom")) {
    return null;
  }
  return {
    request: (callback) => requestAnimationFrame(() => callback()),
    cancel: (handle) => cancelAnimationFrame(handle),
  };
}

/**
 * Begin scheduling one gesture's frames. With a frame provider, moves coalesce into one
 * applied frame per displayed frame; with none, every move applies synchronously - the same
 * write path, differently paced.
 */
export function beginGestureFrame<T>(
  rect: SurfaceRect,
  writes: ReadonlyArray<LiveWrite<T>>,
  frameProvider: FrameProvider | null = environmentFrameProvider(),
): GestureFrame<T> {
  let latest: T | undefined;
  let pending: number | null = null;
  let applied = false;
  let ended = false;

  const applyLatest = () => {
    if (ended || latest === undefined) {
      return;
    }
    for (const write of writes) {
      write.apply(latest);
    }
    applied = true;
  };

  const settle = (revertWrites: boolean) => {
    if (ended) {
      return;
    }
    ended = true;
    if (pending !== null && frameProvider !== null) {
      frameProvider.cancel(pending);
      pending = null;
    }
    if (revertWrites && applied) {
      for (const write of writes) {
        write.revert();
      }
      applied = false;
    }
  };

  return {
    rect,
    move: (value) => {
      if (ended) {
        return;
      }
      latest = value;
      if (frameProvider === null) {
        applyLatest();
        return;
      }
      if (pending === null) {
        pending = frameProvider.request(() => {
          pending = null;
          applyLatest();
        });
      }
    },
    commit: () => settle(true),
    revert: () => settle(true),
    cancel: () => settle(false),
  };
}
