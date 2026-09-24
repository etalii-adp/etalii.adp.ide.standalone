import { describe, expect, it } from "vitest";
import {
  BUILT_IN_ROUTES,
  type CustomRouteRef,
  type CustomShapeRef,
  type DiagramDefinition,
  type RelationTypeDefinition,
} from "./diagramDefinition";
import { assertValidDiagramDefinition, validateDiagramDefinition } from "./validateDiagramDefinition";

/** A small, entirely legal definition the bad cases are perturbations of. */
function goodDefinition(): DiagramDefinition {
  return {
    elementTypes: [
      { id: "node", shape: "box", anchors: { kind: "edge" }, sizing: "model" },
      { id: "group", shape: "frame", anchors: { kind: "edge" }, sizing: "model" },
    ],
    relationTypes: [
      {
        id: "link",
        route: "straight",
        endpoints: {
          source: { elementTypes: ["node"] },
          target: { elementTypes: ["node", "group"] },
          allowSelf: false,
        },
      },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
}

describe("validateDiagramDefinition", () => {
  it("accepts a well-formed definition without complaint", () => {
    expect(validateDiagramDefinition(goodDefinition())).toEqual([]);
  });

  it("rejects a definition allowing zero layout modes", () => {
    // A canvas that can lay out no way is a definition bug, not a runtime state
    // (design, Error Handling 4).
    const definition = { ...goodDefinition(), layout: { modes: [] } };

    const problems = validateDiagramDefinition(definition);

    expect(problems.some((problem) => problem.includes("zero modes"))).toBe(true);
  });

  it("rejects an endpoint constraint naming an element type the definition does not declare", () => {
    const definition = goodDefinition();
    const [link] = definition.relationTypes as RelationTypeDefinition[];
    link.endpoints.target = { elementTypes: ["node", "phantom"] };

    const problems = validateDiagramDefinition(definition);

    expect(problems.some((problem) => problem.includes('"phantom"') && problem.includes('"link"'))).toBe(true);
  });

  it("rejects a custom shape that supplies no renderer", () => {
    const definition = goodDefinition();
    // Constructed as unknown-first because the point IS the missing functions: a module that
    // builds its ref dynamically can produce exactly this, and the type system cannot save it.
    const hollow = { customShape: "gear" } as unknown as CustomShapeRef;
    definition.elementTypes = [{ id: "node", shape: hollow, anchors: { kind: "edge" }, sizing: "model" }];

    const problems = validateDiagramDefinition(definition);

    expect(problems.some((problem) => problem.includes('"gear"'))).toBe(true);
  });

  it("accepts a custom shape that carries its renderer and edge function", () => {
    const definition = goodDefinition();
    const shape: CustomShapeRef = {
      customShape: "gear",
      render: () => null,
      edgePoint: (bounds) => ({ x: bounds.x, y: bounds.y }),
    };
    // The "group" type stays: the relation's target constraint still names it, and dropping
    // it would trip the unknown-element-type validator instead of exercising this one.
    definition.elementTypes = [{ id: "node", shape, anchors: { kind: "edge" }, sizing: "model" }, definition.elementTypes[1]];

    expect(validateDiagramDefinition(definition)).toEqual([]);
  });

  it("rejects a custom route that supplies no path builder", () => {
    const definition = goodDefinition();
    const hollow = { customRoute: "self-loop" } as unknown as CustomRouteRef;
    (definition.relationTypes as RelationTypeDefinition[])[0].route = hollow;

    const problems = validateDiagramDefinition(definition);

    expect(problems.some((problem) => problem.includes('"self-loop"'))).toBe(true);
  });

  it("accepts a custom route that carries its path builder", () => {
    const definition = goodDefinition();
    const route: CustomRouteRef = {
      customRoute: "self-loop",
      path: (from, to) => `M ${from.x} ${from.y} L ${to.x} ${to.y}`,
    };
    (definition.relationTypes as RelationTypeDefinition[])[0].route = route;

    expect(validateDiagramDefinition(definition)).toEqual([]);
  });

  it("assertValidDiagramDefinition throws with every complaint, and passes a good one through", () => {
    expect(() => assertValidDiagramDefinition({ ...goodDefinition(), layout: { modes: [] } })).toThrow(/zero modes/);

    const good = goodDefinition();
    expect(assertValidDiagramDefinition(good)).toBe(good);
  });

  it("the built-in routes cover the four existing connector families", () => {
    // The families were once separate components, since removed; the routes are what remains.
    // straight -> the straight components; cubic-bezier -> bezier and fixed-bezier; the
    // interactive family is cubic-bezier plus adjustability on the relation type; arc is
    // causal-loop's chord-bowed link. A named-member canary each, so the population can
    // never quietly go empty (Requirement 10.2's idiom).
    expect(BUILT_IN_ROUTES).toContain("straight");
    expect(BUILT_IN_ROUTES).toContain("cubic-bezier");
    expect(BUILT_IN_ROUTES).toContain("arc");

    const interactive: RelationTypeDefinition = {
      id: "adjustable-link",
      route: "cubic-bezier",
      adjustable: true,
      endpoints: { source: { elementTypes: ["node"] }, target: { elementTypes: ["node"] }, allowSelf: false },
    };
    expect(interactive.adjustable).toBe(true);
  });
});
