import { describe, expect, it } from "vitest";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument } from "./disTypes";
import { libraryShapeOf } from "./shapeCatalog";

/**
 * Text a notation writes as it is: a label whose text is a literal (DISL §2.5b, a Bindable string is
 * shown as written), a mark badge that shows a glyph of text at its corner moved by its offset (§6.9),
 * and the `text` shape, a node that is only its label (Appendix B.2). The SPARQL query diagram was the
 * first to state all three: a subquery's "subquery" line, the arrow on a projected variable, and a
 * FILTER drawn as its text.
 */

function specOf(): DislDocument {
  return {
    disl: "0.3",
    language: { id: "test", version: "1.0.0" },
    metamodel: {
      types: {
        Item: { attributes: { name: { type: "string" }, shown: { type: "bool" } } },
        Note: { attributes: { text: { type: "string" } } },
      },
    },
    notation: {
      nodes: {
        Item: {
          shape: "rect",
          labels: [
            { id: "name", text: { attribute: "name" } },
            { id: "marker", text: "nested", position: { anchor: [0, 1], offset: [8, -10], align: "start" } },
          ],
          badges: [
            { id: "out", position: "top-left", offset: [8, 16], text: "→", visible: { cel: "self.shown" } },
            { id: "end", position: "top-right", offset: [-6, 12], text: { attribute: "name" } },
            { id: "dot", position: "top-left" },
          ],
        },
        Note: { shape: "text", labels: [{ id: "text", text: { attribute: "text" } }] },
      },
    },
  };
}

const BINDINGS: NotationBindings = {
  celPaths: {},
  classNames: () => [],
  celConditions: { "self.shown": { path: "payload.shown", is: "true" } },
  badgeClassName: (_type, badge) => `t-${badge.id}`,
};

describe("compileNotation, for text written as it is", () => {
  it("writes a label whose text is a literal as that text", () => {
    const [item] = compileNotation(specOf(), BINDINGS).elementTypes;

    expect(item?.labels?.[1]).toMatchObject({ text: { template: "nested" }, anchorTo: "bottom", align: "start", insetX: 8, offset: { x: 0, y: -10 } });
  });

  it("refuses a literal label text with a brace, which the library would read as a placeholder", () => {
    const spec = specOf();
    (spec.notation.nodes.Item!.labels![1] as { text: unknown }).text = "{nested}";

    expect(() => compileNotation(spec, BINDINGS)).toThrow(/label "marker".*brace/);
  });

  it("draws a mark badge with text as that text at its corner moved by its offset, on the corner's side", () => {
    const decorations = compileNotation(specOf(), BINDINGS).elementTypes[0]?.decorations;

    expect(decorations?.[0]).toEqual({
      glyph: "marker",
      anchor: "canvas",
      from: { x: { path: "bounds.left", number: { plus: 8 } }, y: { path: "bounds.top", number: { plus: 16 } } },
      text: { template: "→" },
      textAnchor: "start",
      className: "t-out",
      when: { path: "payload.shown", is: "true" },
    });
    expect(decorations?.[1]).toMatchObject({
      glyph: "marker",
      from: { x: { path: "bounds.right", number: { plus: -6 } }, y: { path: "bounds.top", number: { plus: 12 } } },
      text: { path: "payload.name" },
      textAnchor: "end",
    });
    // A mark with no text is still the library's dot.
    expect(decorations?.[2]).toMatchObject({ glyph: "circle" });
  });

  it("draws the text shape as a node with no body, only its label", () => {
    const [, note] = compileNotation(specOf(), BINDINGS).elementTypes;

    expect(libraryShapeOf("text")).toBe("none");
    expect(note?.shape).toBe("none");
    expect(note?.labels?.[0]).toMatchObject({ text: { path: "payload.text" } });
  });
});
