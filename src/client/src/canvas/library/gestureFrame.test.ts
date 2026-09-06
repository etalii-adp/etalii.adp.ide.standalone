import { describe, expect, it, vi } from "vitest";
import {
  beginGestureFrame,
  createGestureValue,
  environmentFrameProvider,
  valueWrite,
  type FrameProvider,
  type LiveWrite,
} from "./gestureFrame";

/** A frame provider under the test's control: frames run when the test flushes them. */
function manualFrameProvider() {
  let next = 1;
  const scheduled = new Map<number, () => void>();
  const provider: FrameProvider = {
    request: (callback) => {
      const handle = next++;
      scheduled.set(handle, callback);
      return handle;
    },
    cancel: (handle) => {
      scheduled.delete(handle);
    },
  };
  const flush = () => {
    const callbacks = [...scheduled.values()];
    scheduled.clear();
    for (const callback of callbacks) {
      callback();
    }
  };
  return { provider, flush, pendingCount: () => scheduled.size };
}

function recordingWrite(): LiveWrite<number> & { applied: number[]; reverts: number } {
  const write = {
    applied: [] as number[],
    reverts: 0,
    apply(value: number) {
      write.applied.push(value);
    },
    revert() {
      write.reverts++;
    },
  };
  return write;
}

const rect = { left: 10, top: 20, width: 500, height: 400 };

describe("beginGestureFrame", () => {
  it("coalesces many moves into one applied frame, the last value winning", () => {
    const { provider, flush, pendingCount } = manualFrameProvider();
    const write = recordingWrite();
    const frame = beginGestureFrame(rect, [write], provider);

    frame.move(1);
    frame.move(2);
    frame.move(3);

    // Nothing applied yet, and the three moves scheduled ONE frame between them.
    expect(write.applied).toEqual([]);
    expect(pendingCount()).toBe(1);

    flush();
    expect(write.applied).toEqual([3]);
  });

  it("schedules a fresh frame for moves arriving after the previous frame ran", () => {
    const { provider, flush } = manualFrameProvider();
    const write = recordingWrite();
    const frame = beginGestureFrame(rect, [write], provider);

    frame.move(1);
    flush();
    frame.move(2);
    flush();

    expect(write.applied).toEqual([1, 2]);
  });

  it("revert undoes every write handle that was applied", () => {
    const { provider, flush } = manualFrameProvider();
    const first = recordingWrite();
    const second = recordingWrite();
    const frame = beginGestureFrame(rect, [first, second], provider);

    frame.move(5);
    flush();
    frame.revert();

    expect(first.reverts).toBe(1);
    expect(second.reverts).toBe(1);
  });

  it("revert before anything was applied reverts nothing - there is nothing to undo", () => {
    const { provider } = manualFrameProvider();
    const write = recordingWrite();
    const frame = beginGestureFrame(rect, [write], provider);

    frame.move(5); // recorded, but the frame never ran
    frame.revert();

    expect(write.reverts).toBe(0);
    expect(write.applied).toEqual([]);
  });

  it("commit cancels the pending frame and undoes the applied writes", () => {
    const { provider, flush, pendingCount } = manualFrameProvider();
    const write = recordingWrite();
    const frame = beginGestureFrame(rect, [write], provider);

    frame.move(1);
    flush();
    frame.move(2); // pending when the gesture ends
    frame.commit();

    expect(pendingCount()).toBe(0);
    expect(write.applied).toEqual([1]); // the pending value was never applied
    expect(write.reverts).toBe(1);
  });

  it("cancel drops the pending frame without touching the writes - the DOM is unmounting", () => {
    const { provider, flush, pendingCount } = manualFrameProvider();
    const write = recordingWrite();
    const frame = beginGestureFrame(rect, [write], provider);

    frame.move(1);
    flush();
    frame.move(2);
    frame.cancel();
    flush();

    expect(pendingCount()).toBe(0);
    expect(write.applied).toEqual([1]);
    expect(write.reverts).toBe(0);
  });

  it("ignores moves after the gesture ended", () => {
    const { provider, flush } = manualFrameProvider();
    const write = recordingWrite();
    const frame = beginGestureFrame(rect, [write], provider);

    frame.commit();
    frame.move(9);
    flush();

    expect(write.applied).toEqual([]);
  });

  it("applies every move synchronously when no frame provider exists", () => {
    const write = recordingWrite();
    const frame = beginGestureFrame(rect, [write], null);

    frame.move(1);
    frame.move(2);

    // The same write path, paced by the moves themselves rather than by frames.
    expect(write.applied).toEqual([1, 2]);

    frame.revert();
    expect(write.reverts).toBe(1);
  });

  it("exposes the rect it was begun with - read once, served for the gesture's life", () => {
    const frame = beginGestureFrame(rect, [], null);
    expect(frame.rect).toBe(rect);
  });
});

describe("createGestureValue", () => {
  it("notifies subscribers on set, and hands the latest value back", () => {
    const cell = createGestureValue<number>();
    const heard: Array<number | null> = [];
    cell.subscribe(() => heard.push(cell.get()));

    cell.set(1);
    cell.set(2);

    expect(heard).toEqual([1, 2]);
    expect(cell.get()).toBe(2);
  });

  it("clear notifies once and an already-empty clear wakes nobody", () => {
    const cell = createGestureValue<number>();
    const listener = vi.fn();
    cell.subscribe(listener);

    cell.clear(); // empty already - a revert of a gesture that never published
    expect(listener).not.toHaveBeenCalled();

    cell.set(1);
    cell.clear();
    cell.clear();
    expect(listener).toHaveBeenCalledTimes(2); // the set and the one real clear
    expect(cell.get()).toBeNull();
  });

  it("an unsubscribed listener hears nothing more", () => {
    const cell = createGestureValue<number>();
    const listener = vi.fn();
    const unsubscribe = cell.subscribe(listener);

    cell.set(1);
    unsubscribe();
    cell.set(2);

    expect(listener).toHaveBeenCalledTimes(1);
  });

  it("valueWrite publishes on apply and clears on revert - the scheduler's store handle", () => {
    const cell = createGestureValue<string>();
    const write = valueWrite(cell);

    write.apply("live");
    expect(cell.get()).toBe("live");

    write.revert();
    expect(cell.get()).toBeNull();
  });
});

describe("environmentFrameProvider", () => {
  it("declines jsdom's requestAnimationFrame: a 16ms timer is not a paint signal", () => {
    // jsdom DOES provide requestAnimationFrame in this test environment - that is the
    // measured fact this test pins, because the synchronous fallback keys on it. If this
    // assertion ever fails, the environment changed and the detection needs re-measuring.
    expect(typeof requestAnimationFrame).toBe("function");
    expect(navigator.userAgent).toContain("jsdom");
    expect(environmentFrameProvider()).toBeNull();
  });

  it("returns null where requestAnimationFrame does not exist at all", () => {
    const original = globalThis.requestAnimationFrame;
    vi.stubGlobal("requestAnimationFrame", undefined);
    try {
      expect(environmentFrameProvider()).toBeNull();
    } finally {
      vi.stubGlobal("requestAnimationFrame", original);
    }
  });
});
