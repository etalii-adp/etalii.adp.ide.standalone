import { describe, expect, it } from "vitest";
import { validateDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { ABM_DEFINITION, ABM_SHAPES } from "./AbmCanvas";
import { ABM_ACTION_IDS, ABM_CATEGORY, ABM_NODE_KINDS, ABM_PROPERTY_IDS } from "./abmIds";

describe("the agent behavior model's definition", () => {
  it("passes the library's own validation", () => {
    // Act & assert.
    expect(validateDiagramDefinition(ABM_DEFINITION)).toEqual([]);
  });

  it("declares the eleven kinds, one shape per family of composite and wrapper", () => {
    // Assert.
    expect(ABM_DEFINITION.elementTypes.map((type) => type.id)).toEqual([...ABM_NODE_KINDS]);
    for (const kind of ABM_NODE_KINDS) {
      if (ABM_CATEGORY[kind] === "composite") expect(ABM_SHAPES[kind], kind).toBe("superellipse");
      if (ABM_CATEGORY[kind] === "decorator") expect(ABM_SHAPES[kind], kind).toBe("hexagon");
    }
  });

  it("edits the label in place, never the keyword", () => {
    // Assert.
    for (const type of ABM_DEFINITION.elementTypes) {
      expect(type.labels?.map((label) => label.editable === true), type.id).toEqual([false, true]);
    }
  });

  it("draws a parent line only from a node that holds children, and never round a cycle", () => {
    // Assert.
    const child = ABM_DEFINITION.relationTypes[0];
    expect(ABM_DEFINITION.relationTypes).toHaveLength(1);
    expect(child.endpoints.source.elementTypes).toEqual(ABM_NODE_KINDS.filter((kind) => ABM_CATEGORY[kind] !== "leaf"));
    expect(child.endpoints.allowSelf).toBe(false);
    expect(ABM_DEFINITION.acyclic).toEqual([{ relationTypes: ["child"] }]);
  });

  it("names each action and property once, for the backend to answer", () => {
    // Assert: eleven adds, connect, rename, remove, two moves, notes and arrange.
    expect(ABM_ACTION_IDS).toHaveLength(18);
    expect(new Set(ABM_ACTION_IDS).size).toBe(ABM_ACTION_IDS.length);
    expect(new Set(ABM_PROPERTY_IDS).size).toBe(ABM_PROPERTY_IDS.length);
    for (const action of ABM_DEFINITION.actions ?? []) {
      expect(ABM_ACTION_IDS, action.id).toContain(action.id);
    }
  });
});
