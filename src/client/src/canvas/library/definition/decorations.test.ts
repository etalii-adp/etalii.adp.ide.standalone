import { describe, expect, it } from "vitest";
import type { DiagramModelElement } from "../api/diagramModel";
import type { BindingSource } from "./binding";
import type { DecorationDeclaration } from "./diagramDefinition";
import { resolveDecorations } from "./decorations";

const element: DiagramModelElement = { id: "e1", type: "n", x: 0, y: 0 };
const bounds = { x: -50, y: -20, width: 100, height: 40 };
const source = (payload?: unknown): BindingSource => ({ element, payload });

/**
 * THE FOUR SHAPES THE FIVE COMPONENT-LESS RENDERERS DRAW, each written as its module writes it
 * today. This is the acceptance for the addition: if one of these cannot be expressed, the
 * vocabulary is wrong and the procedure is to extend it centrally - never to leave the module
 * imperative.
 */
describe("decorations — the stub (ansible, helm)", () => {
  // A line from the element's right edge to nowhere, with a label above it. Today: a <path>
  // through straightPath() plus a <text> at from.x + 8, from.y - 6.
  const stub: DecorationDeclaration[] = [
    {
      glyph: "line",
      from: { x: 50, y: 0 },
      to: { x: 40, y: 0 },
      text: { path: "payload.missing" },
      textAt: { x: 8, y: -6 },
      className: "ansible-edge-line",
    },
  ];

  it("draws a line from a point to a fixed length, with its label offset from the start", () => {
    const out = resolveDecorations(stub, source({ missing: "db_servers" }), bounds);

    expect(out).toHaveLength(1);
    expect(out[0]!.from).toEqual({ x: 50, y: 0 });
    // `to` is relative to `from`, which is what makes "a fixed length onward" declarable
    // without the author computing an absolute point.
    expect(out[0]!.to).toEqual({ x: 90, y: 0 });
    expect(out[0]!.text).toBe("db_servers");
    expect(out[0]!.textAt).toEqual({ x: 58, y: -6 });
  });
});

describe("decorations — the badge (causal-loop)", () => {
  // A polarity arc plus a centred caption. The arc is computed today by
  // loopMarkerPath(x, y, r, clockwise) inside the renderer; as a declaration the same path
  // arrives as DATA through a binding, which is what keeps it unable to call anything.
  const badge: DecorationDeclaration[] = [
    { glyph: "path", d: { path: "payload.markerPath" }, className: "causal-loop-marker" },
    { glyph: "marker", marker: "arrow", text: { path: "payload.caption" }, textAnchor: "middle" },
  ];

  it("takes a computed path as data rather than as a function", () => {
    const out = resolveDecorations(badge, source({ markerPath: "M0 0 A 13 13 0 1 1 1 0", caption: "R1" }), bounds);

    expect(out[0]!.d).toBe("M0 0 A 13 13 0 1 1 1 0");
    expect(out[1]!.text).toBe("R1");
    expect(out[1]!.textAnchor).toBe("middle");
  });

  it("omits a decoration whose condition fails, which is how one polarity draws and not the other", () => {
    const conditional: DecorationDeclaration[] = [
      { glyph: "path", d: { path: "payload.markerPath" }, when: { path: "payload.reinforcing", is: "true" } },
    ];

    expect(resolveDecorations(conditional, source({ markerPath: "M0 0", reinforcing: true }), bounds)).toHaveLength(1);
    expect(resolveDecorations(conditional, source({ markerPath: "M0 0", reinforcing: false }), bounds)).toHaveLength(0);
  });
});

describe("decorations — the annotation (sparql) and the target (wardley)", () => {
  it("draws bare positioned text with no glyph geometry at all", () => {
    const annotation: DecorationDeclaration[] = [
      { glyph: "marker", text: { path: "payload.note" }, textAt: { x: 0, y: 24 }, typography: { fontStyle: "italic" } },
    ];
    const out = resolveDecorations(annotation, source({ note: "OPTIONAL" }), bounds);

    expect(out[0]!.text).toBe("OPTIONAL");
    expect(out[0]!.textAt).toEqual({ x: 0, y: 24 });
    expect(out[0]!.typography).toEqual({ fontStyle: "italic" });
  });

  it("draws a circle with a bound radius and a label", () => {
    const target: DecorationDeclaration[] = [
      { glyph: "circle", radius: { path: "payload.r" }, text: { path: "payload.name" }, textAt: { x: 0, y: -12 } },
    ];
    const out = resolveDecorations(target, source({ r: 6, name: "target" }), bounds);

    expect(out[0]!.radius).toBe(6);
    expect(out[0]!.text).toBe("target");
  });
});

describe("decorations — resolution rules", () => {
  it("falls back rather than vanishing when a bound number does not resolve", () => {
    // A decoration that disappeared on a typo would be indistinguishable from one nobody
    // declared. It draws at the fallback instead, which is visible and therefore reportable.
    const out = resolveDecorations([{ glyph: "circle", radius: { path: "payload.nope" } }], source({}), bounds);

    expect(out).toHaveLength(1);
    expect(out[0]!.radius).toBe(0);
  });

  it("takes a literal number as readily as a binding, so authored geometry stays authored", () => {
    const out = resolveDecorations([{ glyph: "circle", radius: 9 }], source({}), bounds);
    expect(out[0]!.radius).toBe(9);
  });

  it("positions everything relative to the element's centre, not the canvas origin", () => {
    const shifted = { x: 200, y: 100, width: 100, height: 40 };
    const out = resolveDecorations([{ glyph: "line", from: { x: 0, y: 0 } }], source({}), shifted);

    expect(out[0]!.from).toEqual({ x: 250, y: 120 });
  });

  it("returns nothing for no declarations", () => {
    expect(resolveDecorations(undefined, source({}), bounds)).toEqual([]);
    expect(resolveDecorations([], source({}), bounds)).toEqual([]);
  });
});
