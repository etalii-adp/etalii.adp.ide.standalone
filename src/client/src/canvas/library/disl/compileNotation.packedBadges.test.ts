import { describe, expect, it } from "vitest";
import { resolveDecorations } from "../definition/decorations";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument } from "./disTypes";

/**
 * Packed badges (DISL 0.4 §6.9, `node.badgeLayout`): the badges with `pack: true` whose `visible` holds
 * take consecutive slots in declaration order, the first at the layout's `start` moved by its `offset`,
 * each next one `spacing` further in its `direction`, and a badge that is not visible takes no slot.
 * Then the mappings a box with a rounded corner, a count that declines its noun and a module's own
 * attributes and ornaments need.
 */

/** Where the renderer says a 200 by 100 box at the origin is drawn. */
const BOUNDS = { left: 0, top: 0, right: 200, bottom: 100, width: 200, height: 100, centreX: 100, centreY: 50 };

function specOf(badgeLayout: unknown = { start: "top-right", offset: [-12, 12], direction: "left", spacing: 16 }): DislDocument {
  return {
    disl: "0.3",
    language: { id: "test", version: "1.0.0" },
    metamodel: { types: { Box: { attributes: { name: { type: "string" }, count: { type: "int" }, off: { type: "bool" }, note: { type: "string" } } } } },
    notation: {
      nodes: {
        Box: {
          shape: { type: "roundedRect", params: { radius: 6 } },
          labels: [
            { id: "name", text: { cel: "self.name != '' ? self.name : 'box'" }, position: { anchor: [0, 0], offset: [12, 24], align: "start" }, editable: false },
            { id: "count", text: { cel: "string(self.count) + (self.count == 1 ? ' item' : ' items')" }, position: { anchor: [0, 0], offset: [12, 44], align: "start" }, editable: false },
          ],
          ...(badgeLayout !== null ? { badgeLayout: badgeLayout as never } : {}),
          badges: [
            { id: "off", pack: true, text: "⊘", style: "indicator", visible: { cel: "self.off" }, tooltip: "Off." },
            { id: "note", pack: true, text: { cel: "'#' + self.note" }, style: "indicator", visible: { cel: "has(self.note) && self.note != ''" }, tooltip: { cel: "'Note: ' + self.note" } },
          ],
        } as never,
      },
    },
  };
}

const BINDINGS: NotationBindings = {
  celPaths: { "self.name != '' ? self.name : 'box'": "payload.name" },
  celConditions: { "has(self.note) && self.note != ''": { path: "payload.note", is: "non-empty" } },
  classNames: () => [],
  builtInShapes: { roundedRect: "rounded-rectangle" },
  badgeClassName: () => "t-badge",
};

const box = (spec = specOf(), bindings = BINDINGS) => compileNotation(spec, bindings).elementTypes[0]!;

