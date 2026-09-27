import { describe, expect, it } from "vitest";
import { effectiveDefinition } from "./diagramRuntimeConfig";
import type { DiagramDefinition } from "../definition/diagramDefinition";

const definition: DiagramDefinition = {
  elementTypes: [{ id: "node", shape: "box", anchors: { kind: "edge" }, sizing: "user" }],
  relationTypes: [],
  layout: {
    modes: ["manual", "row-packed"],
    rowPacked: { width: 48, gap: 4 },
    modeOverrides: {
      "row-packed": {
        elementTypes: [{ id: "node", shape: "box", anchors: { kind: "edge" }, sizing: "model" }],
        dragging: "disabled",
      },
    },
  },
  dragging: "enabled",
};

describe("the effective definition under a layout mode", () => {
  it("applies the active mode's overrides, and only that mode's", () => {
    expect(effectiveDefinition(definition, undefined, "row-packed").elementTypes[0].sizing).toBe("model");
    expect(effectiveDefinition(definition, undefined, "row-packed").dragging).toBe("disabled");
    expect(effectiveDefinition(definition, undefined, "manual")).toBe(definition);
    expect(effectiveDefinition(definition, undefined)).toBe(definition);
  });

  it("never lets a mode replace the layout, so the modes hold still while switching", () => {
    expect(effectiveDefinition(definition, undefined, "row-packed").layout).toBe(definition.layout);
  });

  it("lets the runtime's overrides win over the mode's", () => {
    const effective = effectiveDefinition(definition, { dragging: "enabled", definitionOverrides: { elementTypes: [] } }, "row-packed");

    expect(effective.dragging).toBe("enabled");
    expect(effective.elementTypes).toEqual([]);
  });
});
