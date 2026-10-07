import { describe, expect, it } from "vitest";
import type { CustomRouteRef } from "../definition/diagramDefinition";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument } from "./disTypes";

/**
 * The mappings a read-only notation needs, which the .NET dependency graph was the first to state:
 * a node nobody may delete, a label drawn only while it has text, a double-click that runs an
 * operation rather than a menu entry, a built-in shape the module draws as a shared one, a tab
 * order, a line class and a route of the module's own.
 */

function specOf(): DislDocument {
  return {
    disl: "0.3",
    language: { id: "test", version: "1.0.0" },
    metamodel: {
      types: {
        Item: { attributes: { name: { type: "string" }, tags: { type: "string", many: true } } },
        Other: { attributes: { name: { type: "string" } } },
      },
      relations: { Uses: { source: "Item", target: "Other", directed: true } },
    },
    notation: {
      nodes: {
        Item: {
          shape: { type: "roundedRect", params: { radius: 6 } },
          labels: [
            { id: "name", text: { attribute: "name" } },
            { id: "tags", text: { cel: "self.tags.join(', ')" }, visible: { cel: "self.tags.size() > 0" } },
          ],
          accessibility: { role: "button", name: { attribute: "name" } },
          anchors: { mode: "sides", sides: ["left", "right"] },
          deletable: false,
          doubleClick: "reveal",
        },
        Other: { shape: "rect", anchors: { mode: "sides" }, doubleClick: "none" },
      },
      edges: { Uses: { line: { routing: "bezier" }, targetMarker: "arrowFilled" } },
    },
    behavior: { operations: { reveal: {} } },
    "x-test": { actions: { reveal: "t.reveal" } },
  };
}

const route: CustomRouteRef = { customRoute: "t-route", path: (from, to) => `M ${from.x} ${from.y} L ${to.x} ${to.y}` };

const BINDINGS: NotationBindings = {
  wireIds: "x-test",
  celPaths: { "self.tags.join(', ')": "payload.tagText" },
  classNames: () => [],
  celConditions: { "self.tags.size() > 0": { path: "payload.tagText", is: "non-empty" } },
  builtInShapes: { roundedRect: "span" },
  focusable: true,
  relationLineClassName: (relation) => `t-${relation.toLowerCase()}-line`,
  customRoutes: { Uses: route },
};

describe("compileNotation, for a read-only notation", () => {
  it("keeps a node nobody may delete from the delete gesture, and leaves every other node as it was", () => {
    const [item, other] = compileNotation(specOf(), BINDINGS).elementTypes;

    expect(item?.deletable).toBe(false);
    expect(other).not.toHaveProperty("deletable");
  });

  it("draws a label only while its CEL visible holds, through the module's condition for it", () => {
    const [item] = compileNotation(specOf(), BINDINGS).elementTypes;

    expect(item?.labels?.[1]).toMatchObject({ text: { path: "payload.tagText" }, when: { path: "payload.tagText", is: "non-empty" } });
    expect(item?.labels?.[0]).not.toHaveProperty("when");
  });

  it("refuses a CEL visible the module states no condition for", () => {
    expect(() => compileNotation(specOf(), { ...BINDINGS, celConditions: {} })).toThrow(/visible.*celConditions/);
  });

  it("draws a built-in shape the library has no drawing of as the shape the module names, and refuses it unnamed", () => {
    const [item] = compileNotation(specOf(), BINDINGS).elementTypes;

    expect(item?.shape).toBe("span");
    expect(() => compileNotation(specOf(), { ...BINDINGS, builtInShapes: undefined })).toThrow(/roundedRect/);
  });

  it("puts every element in the tab order when the module asks", () => {
    const [item] = compileNotation(specOf(), BINDINGS).elementTypes;

    expect(item?.accessibility).toEqual({ role: "button", focusable: true, label: { path: "payload.name" } });
    expect(compileNotation(specOf(), { ...BINDINGS, focusable: undefined }).elementTypes[0]?.accessibility).not.toHaveProperty("focusable");
  });

  it("gives a relation the module's line class and route", () => {
    const [uses] = compileNotation(specOf(), BINDINGS).relationTypes;

    expect(uses?.lineClassName).toBe("t-uses-line");
    expect(uses?.route).toBe(route);
    expect(compileNotation(specOf(), { ...BINDINGS, customRoutes: undefined }).relationTypes[0]?.route).toBe("cubic-bezier");
  });

  it("runs a node's double-click operation on the activate gesture, on that node's type alone", () => {
    const { actions } = compileNotation(specOf(), BINDINGS);

    expect(actions).toEqual([
      { id: "t.reveal", invokedBy: [{ kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element", elementTypes: ["item"] }] },
    ]);
  });

  it("refuses a double-click operation with no wire id", () => {
    expect(() => compileNotation({ ...specOf(), "x-test": { actions: {} } }, BINDINGS)).toThrow(/double-click operation "reveal"/);
  });
});