describe("compileNotation, for packed badges", () => {
  it("compiles each packed badge into a slot of the node's badge layout, in declaration order", () => {
    expect(box().decorations).toEqual([
      {
        glyph: "marker",
        anchor: "canvas",
        from: { x: { path: "bounds.right", number: { plus: -12 } }, y: { path: "bounds.top", number: { plus: 12 } } },
        pack: { step: { x: -16, y: 0 } },
        text: { template: "⊘" },
        textAnchor: "end",
        className: "t-badge",
        tooltip: { template: "Off." },
        when: { path: "payload.off", is: "true" },
      },
      {
        glyph: "marker",
        anchor: "canvas",
        from: { x: { path: "bounds.right", number: { plus: -12 } }, y: { path: "bounds.top", number: { plus: 12 } } },
        pack: { step: { x: -16, y: 0 } },
        text: { template: "#{payload.note}" },
        textAnchor: "end",
        className: "t-badge",
        tooltip: { template: "Note: {payload.note}" },
        when: { path: "payload.note", is: "non-empty" },
      },
    ]);
  });

  it("starts the row at any corner and runs it in any direction", () => {
    const [first] = box(specOf({ start: "bottom-left", offset: [4, -4], direction: "up", spacing: 10 })).decorations!;

    expect(first).toMatchObject({
      from: { x: { path: "bounds.left", number: { plus: 4 } }, y: { path: "bounds.bottom", number: { plus: -4 } } },
      pack: { step: { x: 0, y: -10 } },
      textAnchor: "middle",
    });
    expect(box(specOf({ start: "top-left", offset: [0, 0], direction: "right", spacing: 8 })).decorations![0]).toMatchObject({ pack: { step: { x: 8, y: 0 } }, textAnchor: "start" });
  });

  it("refuses a packed badge without a layout, and a layout that starts where it cannot place one", () => {
    expect(() => box(specOf(null))).toThrow(/badge "off" is packed, and the node states no badgeLayout/);
    expect(() => box(specOf({ start: "middle", offset: [0, 0], direction: "left", spacing: 16 }))).toThrow(/starts its badges at "middle"/);
  });

  it("draws a visible packed badge in the next free slot, and leaves none for one that is hidden", () => {
    const decorations = box().decorations!;
    const at = (payload: unknown) =>
      resolveDecorations(decorations, { element: { id: "b", type: "box", x: 100, y: 50 }, payload, bounds: BOUNDS }, { x: 0, y: 0, width: 200, height: 100 })
        .map((resolved) => [resolved.text, resolved.textAt.x, resolved.textAt.y]);

    expect(at({ off: true, note: "a" })).toEqual([["⊘", 188, 12], ["#a", 172, 12]]);
    // The first is hidden, so the second takes the first slot.
    expect(at({ off: false, note: "a" })).toEqual([["#a", 188, 12]]);
    expect(at({ off: false, note: "" })).toEqual([]);
  });
});

describe("compileNotation, for a rounded rectangle, a count and a module's own additions", () => {
  it("draws a rounded rectangle with the corner radius the reference asks for", () => {
    expect(box().shape).toBe("rounded-rectangle");
    expect(box().style).toEqual({ cornerRadius: 6 });
  });

  it("paints a node on the background layer beneath the connections, and any other node above them", () => {
    const spec = specOf();
    const layered = { ...spec, notation: { ...spec.notation, nodes: { Box: { ...spec.notation.nodes.Box!, layer: "background" } } } } as DislDocument;

    expect(box(layered).beneathConnections).toBe(true);
    expect(box()).not.toHaveProperty("beneathConnections");
  });

  it("reads a count that declines its noun as the library's plural", () => {
    expect(box().labels![1]!.text).toEqual({ path: "payload.count", plural: { one: "item", other: "items" } });
  });

  it("adds the module's attributes, ornaments and badge attributes where it states them", () => {
    const type = box(specOf(), {
      ...BINDINGS,
      elementData: () => ({ testid: { template: "box-{element.id}" } }),
      elementDecorations: () => [{ glyph: "marker", text: { path: "payload.mark" } }],
      badgeData: (_type, badge) => ({ testid: `badge-${badge.id}` }),
    });

    expect(type.data).toEqual({ testid: { template: "box-{element.id}" } });
    expect(type.decorations!.map((decoration) => decoration.data)).toEqual([{ testid: "badge-off" }, { testid: "badge-note" }, undefined]);
    expect(type.decorations![2]).toEqual({ glyph: "marker", text: { path: "payload.mark" } });
    // Stated nowhere, none of them is added.
    expect(box()).not.toHaveProperty("data");
  });
});

describe("compileNotation, for a layout the host computes", () => {
  it("draws a layered layout where the host puts each element, and still refuses an algorithm it has never heard of", () => {
    const laidOut = (algorithm: string) => compileNotation({ ...specOf(), layout: { algorithms: { main: { algorithm } }, default: "main" } } as DislDocument, BINDINGS).layout;

    expect(laidOut("layered")).toEqual({ modes: ["manual"] });
    expect(() => laidOut("sideways")).toThrow(/layout algorithm "sideways" has no library mode/);
  });
});
