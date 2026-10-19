import { describe, expect, it } from "vitest";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument } from "./disTypes";

/**
 * A card whose lines are fitted at the size its stylesheet draws them, its colours the document's, on a
 * canvas with a title above it and a key below it computed from what is drawn, whose toolbox drops land
 * on the canvas: what a C4 view states, in a specification small enough to read in one go.
 */

function specOf(canvas: unknown = undefined, drop: unknown = { targets: [{ on: ["canvas", "Card"], kind: "operation", operation: "addCard" }] }): DislDocument {
  return {
    disl: "0.3",
    language: { id: "test", version: "1.0.0" },
    metamodel: { types: { Card: { attributes: { name: { type: "string" }, kind: { type: "string" }, summary: { type: "string" } } } } },
    notation: {
      styles: {
        card: { font: { size: 14, lineHeight: 1.4 }, padding: [10, 12] },
        blue: { extends: "card", fill: "#1168bd" },
        cardKind: { font: { size: 10 } },
        cardSummary: { font: { size: 10, lineHeight: 1.4 } },
        plain: { opacity: 0.9 },
      },
      nodes: {
        Card: {
          shape: "rect",
          style: "blue",
          labels: [
            { id: "name", text: { attribute: "name" }, position: { anchor: [0.5, 0], offset: [0, 22] } },
            { id: "kind", text: { attribute: "kind" }, position: { anchor: [0.5, 0], offset: [0, 38] }, style: "cardKind", wrap: "none", overflow: "ellipsis" },
            { id: "summary", text: { attribute: "summary" }, position: { anchor: [0.5, 0], offset: [0, 58] }, style: "cardSummary", wrap: "word", maxWidth: "parent" },
          ],
        },
      } as never,
      ...(canvas !== undefined ? { canvas } : {}),
    },
    toolbox: { groups: [{ id: "add", tools: [{ id: "card", kind: "operation", operation: "addCard", mode: "drop", drop } as never] }] },
    "x-test": { tools: { card: { id: "x.toolbox.card", drop: "x.add-card" } }, actions: { addCard: "x.add-card" } },
  } as DislDocument;
}

const BINDINGS: NotationBindings = { wireIds: "x-test", celPaths: {}, classNames: () => [], builtInShapes: { rect: "styled-box" } };

const KEY = {
  visible: true,
  position: "outside-bottom",
  title: "Key",
  computed: { key: { cel: "self.legendLabel" }, label: { cel: "self.legendLabel" }, swatch: { fill: { cel: "token(self.type)" } } },
};

describe("compileNotation, for a toolbox whose drops the module answers", () => {
  it("states no drops, so that every drop reaches the module, where without it a drop on the canvas is refused", () => {
    expect(compileNotation(specOf(), { ...BINDINGS, toolboxDrops: "module" }).toolbox).toBeUndefined();
    expect(() => compileNotation(specOf(), BINDINGS)).toThrow(/tool "card" drops on the canvas/);
  });
});

describe("compileNotation, for a card whose colours are the document's", () => {
  it("gives each element type the style the module binds, and none where it binds none", () => {
    const silhouette = { path: "payload.shape" };
    const compiled = compileNotation(specOf(), { ...BINDINGS, toolboxDrops: "module", boundStyle: (type) => (type === "Card" ? { silhouette, fill: { path: "payload.background" } } : undefined) });
    expect(compiled.elementTypes[0]!.boundStyle).toEqual({ silhouette, fill: { path: "payload.background" } });
    expect(compileNotation(specOf(), { ...BINDINGS, toolboxDrops: "module" }).elementTypes[0]).not.toHaveProperty("boundStyle");
  });
});

describe("compileNotation, for a card's lines fitted at the size their style draws them", () => {
  const labels = (bindings: NotationBindings) => compileNotation(specOf(), { ...bindings, toolboxDrops: "module" }).elementTypes[0]!.labels!;

  it("fits a cut line and a wrapped one by their style's font size inside the node style's padding, the wrapped lines a font size and a line height apart", () => {
    const [name, kind, summary] = labels({ ...BINDINGS, labelFit: "style" });
    expect(name).not.toHaveProperty("fit");
    expect(kind).toMatchObject({ truncate: true, fit: { fontSize: 10, inset: 12 } });
    expect(kind).not.toHaveProperty("stack");
    expect(summary).toMatchObject({ wrap: true, fit: { fontSize: 10, inset: 12 }, stack: { lineHeight: 14 } });
  });

  it("leaves the library's own measures to a module that does not ask", () => {
    for (const label of labels(BINDINGS)) {
      expect(label).not.toHaveProperty("fit");
      expect(label).not.toHaveProperty("stack");
    }
  });

  it("refuses a fitted line whose style states no font size", () => {
    const spec = specOf();
    (spec.notation.nodes.Card!.labels![1] as { style: string }).style = "plain";
    expect(() => compileNotation(spec, { ...BINDINGS, toolboxDrops: "module", labelFit: "style" })).toThrow(/label "kind" is fitted to its node by its style, which states no font size/);
  });
});

describe("compileNotation, for a canvas's title and computed key", () => {
  const chrome = (canvas: unknown, bindings: Partial<NotationBindings> = {}) => compileNotation(specOf(canvas), { ...BINDINGS, toolboxDrops: "module", ...bindings }).chrome;
  const stated: NotationBindings["canvasChrome"] = {
    title: { className: "t-title", data: { testid: "title" } },
    legend: { entries: "payload.legend", className: "t-key", titleClassName: "t-key-title", entryClassName: "t-key-entry", swatchClassName: "t-key-swatch", data: { testid: "key" } },
  };

  it("binds the title to the diagram's attribute on the model's background, with the module's class", () => {
    expect(chrome({ title: { id: "heading", text: { attribute: "heading" }, editable: false } }, { canvasChrome: stated })).toEqual({
      title: { text: { path: "payload.heading" }, className: "t-title", data: { testid: "title" } },
    });
  });

  it("draws the computed key from the list the module names, each entry's caption and swatch the paths the key's CEL stands for", () => {
    expect(chrome({ legend: KEY }, { canvasChrome: stated, rowPaths: { "self.legendLabel": "label", "token(self.type)": "style.background" } })).toEqual({
      legend: {
        entries: { path: "payload.legend", each: { path: "label" } },
        swatchColour: { path: "style.background" },
        title: "Key",
        className: "t-key",
        titleClassName: "t-key-title",
        entryClassName: "t-key-entry",
        swatchClassName: "t-key-swatch",
        data: { testid: "key" },
      },
    });
  });

  it("states neither where the canvas has neither, or its key is hidden", () => {
    expect(chrome(undefined)).toBeUndefined();
    expect(chrome({ legend: { ...KEY, visible: false } }, { canvasChrome: stated })).toBeUndefined();
  });

  it("refuses what it cannot draw: an editable title, a key elsewhere than below, a computed key with no list, a CEL with no path", () => {
    const paths = { rowPaths: { "self.legendLabel": "label", "token(self.type)": "style.background" } };
    expect(() => chrome({ title: { text: { attribute: "heading" }, editable: true } })).toThrow(/the canvas's title is editable/);
    expect(() => chrome({ legend: { ...KEY, position: "outside-right" } }, { canvasChrome: stated, ...paths })).toThrow(/the canvas's key is drawn "outside-right"/);
    expect(() => chrome({ legend: KEY }, paths)).toThrow(/needs the list of entries the module names in canvasChrome\.legend/);
    expect(() => chrome({ legend: KEY }, { canvasChrome: stated })).toThrow(/the CEL "self\.legendLabel" has no path within a row/);
  });
});
