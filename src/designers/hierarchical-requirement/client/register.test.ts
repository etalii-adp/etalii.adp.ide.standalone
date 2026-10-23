import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { hierarchicalRequirementSurface } from "./hierarchicalRequirementDefinition";
import { registrations } from "./register";

describe("the Hierarchical Requirement Designer registration", () => {
  it("claims etalii/hierarchical-requirement and nothing else", () => {
    // Assert.
    const registration = registrations[0]!;
    expect(registrations).toHaveLength(1);
    expect(registration.matches("etalii/hierarchical-requirement")).toBe(true);
    expect(registration.matches("etalii/knowledge")).toBe(false);
    expect(registration.matches("etalii/markdown")).toBe(false);
    expect(registration.Panel).toBeDefined();
  });
});

describe("the Hierarchical Requirement Designer's surface", () => {
  interface Definition {
    metamodel: { enums: { AssessmentKind: { values: Record<string, { value: string; icon: string }> } } };
    surface: {
      kind: string;
      levels: { type: string };
      entries: { type: string };
      labels: Record<string, { on: string[]; prefix: string }>;
    };
    operations: Record<string, unknown>;
  }

  /** The designer's definition, as it is checked in. */
  function definition(): Definition {
    const source = readFileSync(join(__dirname, "..", "definition", "hierarchical-requirement.des"), "utf8");
    return JSON.parse(source.replace(/^\uFEFF/, "")) as Definition;
  }

  it("draws the levels and entries its definition names", () => {
    // Act.
    const { surface } = definition();

    // Assert.
    expect(surface.kind).toBe("hierarchy");
    expect(hierarchicalRequirementSurface.levelType).toBe(surface.levels.type);
    expect(hierarchicalRequirementSurface.entryType).toBe(surface.entries.type);
  });

  it("declares the labels its definition has, with their prefixes and the types that carry them", () => {
    // Act.
    const declared = definition().surface.labels;

    // Assert: the definition was read at all - three labels - and the surface names the same ones.
    expect(Object.keys(declared)).toHaveLength(3);
    expect(hierarchicalRequirementSurface.labels.map((label) => label.label).sort()).toEqual(Object.keys(declared).sort());
    for (const label of hierarchicalRequirementSurface.labels) {
      expect(`${label.label}: ${label.prefix} ${label.on.join(",")}`).toBe(`${label.label}: ${declared[label.label]?.prefix} ${declared[label.label]?.on.join(",")}`);
    }
  });

  it("shows each assessment kind by the icon its definition gives it, keyed by the stored form", () => {
    // Act.
    const kinds = Object.values(definition().metamodel.enums.AssessmentKind.values);

    // Assert.
    expect(kinds).toHaveLength(4);
    expect(Object.keys(hierarchicalRequirementSurface.noteIcons).sort()).toEqual(kinds.map((kind) => kind.value).sort());
    for (const kind of kinds) {
      expect(`${kind.value}: ${hierarchicalRequirementSurface.noteIcons[kind.value]}`).toBe(`${kind.value}: ${kind.icon}`);
    }
    expect(hierarchicalRequirementSurface.severalNotesIcon).toBe("mdi-comment-multiple-outline");
  });

  it("sends only operations its definition has", () => {
    // Act.
    const { operations } = definition();

    // Assert.
    for (const operation of Object.values(hierarchicalRequirementSurface.operations)) {
      expect(Object.keys(operations)).toContain(operation);
    }
  });
});
