import { describe, expect, it } from "vitest";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument } from "./disTypes";

/**
 * The ids the canvas tells element and relation types apart by. A wire block may give several of a
 * specification's types one wire type, where the backend sends them as one kind of element; the canvas
 * then needs an id of its own for each, which the specification's names give.
 */

function specOf(): DislDocument {
  return {
    disl: "0.3",
    language: { id: "test", version: "1.0.0" },
    metamodel: {
      types: { Round: { attributes: {} }, Square: { attributes: {} } },
      relations: { Near: { source: "Round", target: "Square" }, Far: { source: "Round", target: "Square" } },
    },
    notation: {
      nodes: { Round: { shape: "rect" }, Square: { shape: "rect" } },
      edges: { Near: {}, Far: {} },
    },
    toolbox: { contextMenus: [{ for: ["Round"], tools: [{ kind: "operation", operation: "shrink", label: "Shrink", shortcut: "F3" }] }] },
    "x-test": {
      types: { Round: "test/thing", Square: "test/thing", Near: "test/link", Far: "test/link" },
      actions: { shrink: "test.shrink" },
    },
  } as unknown as DislDocument;
}

const BINDINGS: NotationBindings = { celPaths: {}, classNames: () => [], builtInShapes: { rect: "box" }, wireIds: "x-test" };

describe("compileNotation, for the ids of its types", () => {
  it("refuses two node types the wire block gives one id, which the canvas could not tell apart", () => {
    expect(() => compileNotation(specOf(), BINDINGS)).toThrow(/node types "Round" and "Square" are both "test\/thing"/);
  });

  it("refuses two edges the wire block gives one id", () => {
    const spec = specOf();
    (spec as unknown as { "x-test": { types: Record<string, string> } })["x-test"].types.Square = "test/square";

    expect(() => compileNotation(spec, BINDINGS)).toThrow(/edges "Near" and "Far" are both "test\/link"/);
  });

  it("names its types by the specification where the module asks, its actions still by the wire block", () => {
    const definition = compileNotation(specOf(), { ...BINDINGS, typeIds: "specification" });

    expect(definition.elementTypes.map((type) => type.id)).toEqual(["round", "square"]);
    expect(definition.relationTypes.map((type) => type.id)).toEqual(["near", "far"]);
    expect(definition.actions?.map((action) => [action.id, action.appliesTo])).toEqual([["test.shrink", [{ kind: "element", elementTypes: ["round"] }]]]);
  });
});
