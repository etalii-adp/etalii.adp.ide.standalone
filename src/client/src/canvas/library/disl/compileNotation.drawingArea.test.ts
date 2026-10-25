import { describe, expect, it } from "vitest";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument } from "./disTypes";
import { drawingArea } from "./drawingArea";

/**
 * A bounded drawing area and the axes along its edges (DISL §5.3 `ranges`, `endLabels`, `outOfRange`;
 * §6.13 `canvas.bounds`, `fit`, `margin`): a canvas of fixed bounds within a margin, stretched across
 * to the pane's proportions, whose axes span it from their `min` to their `max`; an axis's named,
 * unequal ranges drawn as bands, its end labels and its title along its edge, and a drag clamped at
 * the bounds where both axes say so.
 */

function specOf(): DislDocument {
  return {
    disl: "0.3",
    language: { id: "test", version: "1.0.0" },
    metamodel: { types: { Dot: { attributes: { across: { type: "number" }, up: { type: "number" } } } } },
    coordinates: {
      axes: {
        across: {
          kind: "linear",
          label: "Across",
          min: 0,
          max: 1,
          outOfRange: { drag: "clamp" },
          ranges: [
            { id: "near", label: "Near", from: 0, to: 0.25, style: "first" },
            { id: "far", label: "Far", from: 0.25, to: 1, style: "second", separator: { width: 1, dash: [6, 6] } },
          ],
        },
        up: { kind: "linear", label: "Up", min: 0, max: 1, reversed: true, outOfRange: { drag: "clamp" }, endLabels: { min: "Low", max: "High" } },
      },
      systems: { plane: { kind: "cartesian", x: "across", y: "up", orientation: "y-down" } },
      default: "plane",
    },
    notation: {
      nodes: { Dot: { shape: "rect", size: { fixed: [10, 10] }, placement: { system: "plane", x: { attribute: "across" }, y: { attribute: "up" }, movable: true } } },
      canvas: { bounds: { x: 0, y: 0, w: 100, h: 100 }, fit: "stretch", margin: 10 },
    },
  };
}

const TYPOGRAPHY = { fontSize: 20, scaleWithView: { min: 0.5, max: 2 } };

const BINDINGS: NotationBindings = {
  celPaths: {},
  classNames: () => [],
  axisChrome: {
    className: "t-chrome",
    axisClassName: "t-axis",
    rangeClassName: (_axis, _range, index) => `t-range t-range-${index}`,
    typography: TYPOGRAPHY,
  },
};

/** A pane twice as wide as it is tall. */
const WIDE = { pane: { width: 240, height: 120 } };

