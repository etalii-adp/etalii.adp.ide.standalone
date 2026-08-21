import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useDebouncedValue } from "./useDebouncedValue";

describe("useDebouncedValue", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("emits nothing before the delay has elapsed", () => {
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 200), {
      initialProps: { value: "first" },
    });

    rerender({ value: "second" });
    act(() => {
      vi.advanceTimersByTime(199);
    });

    expect(result.current).toBe("first");
  });

  it("emits once with the final value when changes come faster than the delay", () => {
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 200), {
      initialProps: { value: "a" },
    });

    for (const value of ["ab", "abc", "abcd"]) {
      rerender({ value });
      act(() => {
        vi.advanceTimersByTime(100);
      });
      expect(result.current).toBe("a");
    }

    act(() => {
      vi.advanceTimersByTime(200);
    });

    expect(result.current).toBe("abcd");
  });

  it("emits the new value once it has stayed put for the whole delay", () => {
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 200), {
      initialProps: { value: "before" },
    });

    rerender({ value: "after" });
    act(() => {
      vi.advanceTimersByTime(200);
    });

    expect(result.current).toBe("after");
  });

  it("emits nothing when unmounted part-way through the window", () => {
    const { rerender, unmount } = renderHook(({ value }) => useDebouncedValue(value, 200), {
      initialProps: { value: "before" },
    });

    rerender({ value: "after" });
    unmount();

    // Nothing is left to fire: the cleanup cleared the pending timeout, so advancing past
    // the window cannot schedule a state update on an unmounted hook.
    expect(vi.getTimerCount()).toBe(0);
  });
});
