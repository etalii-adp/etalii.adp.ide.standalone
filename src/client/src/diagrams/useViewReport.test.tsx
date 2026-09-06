import { act, render } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useViewReport } from "./useViewReport";
import { VIEW_REPORT_DEBOUNCE_MS, type ViewBox, type Viewport } from "./viewReport";

/** A host that does nothing but run the hook, so the tests measure the hook and not a canvas. */
function Host({
  view,
  report,
  convert,
  ready = true,
}: {
  view: ViewBox;
  report: (viewport: Viewport) => void;
  convert?: () => Viewport;
  ready?: boolean;
}) {
  useViewReport({
    view,
    report,
    convert: convert ?? (() => ({ minX: view.x, minY: view.y, maxX: view.x + view.w, maxY: view.y + view.h })),
    ready,
  });
  return null;
}

describe("useViewReport", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const settle = () => act(() => void vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS));

  it("reports the view once it settles", () => {
    // Arrange.
    const report = vi.fn();
    render(<Host view={{ x: 0, y: 0, w: 10, h: 10 }} report={report} />);

    // Act.
    settle();

    // Assert.
    expect(report).toHaveBeenCalledExactlyOnceWith({ minX: 0, minY: 0, maxX: 10, maxY: 10 });
  });

  it("reports once for a burst of changes, not once per change", () => {
    // Arrange.
    const report = vi.fn();
    const { rerender } = render(<Host view={{ x: 0, y: 0, w: 10, h: 10 }} report={report} />);

    // Act.
    // A pan produces a view change per frame. Advancing less than the interval between them is
    // what a drag looks like from here.
    for (const x of [1, 2, 3, 4, 5]) {
      rerender(<Host view={{ x, y: 0, w: 10, h: 10 }} report={report} />);
      act(() => void vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS / 4));
    }
    settle();

    // Assert.
    // "At least one" would pass against a debounce that does not debounce, which is the whole
    // failure this test exists for: the backend becoming a participant in the gesture.
    expect(report).toHaveBeenCalledTimes(1);
    expect(report).toHaveBeenCalledWith({ minX: 5, minY: 0, maxX: 15, maxY: 10 });
  });

  it("reports again when the view changes, not only at open", () => {
    // Arrange.
    const report = vi.fn();
    const { rerender } = render(<Host view={{ x: 0, y: 0, w: 10, h: 10 }} report={report} />);
    settle();

    // Act.
    rerender(<Host view={{ x: 40, y: 0, w: 10, h: 10 }} report={report} />);
    settle();

    // Assert.
    // The behavioural definition of the whole mechanism: a module that reports only at open has
    // implemented the first frame of it, not the loop.
    expect(report).toHaveBeenCalledTimes(2);
    expect(report).toHaveBeenLastCalledWith({ minX: 40, minY: 0, maxX: 50, maxY: 10 });
  });

  it("reports on a zoom, not only on a pan", () => {
    // Arrange.
    const report = vi.fn();
    const { rerender } = render(<Host view={{ x: 0, y: 0, w: 10, h: 10 }} report={report} />);
    settle();

    // Act.
    rerender(<Host view={{ x: 0, y: 0, w: 20, h: 20 }} report={report} />);
    settle();

    // Assert.
    // The key is built from all four numbers; one built from x and y alone would pass every
    // test above and silently never report a zoom.
    expect(report).toHaveBeenCalledTimes(2);
    expect(report).toHaveBeenLastCalledWith({ minX: 0, minY: 0, maxX: 20, maxY: 20 });
  });

  it("does not report while the diagram is loading or has failed", () => {
    // Arrange.
    const report = vi.fn();
    const { rerender } = render(<Host view={{ x: 0, y: 0, w: 10, h: 10 }} report={report} ready={false} />);

    // Act.
    settle();
    rerender(<Host view={{ x: 5, y: 0, w: 10, h: 10 }} report={report} ready={false} />);
    settle();

    // Assert.
    // A report before the first delta describes a view of nothing; a report after a permanent
    // failure is a call to a connection that has just been told its path is gone.
    expect(report).not.toHaveBeenCalled();
  });

  // This test is insensitive to WHEN the first report arrives, and that is worth knowing before
  // editing it. It advances the full debounce before asserting, so it passes whether the report
  // fires immediately on becoming ready or 200ms later - and `useViewReport` currently makes it
  // wait, because the effect sets one unconditional timer for every reason it runs, the readiness
  // transition included. There is nothing to coalesce at open: one view, reported once.
  //
  // So it is not wrong and it is not redundant, but it cannot serve as the guard for first-report
  // latency. A test for that must assert the report has arrived with NO timer advanced at all.
  // Keep both if that guard is ever written; deleting this one as duplicated would lose the
  // becoming-ready reason, which is the thing it does pin.
  it("reports once the diagram becomes ready", () => {
    // Arrange.
    const report = vi.fn();
    const view = { x: 0, y: 0, w: 10, h: 10 };
    const { rerender } = render(<Host view={view} report={report} ready={false} />);
    settle();

    // Act.
    rerender(<Host view={view} report={report} ready={true} />);
    settle();

    // Assert.
    // Becoming ready is itself a reason to report: the view has not moved, but nothing has been
    // told about it yet, and a canvas that waits for a pan would open showing nothing.
    expect(report).toHaveBeenCalledTimes(1);
  });

  it("does not re-report merely because the report or conversion was rebuilt", () => {
    // Arrange.
    const view = { x: 0, y: 0, w: 10, h: 10 };
    const report = vi.fn();
    const { rerender } = render(<Host view={view} report={report} />);
    settle();

    // Act.
    // A module's reportView is rebuilt every render. Without the ref indirection this re-fires
    // the effect on every render and the debounce protects nothing.
    for (let i = 0; i < 3; i++) {
      rerender(<Host view={{ ...view }} report={(viewport) => report(viewport)} convert={() => ({ minX: 0, minY: 0, maxX: 10, maxY: 10 })} />);
      settle();
    }

    // Assert.
    expect(report).toHaveBeenCalledTimes(1);
  });

  it("converts when the timer fires, so it reads current state rather than render-time state", () => {
    // Arrange.
    const report = vi.fn();
    let live: Viewport = { minX: 0, minY: 0, maxX: 1, maxY: 1 };
    render(<Host view={{ x: 0, y: 0, w: 10, h: 10 }} report={report} convert={() => live} />);

    // Act.
    // This is how a canvas reaches its live view and its measured surface: the conversion runs
    // at fire time against refs, not at render time against a captured value.
    live = { minX: 7, minY: 7, maxX: 8, maxY: 8 };
    settle();

    // Assert.
    expect(report).toHaveBeenCalledExactlyOnceWith({ minX: 7, minY: 7, maxX: 8, maxY: 8 });
  });

  it("sends nothing after the canvas is gone", () => {
    // Arrange.
    const report = vi.fn();
    const { unmount } = render(<Host view={{ x: 0, y: 0, w: 10, h: 10 }} report={report} />);

    // Act.
    unmount();
    settle();

    // Assert.
    expect(report).not.toHaveBeenCalled();
  });
});
