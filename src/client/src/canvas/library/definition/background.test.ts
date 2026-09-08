import { describe, expect, it } from "vitest";
import type { DiagramModelElement } from "../api/diagramModel";
import type { BindingSource } from "./binding";
import { resolveBackground, type BackgroundDeclaration } from "./background";

const element: DiagramModelElement = { id: "__background__", type: "__background__", x: 0, y: 0 };
const extent = { x: 0, y: 0, width: 1000, height: 500 };
const source = (payload?: unknown): BindingSource => ({ element, payload });

/**
 * WARDLEY'S BACKGROUND, WHICH IS THE ACCEPTANCE. Twenty-one of its twenty-four raw-SVG lines
 * are these: evolution stage bands with boundaries and labels, two axes with a rotated title
 * and end labels, and the attitude regions bound to a model collection. If one of these cannot
 * be expressed the vocabulary is wrong, and the procedure is to extend it centrally rather than
 * to leave the module imperative.
 */
const wardley: BackgroundDeclaration = {
  bands: [
    {
      each: { path: "payload.stages" },
      orientation: "vertical",
      start: { path: "start" },
      end: { path: "end" },
      label: { path: "label" },
      edge: true,
      className: "wardley-band",
    },
  ],
  axes: [
    {
      orientation: "vertical",
      title: { path: "payload.valueChainTitle" },
      startLabel: { path: "payload.visible" },
      endLabel: { path: "payload.invisible" },
      className: "wardley-axis",
    },
    { orientation: "horizontal", title: { path: "payload.evolutionTitle" }, className: "wardley-axis" },
  ],
  regions: [
    {
      each: { path: "payload.attitudes" },
      x: { path: "x" },
      y: { path: "y" },
      width: { path: "w" },
      height: { path: "h" },
      label: { path: "label" },
      className: "wardley-attitude",
    },
  ],
};

const wardleyModel = {
  stages: [
    { start: 0, end: 0.25, label: "Genesis" },
    { start: 0.25, end: 0.5, label: "Custom" },
    { start: 0.5, end: 0.75, label: "Product" },
    { start: 0.75, end: 1, label: "Commodity" },
  ],
  valueChainTitle: "Value chain",
  visible: "Visible",
  invisible: "Invisible",
  evolutionTitle: "Evolution",
  attitudes: [{ x: 0.1, y: 0.1, w: 0.2, h: 0.3, label: "Pioneers" }],
};

describe("background — wardley's stage bands", () => {
  it("draws one band per model stage, spanning the full height at its own fractions", () => {
    const out = resolveBackground(wardley, source(wardleyModel), extent);
    const bands = out.rects.filter((r) => r.className === "wardley-band");

    expect(bands).toHaveLength(4);
    expect(bands[0]).toMatchObject({ x: 0, y: 0, width: 250, height: 500 });
    expect(bands[3]).toMatchObject({ x: 750, width: 250 });
  });

  it("draws a boundary between stages but not on the outer edge", () => {
    // Wardley draws the rule BETWEEN two stages; one on the outermost edge would double the
    // axis already drawn there. Three boundaries for four bands.
    const out = resolveBackground(wardley, source(wardleyModel), extent);
    const edges = out.lines.filter((l) => l.className === "wardley-band-edge");

    expect(edges).toHaveLength(3);
    expect(edges[0]!.x1).toBe(250);
  });

  it("labels each band under the plotted area", () => {
    const out = resolveBackground(wardley, source(wardleyModel), extent);
    const labels = out.texts.filter((t) => t.className === "wardley-band-label");

    expect(labels.map((t) => t.text)).toEqual(["Genesis", "Custom", "Product", "Commodity"]);
    expect(labels[0]!.x).toBe(125);
    expect(labels[0]!.y).toBeGreaterThan(extent.height);
  });

  it("draws nothing when the stage collection is empty, rather than one degenerate band", () => {
    const out = resolveBackground(wardley, source({ ...wardleyModel, stages: [] }), extent);
    expect(out.rects.filter((r) => r.className === "wardley-band")).toEqual([]);
  });
});

describe("background — wardley's axes", () => {
  it("draws the value-chain axis up the left and the evolution axis along the bottom", () => {
    const out = resolveBackground(wardley, source(wardleyModel), extent);
    const axes = out.lines.filter((l) => l.className === "wardley-axis");

    expect(axes).toHaveLength(2);
    expect(axes[0]).toMatchObject({ x1: 0, y1: 0, x2: 0, y2: 500 });
    expect(axes[1]).toMatchObject({ x1: 0, y1: 500, x2: 1000, y2: 500 });
  });

  it("rotates the vertical axis title, which is the thing no other declaration expresses", () => {
    const out = resolveBackground(wardley, source(wardleyModel), extent);
    const title = out.texts.find((t) => t.text === "Value chain");

    expect(title!.rotate).toBe(-90);
    expect(title!.x).toBeLessThan(0);
  });

  it("labels both ends of the value chain", () => {
    const out = resolveBackground(wardley, source(wardleyModel), extent);
    const ends = out.texts.filter((t) => t.className === "wardley-axis-end");

    expect(ends.map((t) => t.text)).toEqual(["Visible", "Invisible"]);
    expect(ends[0]!.y).toBeLessThan(ends[1]!.y);
  });

  it("leaves the horizontal axis without end labels when none are declared", () => {
    // Absence is as declarable as presence: wardley's evolution axis has a title and no ends.
    const out = resolveBackground(wardley, source(wardleyModel), extent);
    expect(out.texts.filter((t) => t.text === "Evolution")).toHaveLength(1);
  });
});

