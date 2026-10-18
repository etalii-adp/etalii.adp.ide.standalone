import { describe, expect, it } from "vitest";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument } from "./disTypes";

/**
 * A toolbox entry that means something only on an element (DISL 7.2 `drop.targets`): each operation
 * target becomes the library's `toolbox.drops`, its `on` read as the concrete types it stands for,
 * its payload and its actions under their wire ids, and `elsewhere` as a refusal or a silence.
 */

function specOf(drop: unknown, tools: Record<string, unknown> = { card: { id: "x.toolbox.card", drop: "x.add-card" } }): DislDocument {
  return {
    disl: "0.3",
    language: { id: "test", version: "1.0.0" },
    metamodel: {
      types: {
        Holder: { abstract: true, attributes: { name: { type: "string" } } },
        Lane: { extends: "Holder" },
        Card: { extends: "Holder" },
        Note: { attributes: { text: { type: "string" } } },
      },
    },
    notation: {
      nodes: {
        Lane: { shape: { type: "rect" } },
        Card: { shape: { type: "rect" } },
        Note: { shape: { type: "rect" } },
      } as never,
    },
    toolbox: {
      groups: [
        { id: "add", tools: [{ id: "card", kind: "operation", operation: "addCard", mode: "drop", drop } as never] },
      ],
    },
    "x-test": { tools, actions: { addCard: "x.add-card", addCardAfter: "x.add-card-after" } },
  } as DislDocument;
}

const BINDINGS: NotationBindings = { celPaths: {}, classNames: () => [], builtInShapes: { rect: "box" }, wireIds: "x-test" };

const toolbox = (drop: unknown, tools?: Record<string, unknown>) => compileNotation(specOf(drop, tools), BINDINGS).toolbox;

describe("compileNotation, for a toolbox entry dropped on elements", () => {
  it("compiles each target in order, its types the concrete ones it names, under the wire ids", () => {
    expect(toolbox({
      targets: [
        { on: ["Card"], kind: "operation", operation: "addCardAfter" },
        { on: ["Holder"], kind: "operation", operation: "addCard" },
      ],
      elsewhere: "refuse",
      refusal: "Drop a card on a lane.",
    })).toEqual({
      drops: [{
        payload: "x.add-card",
        targets: [
          { elementTypes: ["card"], actionId: "x.add-card-after" },
          { elementTypes: ["lane", "card"], actionId: "x.add-card" },
        ],
        refusal: "Drop a card on a lane.",
      }],
    });
  });

  it("ignores a drop elsewhere in silence when the entry says so", () => {
    expect(toolbox({ targets: [{ on: ["Lane"], kind: "operation", operation: "addCard" }], elsewhere: "ignore" })!.drops![0]).not.toHaveProperty("refusal");
  });

  it("states no drops for a toolbox none of whose entries names targets", () => {
    expect(toolbox(undefined)).toBeUndefined();
  });

  it("refuses what it cannot run: another kind of target, the canvas, a create elsewhere, a computed refusal, a tool with no wire id", () => {
    const target = { on: ["Lane"], kind: "operation", operation: "addCard" };
    expect(() => toolbox({ targets: [{ on: ["Lane"], kind: "connect", via: "Link" }], elsewhere: "refuse", refusal: "No." })).toThrow(/tool "card" drops a "connect" target/);
    expect(() => toolbox({ targets: [{ ...target, on: ["canvas"] }], elsewhere: "ignore" })).toThrow(/tool "card" drops on the canvas/);
    expect(() => toolbox({ targets: [target] })).toThrow(/tool "card" does "create" with a drop elsewhere/);
    expect(() => toolbox({ targets: [target], elsewhere: "refuse", refusal: { cel: "'No.'" } })).toThrow(/tool "card" refuses a drop elsewhere with a sentence that is not text/);
    expect(() => toolbox({ targets: [target], elsewhere: "ignore" }, {})).toThrow(/tool "card" has no drop id in x-test\.tools/);
  });
});
