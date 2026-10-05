import { describe, expect, it } from "vitest";
import { evaluateCondition, evaluateGeom, resolveBox, resolvePath } from "./geomExpr";

const SCOPE = { w: 96, h: 32, p: { count: 3, b1: 0.25, b2: 0.5 } };

describe("the shape-context GeomExpr evaluator", () => {
  it("reads numbers, the box, the parameters, arithmetic and min/max", () => {
    expect(evaluateGeom(12, SCOPE)).toBe(12);
    expect(evaluateGeom("w - min(h / 2.0, w / 2.0)", SCOPE)).toBe(80);
    expect(evaluateGeom("w * p.b1 - max(1, 2) * -3", SCOPE)).toBe(30);
    expect(evaluateGeom("(w + h) / 2", SCOPE)).toBe(64);
  });

  it("reads conditions and the conditional the custom shapes use", () => {
    expect(evaluateCondition("p.count >= 2", SCOPE)).toBe(true);
    expect(evaluateCondition("p.count == 4 || !(w < h)", SCOPE)).toBe(true);
    expect(evaluateCondition("p.count >= 2 && p.count < 3", SCOPE)).toBe(false);
    expect(evaluateCondition(undefined, SCOPE)).toBe(true);
    expect(evaluateGeom("p.count == 3 ? w : w * p.b2", SCOPE)).toBe(96);
    expect(evaluateGeom("p.count == 1 ? w : w * p.b2", SCOPE)).toBe(48);
  });

  it("refuses everything outside the shape context, naming it", () => {
    // A CEL function, a host object, a string and an unbound parameter: an evaluator that guessed
    // at any of them would let a geometry proof say "equal" about an expression it never read.
    expect(() => evaluateGeom("math.round(w)", SCOPE)).toThrow(/"math" is outside/);
    expect(() => evaluateGeom("self.width", SCOPE)).toThrow(/"self" is outside/);
    expect(() => evaluateGeom("'w'", SCOPE)).toThrow(/outside the shape-context subset/);
    expect(() => evaluateGeom("p.b3", SCOPE)).toThrow(/"p\.b3" has no value/);
    expect(() => evaluateGeom("w +", SCOPE)).toThrow(/ends early/);
    expect(() => evaluateGeom("w > h", SCOPE)).toThrow(/is a condition/);
    expect(() => evaluateCondition("w", SCOPE)).toThrow(/is a number/);
  });

  it("resolves a path and a box", () => {
    expect(resolvePath([{ op: "M", x: 0, y: 0 }, { op: "L", x: "w", y: "h / 2.0" }, { op: "Z" }], SCOPE)).toEqual([
      { op: "M", x: 0, y: 0 },
      { op: "L", x: 96, y: 16 },
      { op: "Z" },
    ]);
    expect(resolveBox({ x: 6, y: "h * 0.25", w: "w - 12", h: "h / 2" }, SCOPE)).toEqual({ x: 6, y: 8, w: 84, h: 16 });
  });
});
