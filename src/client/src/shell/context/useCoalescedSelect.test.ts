import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useCoalescedSelect } from "./useCoalescedSelect";

describe("useCoalescedSelect", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("sends a burst of plain selections once, with the last value, after the delay", () => {
    const send = vi.fn();
    const { result } = renderHook(() => useCoalescedSelect<string>(send, 80));

    act(() => {
      result.current("a", false);
      result.current("b", false);
      result.current("c", false);
    });
    expect(send).not.toHaveBeenCalled();

    act(() => {
      vi.advanceTimersByTime(80);
    });
    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith("c");
  });

  it("sends an immediate value at once and drops the pending plain one", () => {
    const send = vi.fn();
    const { result } = renderHook(() => useCoalescedSelect<string>(send, 80));

    act(() => {
      result.current("plain", false);
      result.current("gesture", true);
    });
    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith("gesture");

    act(() => {
      vi.advanceTimersByTime(200);
    });
    expect(send).toHaveBeenCalledTimes(1);
  });

  it("sends an immediate null (a clear) at once", () => {
    const send = vi.fn();
    const { result } = renderHook(() => useCoalescedSelect<string | null>(send, 80));

    act(() => {
      result.current(null, true);
    });

    expect(send).toHaveBeenCalledWith(null);
  });

  it("sends nothing when unmounted inside the window", () => {
    const send = vi.fn();
    const { result, unmount } = renderHook(() => useCoalescedSelect<string>(send, 80));

    act(() => {
      result.current("a", false);
    });
    unmount();
    act(() => {
      vi.advanceTimersByTime(200);
    });

    expect(send).not.toHaveBeenCalled();
  });

  it("keeps a stable function identity across renders", () => {
    const { result, rerender } = renderHook(() => useCoalescedSelect<string>(() => {}, 80));
    const first = result.current;

    rerender();

    expect(result.current).toBe(first);
  });
});