describe("compileNotation, for a bounded drawing area and the axes along it", () => {
  it("shows the bounds within their margin, as they are without a pane", () => {
    expect(compileNotation(specOf(), BINDINGS).extent).toEqual({ x: -10, y: -10, width: 120, height: 120 });
  });

  it("stretches the bounds across to the pane's proportions, the margin and the height kept", () => {
    // 240 by 120 is 2:1, so the extent is 240 wide at the 120 it is high, and the bounds 220 of it.
    expect(compileNotation(specOf(), BINDINGS, WIDE).extent).toEqual({ x: -10, y: -10, width: 240, height: 120 });
  });

  it("keeps a drawing area that is not stretched at its bounds, whatever the pane", () => {
    const spec = specOf();
    delete (spec.notation.canvas as { fit?: unknown }).fit;

    expect(compileNotation(spec, BINDINGS, WIDE).extent).toEqual({ x: -10, y: -10, width: 120, height: 120 });
  });

  it("refuses a stretch with no bounds to stretch", () => {
    const spec = specOf();
    delete (spec.notation.canvas as { bounds?: unknown }).bounds;

    expect(() => compileNotation(spec, BINDINGS)).toThrow(/stretches the canvas to the pane and states no bounds/);
  });

  it("draws an axis's named ranges as bands across the extent, a separator as the rule at a range's start", () => {
    expect(compileNotation(specOf(), BINDINGS).background?.bands).toEqual([
      { orientation: "vertical", start: 10 / 120, end: 35 / 120, label: { template: "Near" }, className: "t-range t-range-0", typography: TYPOGRAPHY },
      { orientation: "vertical", start: 35 / 120, end: 110 / 120, label: { template: "Far" }, edge: true, className: "t-range t-range-1", typography: TYPOGRAPHY },
    ]);
  });

  it("places the bands on the stretched area", () => {
    const bands = compileNotation(specOf(), BINDINGS, WIDE).background?.bands ?? [];

    expect(bands.map((band) => [band.start, band.end])).toEqual([
      [10 / 240, 65 / 240],
      [65 / 240, 230 / 240],
    ]);
  });

  it("draws each axis's title and end labels along its edge, the vertical axis first, its top end the reversed one's maximum", () => {
    const background = compileNotation(specOf(), BINDINGS).background;

    expect(background?.className).toBe("t-chrome");
    expect(background?.axes).toEqual([
      { orientation: "vertical", title: { template: "Up" }, startLabel: { template: "High" }, endLabel: { template: "Low" }, className: "t-axis", typography: TYPOGRAPHY },
      { orientation: "horizontal", title: { template: "Across" }, className: "t-axis", typography: TYPOGRAPHY },
    ]);
  });

  it("draws no axis along a canvas that states no bounds", () => {
    const spec = specOf();
    spec.notation.canvas = {};
    for (const axis of Object.values(spec.coordinates!.axes!)) {
      delete (axis as { outOfRange?: unknown }).outOfRange;
      delete (axis as { ranges?: unknown }).ranges;
    }

    expect(compileNotation(spec, BINDINGS).background?.axes).toBeUndefined();
  });

  it("refuses ranges on a canvas that states no bounds to draw them in", () => {
    const spec = specOf();
    spec.notation.canvas = {};
    for (const axis of Object.values(spec.coordinates!.axes!)) {
      delete (axis as { outOfRange?: unknown }).outOfRange;
    }

    expect(() => compileNotation(spec, BINDINGS)).toThrow(/axis "across" names ranges, and the canvas states no bounds to draw them in/);
  });

  it("refuses a range label the library cannot place", () => {
    const spec = specOf();
    (spec.coordinates!.axes!.across!.ranges![0] as { labelPosition?: string }).labelPosition = "start";

    expect(() => compileNotation(spec, BINDINGS)).toThrow(/range "near" of axis "across" puts its label at the start/);
  });

  it("clamps a drag at the bounds where both axes clamp one", () => {
    expect(compileNotation(specOf(), BINDINGS, WIDE).dragBounds).toEqual({ x: 0, y: 0, width: 220, height: 100 });
  });

  it("refuses a drag clamped on one axis only", () => {
    const spec = specOf();
    delete (spec.coordinates!.axes!.up as { outOfRange?: unknown }).outOfRange;

    expect(() => compileNotation(spec, BINDINGS)).toThrow(/clamps a drag on its x axis and not on its y axis/);
  });

  it("states no drag bound where no axis clamps one", () => {
    const spec = specOf();
    for (const axis of Object.values(spec.coordinates!.axes!)) {
      delete (axis as { outOfRange?: unknown }).outOfRange;
    }

    expect(compileNotation(spec, BINDINGS)).not.toHaveProperty("dragBounds");
  });
});

describe("drawingArea", () => {
  it("maps a domain point onto the stretched bounds, a reversed axis from its maximum", () => {
    const area = drawingArea(specOf(), WIDE.pane)!;

    expect(area.toCanvas(0.25, 1)).toEqual({ x: 55, y: 0 });
    expect(area.toCanvas(1, 0)).toEqual({ x: 220, y: 100 });
  });

  it("maps a canvas point back into the domain", () => {
    expect(drawingArea(specOf(), WIDE.pane)!.toDomain(55, 25)).toEqual({ x: 0.25, y: 0.75 });
  });

  it("is the bounds themselves without a pane", () => {
    const area = drawingArea(specOf())!;

    expect(area.area).toEqual({ x: 0, y: 0, width: 100, height: 100 });
    expect(area.toCanvas(0.5, 0.5)).toEqual({ x: 50, y: 50 });
  });

  it("reads a margin of four sides", () => {
    const spec = specOf();
    (spec.notation.canvas as { margin?: unknown }).margin = { top: 5, right: 10, bottom: 15, left: 20 };

    expect(drawingArea(spec)!.extent).toEqual({ x: -20, y: -5, width: 130, height: 120 });
  });

  it("is none for a canvas that states no bounds", () => {
    const spec = specOf();
    spec.notation.canvas = {};

    expect(drawingArea(spec)).toBeUndefined();
  });
});
