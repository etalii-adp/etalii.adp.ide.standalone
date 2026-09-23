import { describe, expect, it } from "vitest";
import { cornersOf, isInsideOutline, outlineOf, textRegionOf, OUTLINED_SHAPES } from "./outline";
import { BUILT_IN_SHAPES, type ShapeBounds } from "../definition/diagramDefinition";

/**
 * The text region fits inside the outline, for every shape that has one.
 *
 * This is the guard the three-consumer arrangement exists for: a label placed against the
 * BOUNDING BOX sits over a trapezoid's slanted edge and outside a diode's curved end, and looks
 * fine on a box, which is why the bug survives review. Checking the region's corners against the
 * outline by point-in-polygon is the assertion that cannot be satisfied by eye.
 *
 * <b>The height 48 is written out here on purpose, and it is not the library's own number.</b> The
 * library's default element height is `DEFAULT_HEIGHT = 40`. 48 is the shared element height of the
 * notation this capability was built for, which will land module-side as its own constant and does
 * not exist yet. The library must not name that diagram (Requirements 9.1 and 9.6), so the number
 * is written out rather than imported. Two things follow for a later reader: do not "fix" this by
 * importing the constant when it appears, and do not "align" it with the library's 40 - it is
 * deliberately a different number, chosen to exercise a height the library does not default to.
 */
const SHARED_HEIGHT = 48;

/** A label band of a plausible single-line height, which is what a text region is asked for. */
const BAND_HEIGHT = 24;

/** The widths the design names: a minimum, a middling and a generous one. */
const WIDTHS = [80, 160, 400];

const boundsOf = (width: number): ShapeBounds => ({ x: 10, y: 20, width, height: SHARED_HEIGHT });

describe("outlineOf", () => {
  it("covers the three new shapes and the three existing polygons, and nothing else", () => {
    // Arrange, act and assert: the shapes with an outline are stated, and every one of them is a
    // real built-in - a list naming a shape the library does not have would guard nothing.
    expect([...OUTLINED_SHAPES].sort()).toEqual(["diamond", "diode", "hexagon", "parallelogram", "superellipse", "trapezoid"]);
    for (const shape of OUTLINED_SHAPES) {
      expect(BUILT_IN_SHAPES).toContain(shape);
      expect(outlineOf(shape, boundsOf(160)).length).toBeGreaterThanOrEqual(3);
    }
  });

  it("leaves a shape with no outline alone, rather than approximating it", () => {
    // A circle replaced by a 64-gon would move drawings nobody asked to move.
    for (const shape of ["box", "pill", "ellipse", "cylinder", "none"] as const) {
      expect(outlineOf(shape, boundsOf(160))).toEqual([]);
    }
  });

  it("keeps the existing polygons' corners exactly where the canvas drew them", () => {
    // Arrange & act.
    const bounds = boundsOf(200);

    // Assert: the diamond's four corners, from the list the canvas held inline.
    expect(outlineOf("diamond", bounds)).toEqual([
      { x: 110, y: 20 },
      { x: 210, y: 44 },
      { x: 110, y: 68 },
      { x: 10, y: 44 },
    ]);
  });
});

describe("textRegionOf fits inside the outline", () => {
  for (const shape of ["superellipse", "trapezoid", "diode"] as const) {
    for (const width of WIDTHS) {
      it(`${shape} at width ${width}: every corner of the region is inside the shape`, () => {
        // Arrange.
        const bounds = boundsOf(width);
        const outline = outlineOf(shape, bounds);

        // Act.
        const region = textRegionOf(shape, bounds, BAND_HEIGHT);

        // Assert: a region with no room would pass this vacuously, so it has to be real first.
        expect(region.width).toBeGreaterThan(0);
        expect(region.height).toBeGreaterThan(0);
        for (const corner of cornersOf(region)) {
          expect(isInsideOutline(corner, outline), `corner ${corner.x},${corner.y} of ${shape} at ${width}`).toBe(true);
        }
      });
    }
  }

  it("gives a shape without an outline the bounds less the padding, as the canvas already did", () => {
    // Arrange & act.
    const region = textRegionOf("box", boundsOf(160), BAND_HEIGHT);

    // Assert.
    expect(region).toEqual({ x: 16, y: 26, width: 148, height: 36 });
  });

  it("collapses rather than inventing room when the band is taller than the shape allows", () => {
    // A caller asking for a band the shape cannot hold gets nothing, not a region over the edge.
    const region = textRegionOf("superellipse", boundsOf(80), SHARED_HEIGHT * 4);
    expect(region.width).toBe(0);
  });
});

describe("isInsideOutline", () => {
  it("counts the edge as inside, and a point beyond it as out", () => {
    // Arrange: the trapezoid's bottom edge runs from 0.15 to 0.85 of the width.
    const bounds = boundsOf(100);
    const outline = outlineOf("trapezoid", bounds);

    // Act and assert.
    expect(isInsideOutline({ x: 25, y: 68 }, outline)).toBe(true);
    expect(isInsideOutline({ x: 60, y: 44 }, outline)).toBe(true);
    // Below the slanted side at the bottom left: inside the bounding box, outside the shape.
    expect(isInsideOutline({ x: 12, y: 67 }, outline)).toBe(false);
  });

  it("refuses a degenerate outline rather than reporting everything inside", () => {
    expect(isInsideOutline({ x: 0, y: 0 }, [])).toBe(false);
  });
});
