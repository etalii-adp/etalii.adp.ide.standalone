import { describe, expect, it } from "vitest";
import { validateDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { FDG_DEFINITION } from "./FdgCanvas";
import { FDG_ACTION_IDS, FDG_PROPERTY_IDS } from "./fdgIds";

/**
 * Requirement 5's rules table, as the requirements state it. The backend states it once in
 * `FdgRelations`; the canvas states it in its declarations; this copy is what both are held to on
 * the client's side.
 */
const RULES = [
  { id: "ui-child", sources: ["ui-element"], target: "ui-element", cardinality: { maxIntoTarget: 1 } },
  { id: "owns-action", sources: ["ui-element"], target: "action", cardinality: { maxIntoTarget: 1 } },
  { id: "owns-data", sources: ["ui-element", "action", "data-element"], target: "data-element", cardinality: { maxIntoTarget: 1 } },
  { id: "owns-function", sources: ["ui-element", "action", "data-element", "function"], target: "function", cardinality: { maxIntoTarget: 1 } },
  { id: "shows", sources: ["action"], target: "ui-element", cardinality: { maxFromSource: 1 } },
];

describe("the functional decomposition graph's definition", () => {
  it("passes the library's own validation", () => {
    // Act & assert.
    expect(validateDiagramDefinition(FDG_DEFINITION)).toEqual([]);
  });

  it("draws the five types on the design's shapes, all sized by the reader", () => {
    // Assert.
    const shapes = Object.fromEntries(FDG_DEFINITION.elementTypes.map((type) => [type.id, type.shape]));
    expect(shapes).toEqual({
      "ui-element": "superellipse",
      "data-element": "parallelogram",
      action: "trapezoid",
      function: "diode",
      comment: "box",
    });
    expect(FDG_DEFINITION.elementTypes.every((type) => type.sizing === "user")).toBe(true);
  });

  it("gives only the Comment a height of its own, wrapped text, and no anchors", () => {
    // Assert.
    for (const type of FDG_DEFINITION.elementTypes) {
      const isComment = type.id === "comment";
      expect(type.resize === "both", type.id).toBe(isComment);
      expect(type.labels?.[0].wrap === true, type.id).toBe(isComment);
      expect(type.labels?.[0].editable, type.id).toBe(true);
      expect(type.anchors.enabled === false && type.anchors.visible === false, type.id).toBe(isComment);
    }
  });

  it("states the rules table exactly: endpoints, limits, no self-links, a curve with an arrow and a midpoint name", () => {
    // Assert.
    expect(FDG_DEFINITION.relationTypes.map((relation) => relation.id)).toEqual(RULES.map((rule) => rule.id));
    for (const rule of RULES) {
      const relation = FDG_DEFINITION.relationTypes.find((candidate) => candidate.id === rule.id)!;
      expect(relation.endpoints.source.elementTypes, rule.id).toEqual(rule.sources);
      expect(relation.endpoints.target.elementTypes, rule.id).toEqual([rule.target]);
      expect(relation.endpoints.cardinality, rule.id).toEqual(rule.cardinality);
      expect(relation.endpoints.allowSelf, rule.id).toBe(false);
      expect(relation.route, rule.id).toBe("cubic-bezier");
      expect(relation.style, rule.id).toEqual({ endMarker: "arrow" });
      expect(relation.label, rule.id).toEqual({ placement: "midpoint", editable: true });
    }
  });

  it("forbids a cycle among the four ownership relations and deliberately not through Shows", () => {
    // Assert.
    expect(FDG_DEFINITION.acyclic).toEqual([{ relationTypes: ["ui-child", "owns-action", "owns-data", "owns-function"] }]);
  });

  it("lays out by hand only", () => {
    // Assert.
    expect(FDG_DEFINITION.layout.modes).toEqual(["manual"]);
    expect(FDG_DEFINITION.dragging).toBe("enabled");
  });

  it("names each action and property once, for the backend to answer", () => {
    // Assert: five adds, five connects, remove, disconnect, and two renames.
    expect(FDG_ACTION_IDS).toHaveLength(14);
    expect(new Set(FDG_ACTION_IDS).size).toBe(FDG_ACTION_IDS.length);
    expect(new Set(FDG_PROPERTY_IDS).size).toBe(FDG_PROPERTY_IDS.length);
    for (const action of FDG_DEFINITION.actions ?? []) {
      expect(FDG_ACTION_IDS, action.id).toContain(action.id);
    }
  });
});
