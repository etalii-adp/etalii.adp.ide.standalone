import { describe, expect, it } from "vitest";
import { wavyUnderlineMetrics, wavyUnderlinePath, type WavyUnderlineAt } from "./wavyUnderline";

/** Every coordinate pair of a path, in order: the path is written as absolute `M`, `Q`, `L` and `Z` only. */
function pointsOf(d: string): { x: number; y: number }[] {
  const numbers = (d.match(/-?\d+(\.\d+)?/g) ?? []).map(Number);
  const points: { x: number; y: number }[] = [];
  for (let index = 0; index + 1 < numbers.length; index += 2) {
    points.push({ x: numbers[index]!, y: numbers[index + 1]! });
  }
  return points;
}

/** The END point of every segment - where the curve actually passes, as opposed to a control point. */
function onCurve(d: string): { x: number; y: number }[] {
  return d
    .split(/(?=[MQLZ])/)
    .filter((part) => part[0] !== "Z")
    .map((part) => pointsOf(part).at(-1)!);
}

const under = (at: Partial<WavyUnderlineAt> = {}): string =>
  wavyUnderlinePath({ x: 100, baseline: 50, width: 40, anchor: "middle", fontSize: 12, ...at });

describe("wavyUnderlinePath - a wavy line drawn under SVG text", () => {
  it("spans exactly the text's width, from where its anchor puts its left edge", () => {
    // A line wider or narrower than its text marks a different stretch than the one it means.
    for (const [anchor, left] of [["start", 100], ["middle", 80], ["end", 60]] as const) {
      const xs = onCurve(under({ anchor })).map((point) => point.x);

      expect(Math.min(...xs), anchor).toBeCloseTo(left, 5);
      expect(Math.max(...xs), anchor).toBeCloseTo(left + 40, 5);
    }
  });

  it("is a wave: its control points alternate above and below its centre line", () => {
    // A straight underline is exactly what Chromium already draws from `underline wavy`, and the
    // defect this exists for. Control points of a straight line would all sit on it.
    const d = under();
    const controls = d.split(/(?=[MQLZ])/).filter((part) => part[0] === "Q").map((part) => pointsOf(part)[0]!);
    const upper = controls.slice(0, controls.length / 2);
    const ends = onCurve(d);
    const edge = ends[0]!.y;

    expect(upper.length).toBeGreaterThanOrEqual(2);
    upper.forEach((control, index) => {
      expect(Math.sign(control.y - edge), `crest ${index}`).toBe(index % 2 === 0 ? -1 : 1);
    });
  });

  it("sits below the baseline, so it is under the text and not through it", () => {
    const ys = pointsOf(under()).map((point) => point.y);
    const { amplitude } = wavyUnderlineMetrics(12);

    // The curve reaches half way to a control point, so the highest the ribbon goes is one
    // amplitude above its upper edge - still below the baseline.
    const upperEdge = onCurve(under())[0]!.y;
    expect(upperEdge - amplitude).toBeGreaterThan(50);
    expect(Math.max(...ys)).toBeLessThan(50 + 12);
  });

  it("is a closed ribbon with thickness, so a fill paints it in the label's colour", () => {
    const d = under();
    const ends = onCurve(d);
    const { thickness } = wavyUnderlineMetrics(12);

    expect(d.trim().endsWith("Z")).toBe(true);
    expect(ends.at(-1)!.y - ends[0]!.y).toBeCloseTo(thickness, 5);
    expect(ends.at(-1)!.x).toBeCloseTo(ends[0]!.x, 5);
  });

  it("has more crests for a wider label, at a wavelength that follows the font size", () => {
    const crests = (width: number, fontSize = 12) => (under({ width, fontSize }).match(/Q/g) ?? []).length;

    expect(crests(120)).toBeGreaterThan(crests(40));
    expect(crests(40, 24)).toBeLessThan(crests(40, 12));
    // However short, at least two half waves: one bump reads as an arc, not a wave.
    expect(crests(2)).toBe(4);
  });

  it("draws nothing under text with no width", () => {
    expect(under({ width: 0 })).toBe("");
    expect(under({ width: Number.NaN })).toBe("");
  });
});
