import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { validateDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { canvasPositionOf, monthIndexOf } from "@client/canvas/library/definition/chrome";
import { GHG_DEFINITION } from "./GhgCanvas";
import { GhgScale, formatMonth, monthAt, xOfMonth } from "./ghgIds";

interface ScaleFixture {
  unitsPerMonth: number;
  origin: string;
  trendHeight: number;
  rowStep: number;
  months: { date: string; x: number }[];
  rows: { row: number; top: number; middle: number }[];
}

/** The one statement of the time scale both tiers are held to (task 15 asserts the backend's). */
const FIXTURE = JSON.parse(readFileSync(join(__dirname, "..", "scale-fixture.json"), "utf8")) as ScaleFixture;

describe("the hype cycle graph's definition", () => {
  it("passes the library's own validation", () => {
    expect(validateDiagramDefinition(GHG_DEFINITION)).toEqual([]);
  });

  it("draws a trend as a phased arrow banner, its name before it, attached anywhere along a phase", () => {
    // Assert: the design's element type, declaration by declaration.
    const [trend] = GHG_DEFINITION.elementTypes;
    expect(GHG_DEFINITION.elementTypes).toHaveLength(1);
    expect(trend.shape).toBe("arrow-banner");
    expect(trend.sizing).toBe("user");
    expect(trend.resize ?? "width").toBe("width");
    expect(trend.segments).toMatchObject({
      max: 4,
      count: { path: "payload.phases" },
      boundaries: "payload.boundaries",
      divider: "chevron",
      draggableBoundaries: true,
      classNames: ["ghg-peak", "ghg-trough", "ghg-slope", "ghg-plateau"],
      tooltips: ["Peak of Inflated Expectations", "Trough of Disillusionment", "Slope of Enlightenment", "Plateau of Productivity"],
    });
    expect(trend.labels).toHaveLength(1);
    expect(trend.labels![0]).toMatchObject({ text: { path: "payload.name" }, placement: "before", editable: true });
    expect(trend.anchors).toEqual({ kind: "along", edges: ["top", "bottom"], regions: "segments", visible: false });
  });

  it("allows one influence each way, never to itself, hidden with the phase it attaches to", () => {
    const [influence] = GHG_DEFINITION.relationTypes;
    expect(GHG_DEFINITION.relationTypes).toHaveLength(1);
    expect(influence.route).toBe("cubic-bezier");
    expect(influence.style).toEqual({ endMarker: "arrow" });
    expect(influence.endpoints.allowSelf).toBe(false);
    expect(influence.endpoints.cardinality).toEqual({ perPair: "ordered" });
    expect(influence.hideWhenAttachmentHidden).toBe(true);
  });

  it("snaps to whole months and rows, rules the bottom in months, filters on tags, lays out by hand", () => {
    expect(GHG_DEFINITION.snap).toEqual({ x: { step: 4, origin: 0 }, y: { step: 56 } });
    expect(GHG_DEFINITION.chrome?.rulers).toHaveLength(1);
    expect(GHG_DEFINITION.chrome!.rulers![0]).toMatchObject({ edge: "bottom", scale: { unit: "month", unitsPerStep: 4, origin: "1900-01" } });
    expect(GHG_DEFINITION.filter).toMatchObject({ field: "payload.tags" });
    expect(GHG_DEFINITION.layout.modes).toEqual(["manual"]);
  });
});

describe("the client's time scale agrees with the checked-in fixture", () => {
  it("states the fixture's constants", () => {
    expect({ unitsPerMonth: GhgScale.unitsPerMonth, origin: GhgScale.origin, trendHeight: GhgScale.trendHeight, rowStep: GhgScale.rowStep })
      .toEqual({ unitsPerMonth: FIXTURE.unitsPerMonth, origin: FIXTURE.origin, trendHeight: FIXTURE.trendHeight, rowStep: FIXTURE.rowStep });
  });

  it.each(FIXTURE.months)("puts $date at x $x, through the module and through the ruler, and back", ({ date, x }) => {
    // The module's own conversion, which its handlers send.
    const month = monthIndexOf(date)!;
    expect(xOfMonth(month)).toBe(x);
    expect(formatMonth(monthAt(x))).toBe(date);
    // The ruler's, which reads the declaration rather than this module's code.
    expect(canvasPositionOf(GHG_DEFINITION.chrome!.rulers![0], { element: { id: "c", type: "c", x: 0, y: 0 } }, month)).toBe(x);
  });

  it.each(FIXTURE.rows)("rests row $row's top at $top, so its middle is at $middle", ({ row, top, middle }) => {
    expect(row * GhgScale.rowStep).toBe(top);
    expect(top + GhgScale.trendHeight / 2).toBe(middle);
  });
});
