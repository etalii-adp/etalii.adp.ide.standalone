import { describe, expect, it } from "vitest";
import disText from "../definition/agent-behavior-modelling.dis?raw";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { resolvePath, type ResolvedSegment } from "@client/canvas/library/disl/geomExpr";
import { libraryShapeOf, shapeNameOf } from "@client/canvas/library/disl/shapeCatalog";
import { outlineOf } from "@client/canvas/library/shapes/outline";
import type { ShapePoint } from "@client/canvas/library/definition/diagramDefinition";

/**
 * The bundled specification's shapes are the library's: its custom `diode` is the library's diode,
 * and its composites' superellipse is the library's squircle.
 *
 * The canvas draws the library shapes `abmBindings.ts` and the shape catalog name, and never reads
 * the specification's geometry. This evaluates that geometry and compares it with the library's
 * outline, so the two cannot drift apart unnoticed.
 */

const SPEC = parseDisl(disText);

/** The node sizes the grid covers: a node is 200 by 60 today, and the shape must hold beyond it. */
const SIZES: readonly (readonly [number, number])[] = [[200, 60], [30, 60], [60, 60], [400, 20], [48, 32]];

/**
 * The centre of the SVG arc from `from` through the `A` command, by the endpoint-to-centre
 * conversion of SVG 1.1 F.6.5, radii scaled up when they cannot span the chord.
 */
function arcCentre(from: ShapePoint, arc: Extract<ResolvedSegment, { op: "A" }>): { centre: ShapePoint; radius: number } {
  if (arc.rotation !== 0 || arc.rx !== arc.ry) {
    throw new Error("Only an unrotated circular arc is compared here.");
  }

  const mx = (from.x - arc.x) / 2;
  const my = (from.y - arc.y) / 2;
  const half = Math.hypot(mx, my);
  const radius = Math.max(arc.rx, half);
  const factor = Math.sqrt(Math.max(0, radius * radius - half * half)) / half;
  const sign = arc.largeArc === arc.sweep ? -1 : 1;
  return {
    centre: { x: (from.x + arc.x) / 2 + sign * factor * my, y: (from.y + arc.y) / 2 - sign * factor * mx },
    radius,
  };
}

describe("the specification's diode is the library's diode", () => {
  const diode = SPEC.notation.shapes!.diode!;
  const path = (w: number, h: number) => resolvePath(diode.path!.segments, { w, h, p: {} });

  it("is drawn by the delegate node, as a custom shape the library answers for", () => {
    expect(shapeNameOf(SPEC.notation.nodes.Delegate!.shape).name).toBe("diode");
    expect(libraryShapeOf("diode")).toBeUndefined();
  });

  it("is the square-left, round-right outline: the same corners, and the arc on the library's circle", () => {
    for (const [w, h] of SIZES.filter(([width, height]) => width >= height / 2)) {
      const [move, top, arc, bottom, close] = path(w, h);
      expect([move!.op, top!.op, arc!.op, bottom!.op, close!.op]).toEqual(["M", "L", "A", "L", "Z"]);
      const library = outlineOf("diode", { x: 0, y: 0, width: w, height: h });
      const first = library[0]!;
      const straight = library[1]!;
      const end = library[library.length - 2]!;
      const last = library[library.length - 1]!;
      const point = (segment: ResolvedSegment | undefined) => (segment !== undefined && segment.op !== "Z" ? { x: segment.x, y: segment.y } : null);

      // The square end and the start of the curve.
      expect([point(move), point(top), point(bottom)], `${w} x ${h}`).toEqual([first, straight, last]);
      expect(point(arc), `${w} x ${h}`).toEqual(end);

      // Every sample of the library's arc lies on the specification's circle, on the side the
      // sweep bulges to, and the samples run from the arc's start to its end.
      const { centre, radius } = arcCentre(point(top)!, arc as Extract<ResolvedSegment, { op: "A" }>);
      const samples = library.slice(2, -2);
      expect(samples.length, `${w} x ${h}`).toBeGreaterThan(8);
      for (const sample of samples) {
        expect(Math.hypot(sample.x - centre.x, sample.y - centre.y), `${w} x ${h}`).toBeCloseTo(radius, 9);
        expect(sample.x, `${w} x ${h}`).toBeGreaterThanOrEqual(centre.x - 1e-9);
      }
      // The sweep flag picks which of the two half circles is drawn: with y pointing down, a set
      // flag turns clockwise on screen, which is an increasing angle from the start to the end.
      const angles = samples.map((sample) => Math.atan2(sample.y - centre.y, sample.x - centre.x));
      const turning = angles.slice(1).map((angle, index) => angle - angles[index]!);
      expect(turning.every((step) => ((arc as Extract<ResolvedSegment, { op: "A" }>).sweep ? step > 0 : step < 0)), `${w} x ${h}`).toBe(true);
      expect(samples[0]!.x).toBeCloseTo(point(top)!.x, 9);
      expect(samples[0]!.y).toBeCloseTo(point(top)!.y, 9);
      expect(samples[samples.length - 1]!.x).toBeCloseTo(point(arc)!.x, 9);
      expect(samples[samples.length - 1]!.y).toBeCloseTo(point(arc)!.y, 9);
    }
  });
});

describe("the specification's composites are the library's squircle", () => {
  it("asks for the exponent the library draws, and the library's samples lie on that curve", () => {
    const composites = Object.entries(SPEC.notation.nodes).filter(([, node]) => shapeNameOf(node.shape).name === "superellipse");
    expect(composites.map(([name]) => name)).toEqual(["Sequence", "Fallback", "Parallel"]);
    for (const [name, node] of composites) {
      expect(libraryShapeOf(node.shape), name).toBe("superellipse");
      const exponent = Number(shapeNameOf(node.shape).params.exponent ?? 4);
      for (const [w, h] of SIZES) {
        for (const sample of outlineOf("superellipse", { x: 0, y: 0, width: w, height: h })) {
          const level = Math.abs((2 * sample.x) / w - 1) ** exponent + Math.abs((2 * sample.y) / h - 1) ** exponent;
          expect(level, `${name} at ${w} x ${h}`).toBeCloseTo(1, 9);
        }
      }
    }
  });
});
