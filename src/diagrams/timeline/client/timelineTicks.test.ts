import { describe, expect, it } from "vitest";
import { ticksFor } from "./timelineTicks";

const DAY = 86400;
const AT = Date.UTC(2026, 0, 1) / 1000;

describe("the ruler's ladder", () => {
  it("stays legible across the whole zoom range, not at one sample", () => {
    // Arrange.
    // The requirement the spec flags as most likely to be got wrong (5.4): every span from a
    // minute across the view to a century must yield labels that neither crowd nor vanish.
    const widthPx = 1200;
    const spans = [
      60, // a minute across the view
      3600,
      DAY,
      7 * DAY,
      30 * DAY,
      365 * DAY,
      10 * 365 * DAY,
      100 * 365 * DAY,
    ];

    for (const span of spans) {
      // Act.
      const ticks = ticksFor(AT, AT + span, widthPx);

      // Assert.
      expect(ticks.length, `span ${span}s should still have labels`).toBeGreaterThan(0);
      expect(ticks.length, `span ${span}s should not crowd`).toBeLessThanOrEqual(widthPx / 60);
      for (let i = 1; i < ticks.length; i++) {
        const spacingPx = (ticks[i].seconds - ticks[i - 1].seconds) / (span / widthPx);
        expect(spacingPx, `span ${span}s labels should not collide`).toBeGreaterThanOrEqual(40);
      }
    }
  });

  it("puts labels on round boundaries, not at viewport offsets", () => {
    // Arrange.
    // A view starting at an arbitrary moment (Requirement 5.5): the first day-tick is the next
    // midnight, not "start plus a day".
    const arbitrary = AT + 5 * 3600 + 1234;

    // Act.
    const ticks = ticksFor(arbitrary, arbitrary + 10 * DAY, 1200);

    // Assert.
    for (const tick of ticks) {
      expect(tick.seconds % DAY, `${tick.label} should sit on a midnight`).toBe(0);
    }
  });

  it("slides rather than renumbers when the view pans", () => {
    // Arrange & act.
    // Two overlapping views: the ticks in the overlap must be identical, which is what makes
    // panning look like sliding labels rather than relabelling (Requirement 5.3).
    const first = ticksFor(AT, AT + 30 * DAY, 1200);
    const second = ticksFor(AT + 5 * DAY, AT + 35 * DAY, 1200);
    const overlapFirst = first.filter((tick) => tick.seconds >= AT + 5 * DAY);
    const overlapSecond = second.filter((tick) => tick.seconds <= AT + 30 * DAY);

    // Assert.
    expect(overlapFirst).toEqual(overlapSecond);
  });

  it("walks the calendar for months, so 'the 1st' is a boundary", () => {
    // Act.
    const ticks = ticksFor(AT, AT + 365 * DAY, 1200);

    // Assert.
    for (const tick of ticks) {
      expect(new Date(tick.seconds * 1000).getUTCDate(), `${tick.label} should sit on a 1st`).toBe(1);
    }
  });

  it("answers nothing for a degenerate view", () => {
    // Act & assert.
    expect(ticksFor(AT, AT, 1200)).toEqual([]);
    expect(ticksFor(AT, AT + DAY, 0)).toEqual([]);
  });
});
