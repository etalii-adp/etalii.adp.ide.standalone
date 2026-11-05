import { describe, expect, it } from "vitest";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument } from "./disTypes";
import { libraryShapeOf } from "./shapeCatalog";

/**
 * Text a notation writes as it is: a label whose text is a literal (DISL §2.5b, a Bindable string is
 * shown as written), a mark badge that shows a glyph of text at its corner moved by its offset (§6.9),
 * and the `text` shape, a node that is only its label (Appendix B.2): a fixed second line under a
 * node's name, an arrow marking a node at its corner, and a constraint drawn as nothing but its text.
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

  it("starts the text of a text shape placed by its left edge at that edge, and ends one placed by its right edge there", () => {
    // The shape is only its text (Appendix B.2), so where the placement puts the node's edge is where
    // the text starts or ends - exactly, whatever width the library estimates for it.
    const spec = specOf();
    const placed = (anchor: string) => ({ ...spec.notation.nodes.Note!, placement: { anchor } });
    spec.notation.nodes = { Item: spec.notation.nodes.Item!, Note: placed("left") };
    const left = compileNotation(spec, BINDINGS).elementTypes[1];
    spec.notation.nodes = { Item: spec.notation.nodes.Item!, Note: placed("right") };
    const right = compileNotation(spec, BINDINGS).elementTypes[1];
    spec.notation.nodes = { Item: spec.notation.nodes.Item!, Note: placed("center") };
    const centre = compileNotation(spec, BINDINGS).elementTypes[1];

    expect(left?.labels?.[0]).toMatchObject({ anchorTo: "centre", align: "start", insetX: 0 });
    expect(right?.labels?.[0]).toMatchObject({ anchorTo: "centre", align: "end", insetX: 0 });
    expect(centre?.labels?.[0]?.align).toBeUndefined();
  });
});
