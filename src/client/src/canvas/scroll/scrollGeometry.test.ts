import { describe, expect, it } from "vitest";
import { scrollExtentOf, thumbOf, type ScrollAxis } from "./scrollGeometry";

/** An axis by its four numbers, in whatever units a test wants to think in. */
function axis(viewStart: number, viewSpan: number, extentStart: number, extentEnd: number): ScrollAxis {
  return { viewStart, viewSpan, extentStart, extentEnd };
}

// The timeline's own constants, restated here rather than imported: shared canvas code must
// not depend on a diagram module, and these two numbers are what the reproduction assertion
// below is about.
const DAY = 86400;
const ROW_HEIGHT = 60;

describe("thumbOf", () => {
  it("places a view inside its extent as fractions of the track", () => {
    // Act.
    const thumb = thumbOf(axis(25, 50, 0, 100));

    // Assert.
    expect(thumb.size).toBe(0.5);
    expect(thumb.offset).toBe(0.25);
  });

  it("claims the whole track when the view is wider than its extent", () => {
    // Act.
    const thumb = thumbOf(axis(-100, 400, 0, 100));

    // Assert.
    // Requirement 1.4: a map that fits shows a bar that invites no pan.
    expect(thumb.size).toBe(1);
    expect(thumb.offset).toBe(0);
  });

  it("never draws a thumb smaller than five percent of the track", () => {
    // Act.
    const thumb = thumbOf(axis(0, 1, 0, 1_000_000));

    // Assert.
    expect(thumb.size).toBe(0.05);
  });

  it("clamps the offset at the start when the view sits before the extent", () => {
    // Act.
    const thumb = thumbOf(axis(-500, 50, 0, 100));

    // Assert.
    expect(thumb.offset).toBe(0);
  });

  it("clamps the offset at the end when the view sits past the extent", () => {
    // Act.
    // A canvas dragged beyond its margin: the thumb reaches the end of the track and stops
    // there, without forcing the view back.
    const thumb = thumbOf(axis(500, 50, 0, 100));

    // Assert.
    expect(thumb.offset).toBe(0.5);
    expect(thumb.offset + thumb.size).toBe(1);
  });

  it("survives a degenerate extent without dividing by zero", () => {
    // Act.
    const thumb = thumbOf(axis(10, 10, 10, 10));

    // Assert.
    expect(Number.isFinite(thumb.size)).toBe(true);
    expect(Number.isFinite(thumb.offset)).toBe(true);
    expect(thumb.size).toBe(1);
  });
});

describe("scrollExtentOf", () => {
  it("adds no margin by default", () => {
    // Act and assert.
    expect(scrollExtentOf(10, 90)).toEqual({ extentStart: 10, extentEnd: 90 });
  });

  it("adds a proportional margin, with a floor on the span it is proportional to", () => {
    // Act.
    // Content narrower than the minimum span is padded as if it were that wide, so a single
    // element still has room either side of it.
    const narrow = scrollExtentOf(0, 10, { factor: 0.5, minimumSpan: 100 });
    const wide = scrollExtentOf(0, 400, { factor: 0.5, minimumSpan: 100 });

    // Assert.
    expect(narrow).toEqual({ extentStart: -50, extentEnd: 60 });
    expect(wide).toEqual({ extentStart: -200, extentEnd: 600 });
  });

  it("adds a fixed margin regardless of content", () => {
    // Act.
    const extent = scrollExtentOf(100, 160, { factor: 0, minimum: 120 });

    // Assert.
    expect(extent).toEqual({ extentStart: -20, extentEnd: 280 });
  });

  it("uses the larger of the proportional and the fixed margin", () => {
    // Act.
    const small = scrollExtentOf(0, 10, { factor: 0.5, minimum: 30 });
    const large = scrollExtentOf(0, 1000, { factor: 0.5, minimum: 30 });

    // Assert.
    expect(small).toEqual({ extentStart: -30, extentEnd: 40 });
    expect(large).toEqual({ extentStart: -500, extentEnd: 1500 });
  });

  it("reproduces the timeline's horizontal extent term for term", () => {
    // Arrange.
    // What TimelineScrollbars computed before the extraction, written out as it was:
    //   timeSpan    = max(maxSeconds - minSeconds, DAY)
    //   extentStart = minSeconds - timeSpan * 0.5
    //   extentEnd   = maxSeconds + timeSpan * 0.5
    const minSeconds = 3 * DAY;
    const maxSeconds = 10 * DAY;
    const timeSpan = Math.max(maxSeconds - minSeconds, DAY);

    // Act.
    const extent = scrollExtentOf(minSeconds, maxSeconds, { factor: 0.5, minimumSpan: DAY });

    // Assert.
    // Requirement 1.6: the extraction carries the timeline's reasoning - margin included - and
    // changes none of its numbers.
    expect(extent.extentStart).toBe(minSeconds - timeSpan * 0.5);
    expect(extent.extentEnd).toBe(maxSeconds + timeSpan * 0.5);
  });

  it("reproduces the timeline's horizontal extent for a single-day span", () => {
    // Arrange.
    // The branch the minimum span exists for: one element, one day wide.
    const minSeconds = 5 * DAY;
    const maxSeconds = 6 * DAY;

    // Act.
    const extent = scrollExtentOf(minSeconds, maxSeconds, { factor: 0.5, minimumSpan: DAY });

    // Assert.
    expect(extent.extentStart).toBe(minSeconds - DAY * 0.5);
    expect(extent.extentEnd).toBe(maxSeconds + DAY * 0.5);
  });

  it("reproduces the timeline's vertical extent term for term", () => {
    // Arrange.
    //   yExtentStart = minY - 2 * ROW_HEIGHT
    //   yExtentEnd   = maxY + 2 * ROW_HEIGHT
    const minY = 0;
    const maxY = 4 * ROW_HEIGHT;

    // Act.
    const extent = scrollExtentOf(minY, maxY, { factor: 0, minimum: 2 * ROW_HEIGHT });

    // Assert.
    expect(extent.extentStart).toBe(minY - 2 * ROW_HEIGHT);
    expect(extent.extentEnd).toBe(maxY + 2 * ROW_HEIGHT);
  });
});
