import { describe, expect, it } from "vitest";
import { outlineOf } from "../shapes/outline";
import { BUILT_IN_SHAPES } from "../definition/diagramDefinition";
import { DISL_SHAPE_NAMES, libraryMarkerOf, libraryRouteOf, libraryShapeOf } from "./shapeCatalog";

/** DISL's superellipse (§6.7): |2x/w - 1|^n + |2y/h - 1|^n = 1, the left side of that for a point. */
function superellipseLevel(x: number, y: number, w: number, h: number, n: number): number {
  return Math.abs((2 * x) / w - 1) ** n + Math.abs((2 * y) / h - 1) ** n;
}

describe("the DISL shape catalog", () => {
  it("names only shapes the library has", () => {
    // The trapezoid and the cylinder are admitted only at the library's own parameters, not DISL's defaults.
    const ASKED: Readonly<Record<string, Readonly<Record<string, unknown>>>> = { trapezoid: { inset: 0.15, direction: "down" }, cylinder: { cap: 12 }, line: { orientation: "horizontal" } };
    const asDrawn = (name: string) => (name in ASKED ? { type: name, params: ASKED[name] } : name);
    for (const name of DISL_SHAPE_NAMES) {
      expect(BUILT_IN_SHAPES, name).toContain(libraryShapeOf(asDrawn(name)));
    }
  });

  it("admits a built-in at the parameters the library draws, its defaults included", () => {
    expect(libraryShapeOf("rect")).toBe("box");
    expect(libraryShapeOf("hexagon")).toBe("hexagon");
    expect(libraryShapeOf({ type: "superellipse", params: { exponent: 4 } })).toBe("superellipse");
    expect(libraryShapeOf({ type: "parallelogram", params: { skew: 0.2, direction: "right" } })).toBe("parallelogram");
    // A name the catalog does not know is a custom shape, which a module binding answers for.
    expect(libraryShapeOf("phasedBanner")).toBeUndefined();
  });

  it("refuses a built-in asked for with parameters the library does not draw", () => {
    expect(() => libraryShapeOf({ type: "superellipse", params: { exponent: 3 } })).toThrow(/exponent 4.*asks for 3/);
    expect(() => libraryShapeOf({ type: "hexagon", params: { orientation: "pointy" } })).toThrow(/orientation/);
    expect(() => libraryShapeOf({ type: "rect", params: { radius: 4 } })).toThrow(/no "radius" parameter/);
    // DISL's trapezoid defaults to its narrow side up; the library's is narrower at the bottom.
    expect(() => libraryShapeOf("trapezoid")).toThrow(/inset 0.15/);
  });

  it("draws DISL's superellipse of exponent 4 as the library's squircle", () => {
    // The library samples the curve; every sample must lie on DISL's curve, and the four extremes
    // must be among them, or the drawing and the specification describe different shapes.
    for (const [w, h] of [[200, 60], [80, 48], [400, 32]] as const) {
      const outline = outlineOf("superellipse", { x: 0, y: 0, width: w, height: h });
      for (const point of outline) {
        expect(superellipseLevel(point.x, point.y, w, h, 4)).toBeCloseTo(1, 9);
      }
      for (const extreme of [{ x: w, y: h / 2 }, { x: w / 2, y: h }, { x: 0, y: h / 2 }, { x: w / 2, y: 0 }]) {
        expect(outline.some((point) => Math.abs(point.x - extreme.x) < 1e-5 && Math.abs(point.y - extreme.y) < 1e-5), JSON.stringify(extreme)).toBe(true);
      }
    }
  });

  it("draws DISL's hexagon and parallelogram at their defaults as the library's corners", () => {
    const bounds = { x: 0, y: 0, width: 200, height: 60 };
    // Hexagon, inset 0.25, flat: flat top and bottom, pointed at the sides' middles.
    expect(outlineOf("hexagon", bounds)).toEqual([
      { x: 50, y: 0 }, { x: 150, y: 0 }, { x: 200, y: 30 }, { x: 150, y: 60 }, { x: 50, y: 60 }, { x: 0, y: 30 },
    ]);
    // Parallelogram, skew 0.2, right: the top edge shifted right by a fifth of the width.
    expect(outlineOf("parallelogram", bounds)).toEqual([{ x: 40, y: 0 }, { x: 200, y: 0 }, { x: 160, y: 60 }, { x: 0, y: 60 }]);
  });

  it("draws DISL's cylinder with a cap of 12 as the library's cylinder", () => {
    expect(libraryShapeOf({ type: "cylinder", params: { cap: 12 } })).toBe("cylinder");
    expect(libraryShapeOf({ type: "cylinder", params: { cap: 12, orientation: "vertical" } })).toBe("cylinder");
    // DISL's cap defaults to 0.15 of the height (Appendix B.2); the library's is 12 at every height.
    expect(() => libraryShapeOf("cylinder")).toThrow(/cap 12.*asks for "0.15 × height"/);
    expect(() => libraryShapeOf({ type: "cylinder", params: { cap: 12, orientation: "horizontal" } })).toThrow(/orientation/);
  });

  it("draws DISL 0.5's person at its default corner of 8 as the library's person", () => {
    // DISL 0.5 §6.7: the person's one parameter is the body's corner radius, 8 by default (Appendix B.2).
    expect(libraryShapeOf("person")).toBe("person");
    expect(libraryShapeOf({ type: "person", params: { radius: 8 } })).toBe("person");
    // The library draws the corner at 8 only, so another radius is refused rather than drawn at 8.
    expect(() => libraryShapeOf({ type: "person", params: { radius: 4 } })).toThrow(/radius 8.*asks for 4/);
    // The head is fixed (§6.7): there is no parameter for it.
    expect(() => libraryShapeOf({ type: "person", params: { head: 12 } })).toThrow(/no "head" parameter/);
  });

  it("crosses DISL's marker names over to the library's", () => {
    expect(libraryMarkerOf("arrowFilled")).toBe("arrow");
    expect(libraryMarkerOf("arrow")).toBe("open-arrow");
    expect(libraryMarkerOf("none")).toBe("none");
    expect(() => libraryMarkerOf("erMany")).toThrow(/no marker/);
  });

  it("maps DISL's routings to the library's routes", () => {
    expect(libraryRouteOf("bezier")).toBe("cubic-bezier");
    expect(libraryRouteOf("orthogonal")).toBe("orthogonal");
    expect(() => libraryRouteOf("manhattan")).toThrow(/no route/);
  });
});
