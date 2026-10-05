import { describe, expect, it } from "vitest";
import disText from "../definition/gartner-hype-cycle-graph.dis?raw";
import { parseDisl, type DislCustomShape, type DislShapePart } from "@client/canvas/library/disl/disTypes";
import { evaluateCondition, resolveBox, resolvePath, type GeomScope, type ResolvedSegment } from "@client/canvas/library/disl/geomExpr";
import { outlineOf } from "@client/canvas/library/shapes/outline";
import { segmentLayoutOf } from "@client/canvas/library/shapes/segments";
import type { ShapePoint } from "@client/canvas/library/definition/diagramDefinition";

/**
 * The bundled specification's `phasedBanner` is the library's `arrow-banner` cut into segments.
 *
 * The canvas never reads the custom shape's GeomExprs: `ghgBindings.ts` says the shape is drawn as
 * the library's banner with `segments`, and that is all the canvas sees. This proves the two say the
 * same thing - the outline, each phase's fill, each chevron and each phase's stretch of the top and
 * bottom edges - at a grid of widths, phase counts and boundary fractions, so neither can be changed
 * without the other.
 */

const SPEC = parseDisl(disText);
const BANNER: DislCustomShape = SPEC.notation.shapes!.phasedBanner!;

const HEIGHT = 32;
const WIDTHS = [20, 48, 96, 400];
const FRACTIONS: readonly (readonly [number, number, number])[] = [
  [0.25, 0.5, 0.75],
  [0.1, 0.6, 0.65],
  [0.02, 0.04, 0.98],
  [0.3, 0.5, 0.9],
];

/** The parts by role, read from their structure rather than their names. */
const PARTS = BANNER.parts ?? [];
/** A phase's fill: a path with the phase's tooltip. */
const FILLS = PARTS.filter((part) => typeof part.shape === "object" && part["x-part.tooltip"] !== undefined);
/** A chevron: a path that is neither a fill nor hit. */
const CHEVRONS = PARTS.filter((part) => typeof part.shape === "object" && part["x-part.tooltip"] === undefined);
/** A phase's attachment stretch: a rectangle. */
const STRETCHES = PARTS.filter((part) => part.shape === "rect");

/** Points, rounded so float noise in the last places is not a difference. */
function rounded(points: readonly ShapePoint[]): ShapePoint[] {
  // `+ 0` turns a negative zero into zero, which `toEqual` would otherwise tell apart.
  return points.map((point) => ({ x: Math.round(point.x * 1e9) / 1e9 + 0, y: Math.round(point.y * 1e9) / 1e9 + 0 }));
}

/** A path's vertices: its moves and lines, without the close or a repeated point. */
function verticesOf(path: readonly ResolvedSegment[]): ShapePoint[] {
  const points: ShapePoint[] = [];
  for (const segment of path) {
    if (segment.op === "M" || segment.op === "L") {
      points.push({ x: segment.x, y: segment.y });
    } else if (segment.op === "A") {
      throw new Error("phasedBanner has no arcs; a path with one needs another comparison.");
    }
  }

  return withoutRepeats(points);
}

function withoutRepeats(points: readonly ShapePoint[]): ShapePoint[] {
  const kept = rounded(points).filter((point, index, all) => index === 0 || point.x !== all[index - 1]!.x || point.y !== all[index - 1]!.y);
  const last = kept[kept.length - 1];
  return kept.length > 1 && last!.x === kept[0]!.x && last!.y === kept[0]!.y ? kept.slice(0, -1) : kept;
}

function pathOf(part: DislShapePart, scope: GeomScope): ShapePoint[] {
  if (typeof part.shape !== "object") {
    throw new Error(`Part "${part.id}" has no path.`);
  }

  return verticesOf(resolvePath(part.shape.path.segments, scope));
}

/** Every case of the grid, each with its scope and the library's layout for it. */
const CASES = WIDTHS.flatMap((w) =>
  [1, 2, 3, 4].flatMap((count) =>
    FRACTIONS.map((fractions) => {
      const [b1, b2, b3] = fractions;
      const scope: GeomScope = { w, h: HEIGHT, p: { count, b1, b2, b3 } };
      const bounds = { x: 0, y: 0, width: w, height: HEIGHT };
      return { name: `w ${w}, ${count} phases, at ${fractions.join("/")}`, scope, bounds, layout: segmentLayoutOf(bounds, count, fractions.slice(0, count - 1), "chevron") };
    })));

describe("the specification's phased banner is the library's segmented arrow banner", () => {
  it("finds the parts it compares: four fills, three chevrons and four stretches", () => {
    // The canary: a structure read that found nothing would compare nothing and pass.
    expect([FILLS.length, CHEVRONS.length, STRETCHES.length]).toEqual([4, 3, 4]);
    expect(CASES).toHaveLength(64);
  });

  it("has the library's outline", () => {
    for (const { name, scope, bounds } of CASES) {
      expect(verticesOf(resolvePath(BANNER.outline!.segments, scope)), name).toEqual(withoutRepeats(outlineOf("arrow-banner", bounds)));
    }
  });

  it("fills each drawn phase with the library's segment polygon, and draws no other", () => {
    for (const { name, scope, layout } of CASES) {
      const drawn = FILLS.filter((part) => evaluateCondition(part.when, scope));
      expect(drawn.length, name).toBe(layout.count);
      drawn.forEach((part, index) => {
        expect(pathOf(part, scope), `${name}, ${part.id}`).toEqual(withoutRepeats(layout.segments[index]!.polygon));
      });
    }
  });

  it("draws the library's chevron between each pair of drawn phases", () => {
    for (const { name, scope, layout } of CASES) {
      const drawn = CHEVRONS.filter((part) => evaluateCondition(part.when, scope));
      expect(drawn.length, name).toBe(layout.dividers.length);
      drawn.forEach((part, index) => {
        expect(pathOf(part, scope), `${name}, ${part.id}`).toEqual(withoutRepeats(layout.dividers[index]!.points));
      });
    }
  });

  it("gives each drawn phase the library's stretch of the top and bottom edges", () => {
    for (const { name, scope, layout } of CASES) {
      const drawn = STRETCHES.filter((part) => evaluateCondition(part.when, scope));
      expect(drawn.length, name).toBe(layout.count);
      drawn.forEach((part, index) => {
        const box = resolveBox(part.box!, scope);
        const segment = layout.segments[index]!;
        const near = (value: number) => Math.round(value * 1e9) / 1e9 + 0;
        const stretch = { from: near(box.x), to: near(box.x + box.w) };
        const round = (span: { from: number; to: number }) => ({ from: near(span.from), to: near(span.to) });
        expect(stretch, `${name}, ${part.id} top`).toEqual(round(segment.top));
        expect(stretch, `${name}, ${part.id} bottom`).toEqual(round(segment.bottom));
        expect([box.y, box.h], `${name}, ${part.id} height`).toEqual([0, HEIGHT]);
      });
    }
  });

  it("names the four phases' tooltips in the order the segments are drawn", () => {
    expect(FILLS.map((part) => part["x-part.tooltip"])).toEqual([
      "Peak of Inflated Expectations",
      "Trough of Disillusionment",
      "Slope of Enlightenment",
      "Plateau of Productivity",
    ]);
  });
});
