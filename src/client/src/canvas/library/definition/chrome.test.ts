import { describe, expect, it } from "vitest";
import type { DiagramModelElement } from "../api/diagramModel";
import type { BindingSource } from "./binding";
import {
  DAY,
  HOUR,
  MINUTE,
  resolveChromeText,
  resolveLegend,
  resolveTicks,
  rulerRangeOf,
  type RulerDeclaration,
} from "./chrome";

const element: DiagramModelElement = { id: "__chrome__", type: "__chrome__", x: 0, y: 0 };
const source = (payload?: unknown): BindingSource => ({ element, payload });

describe("chrome — the states two modules should show identically", () => {
  it("resolves a bound message", () => {
    const resolved = resolveChromeText({ text: { template: "Reading {payload.subject}…" } }, source({ subject: "the solution" }));
    expect(resolved?.text).toBe("Reading the solution…");
  });

  it("shows nothing when a condition fails, so absence is declarable", () => {
    // Requirement 6.2: a module wanting none declares none, and that is a statement rather than
    // an omission - the same distinction `actions` draws for a missing rename.
    expect(resolveChromeText({ text: { path: "payload.t" }, when: { path: "payload.failed", is: "true" } }, source({ t: "x", failed: false }))).toBeNull();
    expect(resolveChromeText(undefined, source({}))).toBeNull();
  });

  it("builds a legend entry per model collection entry, with its own swatch", () => {
    const resolved = resolveLegend(
      { entries: { path: "payload.kinds", each: { path: "caption" } }, swatchClass: { path: "payload.kinds", each: { path: "swatch" } } },
      source({ kinds: [{ caption: "Project", swatch: "kind-project" }, { caption: "Package", swatch: "kind-package" }] }),
    );

    expect(resolved).toEqual([
      { caption: "Project", swatchClass: "kind-project" },
      { caption: "Package", swatchClass: "kind-package" },
    ]);
  });
});

/**
 * THE RULER, which landed here from task 4 after its mechanism was read rather than its purpose.
 *
 * The ladder is DATA and choosing a rung is MECHANISM - that split is what makes a view-fixed
 * ruler declarable at all, and the alternative is a module supplying a function, which is the
 * escape hatch this specification exists to close.
 */
describe("chrome — a ruler's tick ladder", () => {
  const timelineRuler: RulerDeclaration = {
    orientation: "horizontal",
    unitsPerCanvasUnit: { path: "payload.secondsPerUnit" },
    ladder: [
      { every: MINUTE, label: "HH:mm" },
      { every: HOUR, label: "HH:mm" },
      { every: DAY, label: "d MMM" },
      { every: { calendar: "month" }, label: "MMM" },
      { every: { calendar: "year" }, label: "yyyy" },
    ],
    minSpacingPx: 80,
  };

  const jan2026 = Date.UTC(2026, 0, 1) / 1000;

  it("chooses the coarsest rung that still fits, so labels neither crowd nor vanish", () => {
    // A five-year span in 800px: only the year rung can fit. Five labels rather than six,
    // because 5x365 days crosses the 2028 leap year and lands just short of 1 Jan 2031 - which
    // an exact list catches and a count would not. It caught this test's own arithmetic.
    const ticks = resolveTicks(timelineRuler, source({}), { from: jan2026, to: jan2026 + 5 * 365 * DAY, sizePx: 800 });

    expect(ticks.map((t) => t.label)).toEqual(["2026", "2027", "2028", "2029", "2030"]);
  });

  it("drops to a finer rung as the view narrows, which is what changes with zoom", () => {
    // THE PROPERTY A BACKGROUND CANNOT EXPRESS. The tick SET is derived from the visible range,
    // so the same declaration yields different ticks at different zooms - a fixed list could
    // not, and that is why the ruler is chrome rather than a backdrop.
    const ticks = resolveTicks(timelineRuler, source({}), { from: jan2026, to: jan2026 + 6 * HOUR, sizePx: 800 });

    expect(ticks.length).toBeGreaterThan(2);
    expect(ticks[0]!.label).toMatch(/^\d\d:\d\d$/);
  });

  it("puts calendar ticks on the first of the month rather than every 2,629,746 seconds", () => {
    const ticks = resolveTicks(timelineRuler, source({}), { from: jan2026, to: jan2026 + 100 * DAY, sizePx: 400 });

    expect(ticks.map((t) => t.label)).toEqual(["Jan", "Feb", "Mar", "Apr"]);
    // Each falls on a month boundary in UTC.
    for (const tick of ticks) {
      expect(new Date(tick.at * 1000).getUTCDate()).toBe(1);
    }
  });

  it("labels in UTC, so the same document shows the same ruler on every machine", () => {
    // Timeline takes times at face value with no timezone. A formatter using local time would
    // shift every label by the viewer's offset, and the defect would be invisible to whoever
    // wrote it - they would see the right answer on their own machine.
    //
    // AN HOUR LABEL RATHER THAN A DATE ONE, and that choice is the test. A date probe at
    // midnight UTC only shifts in a NEGATIVE offset: this runner is UTC+2, where 1 Jan stays
    // 1 Jan, so the first version of this test passed against local-time formatting and I only
    // saw it fail by forcing TZ=America/Los_Angeles. An hour label moves under EVERY non-zero
    // offset, so the guard discriminates wherever it runs.
    //
    // AND AN EXACT LIST RATHER THAN toContain, which is the second thing this test had to
    // learn. Local time shifts every label by the SAME amount, so a shifted set still contains
    // the probe: at UTC+1 the 22:00 tick becomes "23:00" and `toContain("23:00")` passed
    // against the defect. A uniform shift is invisible to a membership check by construction.
    //
    // Its remaining limit, stated rather than hidden: on a runner genuinely at UTC nothing here
    // can tell the two apart, because there is no difference to observe.
    const hours = resolveTicks(timelineRuler, source({}), { from: jan2026 + 22 * HOUR, to: jan2026 + 26 * HOUR, sizePx: 400 });

    expect(hours.map((t) => t.label)).toEqual(["22:00", "23:00", "00:00", "01:00", "02:00"]);
    const dates = resolveTicks(timelineRuler, source({}), { from: jan2026, to: jan2026 + 3 * DAY, sizePx: 600 });
    expect(dates[0]!.label).toBe("1 Jan");
  });

  it("puts fixed ticks on round boundaries rather than wherever the view began", () => {
    const offset = jan2026 + 37 * MINUTE;
    const ticks = resolveTicks(timelineRuler, source({}), { from: offset, to: offset + 6 * HOUR, sizePx: 800 });

    expect(ticks[0]!.at % HOUR).toBe(0);
  });

  it("yields nothing for a degenerate range rather than looping", () => {
    expect(resolveTicks(timelineRuler, source({}), { from: 0, to: 0, sizePx: 800 })).toEqual([]);
    expect(resolveTicks(timelineRuler, source({}), { from: 0, to: 100, sizePx: 0 })).toEqual([]);
  });

  it("converts a viewport into the ruler's own units through the declared scale", () => {
    const range = rulerRangeOf(timelineRuler, source({ secondsPerUnit: 3600 }), { start: 2, size: 4 });
    expect(range).toEqual({ from: 7200, to: 21600 });
  });
});