describe("background — regions bound to a collection", () => {
  it("draws one region per entry, positioned and sized by fractions of the extent", () => {
    const out = resolveBackground(wardley, source(wardleyModel), extent);
    const region = out.rects.find((r) => r.className === "wardley-attitude");

    expect(region).toMatchObject({ x: 100, y: 50, width: 200, height: 150 });
    expect(out.texts.find((t) => t.text === "Pioneers")).toBeTruthy();
  });
});

describe("background — gridlines and the empty cases", () => {
  it("draws a rule at a declared proportion", () => {
    const out = resolveBackground({ gridlines: [{ orientation: "horizontal", at: 0.5, className: "grid" }] }, source({}), extent);
    expect(out.lines[0]).toMatchObject({ x1: 0, y1: 250, x2: 1000, y2: 250 });
  });

  it("returns nothing at all for no background", () => {
    const out = resolveBackground(undefined, source({}), extent);
    expect(out).toEqual({ rects: [], lines: [], texts: [], circles: [] });
  });

  it("offsets everything by the extent's own origin, so a background is not pinned to 0,0", () => {
    const shifted = { x: 100, y: 200, width: 400, height: 100 };
    const out = resolveBackground({ gridlines: [{ orientation: "vertical", at: 0.5 }] }, source({}), shifted);

    expect(out.lines[0]).toMatchObject({ x1: 300, y1: 200, x2: 300, y2: 300 });
  });
});

/**
 * MARKS — the last thing the Wardley backdrop needed, and register entry G18.
 *
 * Bands, axes, gridlines and regions draw the map's frame. Its furniture - accelerators,
 * notes, numbered annotations - is collection-driven items at model positions, which is a
 * different shape from a slab or a rule and had no expression before this.
 */
describe("background — marks, the map's own furniture", () => {
  it("draws one item per entry, at the entry's own position", () => {
    const declaration: BackgroundDeclaration = {
      marks: [
        {
          each: { path: "payload.annotations" },
          x: { path: "x" },
          y: { path: "y" },
          glyph: "circle",
          radius: 11,
          label: { path: "number" },
          labelAnchor: "middle",
          tooltip: { path: "text" },
          className: "wardley-annotation",
        },
      ],
    };

    const resolved = resolveBackground(declaration, source({ annotations: [
      { x: 0.25, y: 0.5, number: "1", text: "Consider outsourcing" },
      { x: 0.75, y: 0.2, number: "2", text: "Watch this evolve" },
    ] }), extent);

    // Fractions of the extent, as every other background kind reads them.
    expect(resolved.circles.map((circle) => [circle.cx, circle.cy])).toEqual([[250, 250], [750, 100]]);
    expect(resolved.circles.map((circle) => circle.tooltip)).toEqual(["Consider outsourcing", "Watch this evolve"]);
    expect(resolved.texts.map((text) => text.text)).toEqual(["1", "2"]);
  });

  it("draws a caption with no glyph, which is what a map's note is", () => {
    const resolved = resolveBackground(
      { marks: [{ each: { path: "payload.notes" }, x: { path: "x" }, y: { path: "y" }, glyph: "none", label: { path: "text" } }] },
      source({ notes: [{ x: 0.5, y: 0.5, text: "beware the plateau" }] }),
      extent,
    );

    expect(resolved.circles).toEqual([]);
    expect(resolved.lines).toEqual([]);
    expect(resolved.texts.map((text) => text.text)).toEqual(["beware the plateau"]);
  });

  it("grows a declared label with the view, and clamps it at both ends (G19)", () => {
    // Sufficiency row 27: stage names hold a readable size as the map zooms while the
    // boundaries they name do not. Three views, one declaration - and the clamp is the
    // declaration's, because a label that grew without bound would swallow the map.
    const declaration: BackgroundDeclaration = {
      bands: [
        {
          each: { path: "payload.stages" },
          orientation: "vertical",
          start: { path: "start" },
          end: { path: "end" },
          label: { path: "label" },
          typography: { fontSize: 20, scaleWithView: { min: 0.35, max: 2.5 } },
        },
      ],
    };
    const stages = source({ stages: [{ start: 0, end: 1, label: "Genesis" }] });

    const sizeAt = (viewScale: number) => resolveBackground(declaration, stages, extent, viewScale).texts[0]!.typography!.fontSize;

    expect(sizeAt(1)).toBe(20);
    expect(sizeAt(2)).toBe(40);
    // Clamped at both ends rather than growing or vanishing without limit.
    expect(sizeAt(10)).toBe(50);
    expect(sizeAt(0.01)).toBe(7);
  });
});
