import { describe, expect, it } from "vitest";
import { indexesOf, sameWindow, spacersOf, windowOf } from "./windowing";

const base = { rowHeight: 30, rowCount: 10_000, margin: 5 };

describe("windowOf", () => {
  it("is the rows in sight and the margin below them at the top", () => {
    // Act: 300 pixels show rows 0 to 9.
    const window = windowOf({ ...base, scrollTop: 0, viewportHeight: 300 });

    // Assert: nothing above row 0 to keep ready.
    expect(window).toEqual({ first: 0, count: 15 });
  });

  it("widens by the margin on both sides in the middle", () => {
    // Act: scrolled to row 100 exactly; rows 100 to 109 are in sight.
    const window = windowOf({ ...base, scrollTop: 3000, viewportHeight: 300 });

    // Assert.
    expect(window).toEqual({ first: 95, count: 20 });
  });

  it("counts a row that is partly in sight", () => {
    // Act: 10 pixels into row 100, so row 110 shows its first 10 pixels.
    const window = windowOf({ ...base, scrollTop: 3010, viewportHeight: 300 });

    // Assert.
    expect(window).toEqual({ first: 95, count: 21 });
  });

  it("stops at the last row", () => {
    // Act: scrolled to the very end.
    const window = windowOf({ ...base, scrollTop: 10_000 * 30 - 300, viewportHeight: 300 });

    // Assert.
    expect(window.first).toBe(9985);
    expect(window.first + window.count).toBe(10_000);
  });

  it("is the whole view when the view is shorter than the window", () => {
    // Act.
    const window = windowOf({ ...base, rowCount: 3, scrollTop: 0, viewportHeight: 300 });

    // Assert.
    expect(window).toEqual({ first: 0, count: 3 });
  });

  it("stays inside the view when the scroll offset is past its end", () => {
    // Act: the view shrank under a scroll position that no longer exists.
    const window = windowOf({ ...base, rowCount: 20, scrollTop: 50_000, viewportHeight: 300 });

    // Assert: the last row, and nothing beyond.
    expect(window).toEqual({ first: 19, count: 1 });
  });

  it("is empty for a view with no rows", () => {
    expect(windowOf({ ...base, rowCount: 0, scrollTop: 0, viewportHeight: 300 })).toEqual({ first: 0, count: 0 });
  });

  it("never grows with the number of rows", () => {
    // Act.
    const small = windowOf({ ...base, rowCount: 1_000, scrollTop: 600, viewportHeight: 300 });
    const large = windowOf({ ...base, rowCount: 1_000_000, scrollTop: 600, viewportHeight: 300 });

    // Assert: what is drawn depends on what is in sight, not on how much there is.
    expect(large).toEqual(small);
  });
});

describe("spacersOf", () => {
  it("leaves the room of the rows that are not drawn", () => {
    // Act.
    const spacers = spacersOf({ first: 95, count: 20 }, 10_000, 30);

    // Assert: the three parts add up to the whole view.
    expect(spacers).toEqual({ before: 95 * 30, after: (10_000 - 115) * 30 });
    expect(spacers.before + 20 * 30 + spacers.after).toBe(10_000 * 30);
  });

  it("leaves nothing after the last row", () => {
    expect(spacersOf({ first: 9985, count: 15 }, 10_000, 30).after).toBe(0);
  });
});

describe("sameWindow and indexesOf", () => {
  it("compares windows by their rows", () => {
    expect(sameWindow({ first: 5, count: 10 }, { first: 5, count: 10 })).toBe(true);
    expect(sameWindow({ first: 5, count: 10 }, { first: 6, count: 10 })).toBe(false);
    expect(sameWindow({ first: 5, count: 10 }, { first: 5, count: 11 })).toBe(false);
  });

  it("lists a window's indexes in order", () => {
    expect(indexesOf({ first: 7, count: 3 })).toEqual([7, 8, 9]);
    expect(indexesOf({ first: 0, count: 0 })).toEqual([]);
  });
});
