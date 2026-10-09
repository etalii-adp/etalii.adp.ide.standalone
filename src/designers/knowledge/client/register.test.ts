import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { knowledgeTable } from "./knowledgeDefinition";
import { registrations } from "./register";

describe("the Knowledge designer registration", () => {
  it("claims etalii/knowledge and nothing else", () => {
    // Assert.
    const registration = registrations[0]!;
    expect(registrations).toHaveLength(1);
    expect(registration.matches("etalii/knowledge")).toBe(true);
    expect(registration.matches("etalii/markdown")).toBe(false);
    expect(registration.matches("generic/dependencies")).toBe(false);
    expect(registration.Panel).toBeDefined();
  });
});

describe("the Knowledge designer's table", () => {
  interface Definition {
    metamodel: { enums: { ValueType: { values: Record<string, { label: string; icon: string }> } } };
    surface: { valueTypes: Record<string, { comparisons: string[] }> };
  }

  /** The designer's definition, as it is checked in. */
  function definition(): Definition {
    const source = readFileSync(join(__dirname, "..", "definition", "knowledge.des"), "utf8");
    return JSON.parse(source.replace(/^\uFEFF/, "")) as Definition;
  }

  it("declares the value types the designer's definition has, with its names and icons", () => {
    // Act.
    const declared = definition().metamodel.enums.ValueType.values;

    // Assert: the definition was read at all - nine types - and the table names the same ones.
    expect(Object.keys(declared)).toHaveLength(9);
    expect(Object.keys(knowledgeTable.kinds).sort()).toEqual(Object.keys(declared).sort());
    for (const [name, { label, icon }] of Object.entries(declared)) {
      expect(`${name}: ${knowledgeTable.kinds[name]?.label} ${knowledgeTable.kinds[name]?.icon}`).toBe(`${name}: ${label} ${icon}`);
    }
  });

  it("offers each type the comparisons its definition stores a filter under, in its order", () => {
    // Act.
    const declared = definition().surface.valueTypes;

    // Assert.
    expect(Object.keys(declared)).toHaveLength(9);
    for (const [name, { comparisons }] of Object.entries(declared)) {
      expect(`${name}: ${knowledgeTable.kinds[name]?.comparisons?.map((comparison) => comparison.id).join(" ")}`).toBe(`${name}: ${comparisons.join(" ")}`);
    }
  });

  it("lets every type be edited and filtered", () => {
    // Assert.
    for (const [name, kind] of Object.entries(knowledgeTable.kinds)) {
      expect(`${name}: ${kind.editor}`).not.toBe(`${name}: undefined`);
      expect(`${name}: ${kind.comparisons?.length ?? 0}`).not.toBe(`${name}: 0`);
    }
  });

  it("starts a new property as text", () => {
    // Assert.
    expect(knowledgeTable.defaultKind).toBe("text");
    expect(knowledgeTable.kinds.text?.addable).not.toBe(false);
  });
});
