import { describe, expect, it, vi } from "vitest";
import { shownRectOf, viewReportOf, VIEW_REPORT_DEBOUNCE_MS, type ViewBox } from "./viewReport";

/** A surface of a given size, which is all `shownRectOf` asks of one. */
function surfaceOf(width: number, height: number): SVGSVGElement {
  return { getBoundingClientRect: () => ({ width, height }) } as unknown as SVGSVGElement;
}

describe("shownRectOf", () => {
  it("reports the bare box when there is no surface to measure", () => {
    // Arrange.
    const box: ViewBox = { x: 10, y: 20, w: 100, h: 50 };

    // Act.
    const shown = shownRectOf(box);

    // Assert.
    // This is the case that pins the behaviour of a canvas driving a bare viewBox: before this
    // was shared, such a canvas had its own one-argument version returning exactly this. If the
    // no-surface branch ever stops equalling the bare box, those canvases change silently.
    expect(shown).toEqual({ minX: 10, minY: 20, maxX: 110, maxY: 70 });
  });

  it("reports the bare box for a null surface and for one that has not been laid out", () => {
    // Arrange.
    const box: ViewBox = { x: 0, y: 0, w: 4, h: 4 };
    const bare = { minX: 0, minY: 0, maxX: 4, maxY: 4 };

    // Act and assert.
    // jsdom gives every element a zero-sized rect, so this is the branch the whole suite runs in.
    expect(shownRectOf(box, null)).toEqual(bare);
    expect(shownRectOf(box, surfaceOf(0, 0))).toEqual(bare);
  });

  it("reports the bare box for a degenerate view rather than dividing by it", () => {
    // Arrange, act and assert.
    expect(shownRectOf({ x: 1, y: 2, w: 0, h: 10 }, surfaceOf(100, 100))).toEqual({ minX: 1, minY: 2, maxX: 1, maxY: 12 });
    expect(shownRectOf({ x: 1, y: 2, w: 10, h: -5 }, surfaceOf(100, 100))).toEqual({ minX: 1, minY: 2, maxX: 11, maxY: -3 });
  });

  it("widens the axis with room to spare, because that is what the browser draws", () => {
    // Arrange.
    // A square box in a surface twice as wide as it is tall. "meet" fits by the tighter axis -
    // height - and centres, so twice the width is on screen and the height is unchanged.
    const box: ViewBox = { x: 0, y: 0, w: 100, h: 100 };

    // Act.
    const shown = shownRectOf(box, surfaceOf(400, 200));

    // Assert.
    // Reporting the bare box here would understate the visible area by half, and the backend
    // would cull elements sitting either side of centre while the reader looks straight at them.
    expect(shown).toEqual({ minX: -50, minY: 0, maxX: 150, maxY: 100 });
  });

  it("keeps the centre fixed whichever axis is the roomy one", () => {
    // Arrange.
    const box: ViewBox = { x: 20, y: 60, w: 40, h: 40 };

    // Act.
    const wide = shownRectOf(box, surfaceOf(400, 100));
    const tall = shownRectOf(box, surfaceOf(100, 400));

    // Assert.
    for (const shown of [wide, tall]) {
      expect((shown.minX + shown.maxX) / 2).toBeCloseTo(40);
      expect((shown.minY + shown.maxY) / 2).toBeCloseTo(80);
    }
    expect(wide.maxX - wide.minX).toBeGreaterThan(wide.maxY - wide.minY);
    expect(tall.maxY - tall.minY).toBeGreaterThan(tall.maxX - tall.minX);
  });
});

describe("viewReportOf", () => {
  const projectId = new Uint8Array([1, 2]);
  const watchId = new Uint8Array([3, 4]);

  it("sends the viewport as a centre and a bounding box, correlated to the stream", () => {
    // Arrange.
    const updateView = vi.fn().mockResolvedValue({});
    const report = viewReportOf({ updateView } as never, projectId, watchId, ["a", "b.adp"]);

    // Act.
    report({ minX: 0, minY: 10, maxX: 40, maxY: 30 });

    // Assert.
    // watchId and path are what pair this unary call to the open stream; a report that loses
    // them reaches the backend and applies to nothing.
    expect(updateView).toHaveBeenCalledTimes(1);
    expect(updateView).toHaveBeenCalledWith({
      projectId: { value: projectId },
      watchId: { value: watchId },
      path: { segments: ["a", "b.adp"] },
      view: {
        center: { x: 20, y: 20 },
        boundingBox: { min: { x: 0, y: 10 }, max: { x: 40, y: 30 } },
      },
    });
  });

  it("copies the path rather than passing the caller's array through", () => {
    // Arrange.
    const updateView = vi.fn().mockResolvedValue({});
    const path = ["a", "b.adp"];
    const report = viewReportOf({ updateView } as never, projectId, watchId, path);

    // Act.
    report({ minX: 0, minY: 0, maxX: 1, maxY: 1 });
    path.push("mutated");

    // Assert.
    expect(updateView.mock.calls[0][0].path.segments).toEqual(["a", "b.adp"]);
  });

  it("swallows a failed report instead of surfacing it to the reader", async () => {
    // Arrange.
    const updateView = vi.fn().mockRejectedValue(new Error("the connection went away"));
    const report = viewReportOf({ updateView } as never, projectId, watchId, ["a.adp"]);
    const unhandled = vi.fn();
    process.on("unhandledRejection", unhandled);

    // Act.
    report({ minX: 0, minY: 0, maxX: 1, maxY: 1 });
    await new Promise((resolve) => setTimeout(resolve, 0));
    process.off("unhandledRejection", unhandled);

    // Assert.
    // A view report is advisory: the backend keeps the last window it had, so a dropped one
    // costs nothing and must never reach the reader as an error.
    expect(unhandled).not.toHaveBeenCalled();
  });
});

describe("the debounce interval", () => {
  it("is written once, here", () => {
    // Arrange, act and assert.
    // Four modules each carried their own copy of this number before it was shared.
    expect(VIEW_REPORT_DEBOUNCE_MS).toBe(200);
  });
});
