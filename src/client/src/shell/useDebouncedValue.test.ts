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
    // Arrange.
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 200), {
      initialProps: { value: "first" },
    });

    // Act.
    rerender({ value: "second" });
    act(() => {
      vi.advanceTimersByTime(199);
    });

    // Assert.
    expect(result.current).toBe("first");
  });

  it("emits once with the final value when changes come faster than the delay", () => {
    // Arrange.
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 200), {
      initialProps: { value: "a" },
    });

    // Arrange, continued.
    for (const value of ["ab", "abc", "abcd"]) {
      rerender({ value });
      act(() => {
        vi.advanceTimersByTime(100);
      });
      expect(result.current).toBe("a");
    }

    // Act.
    act(() => {
      vi.advanceTimersByTime(200);
    });

    // Assert.
    expect(result.current).toBe("abcd");
  });

  it("emits the new value once it has stayed put for the whole delay", () => {
    // Arrange.
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 200), {
      initialProps: { value: "before" },
    });

    // Act.
    rerender({ value: "after" });
    act(() => {
      vi.advanceTimersByTime(200);
    });

    // Assert.
    expect(result.current).toBe("after");
  });

  it("emits nothing when unmounted part-way through the window", () => {
    // Arrange.
    const { rerender, unmount } = renderHook(({ value }) => useDebouncedValue(value, 200), {
      initialProps: { value: "before" },
    });

    // Act.
    rerender({ value: "after" });
    unmount();

    // Assert.
    // Nothing is left to fire: the cleanup cleared the pending timeout, so advancing past
    // the window cannot schedule a state update on an unmounted hook.
    expect(vi.getTimerCount()).toBe(0);
  });
});
