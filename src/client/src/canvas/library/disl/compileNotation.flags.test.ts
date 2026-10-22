import { describe, expect, it } from "vitest";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument, DislRelation } from "./disTypes";

/**
 * A relation type's ends and flags come from its supertypes along their C3 linearisation, the nearest type that
 * states one winning (DISL 0.6, §4.7), and a relation type that widens a flag or inherits two different values
 * of one is refused, as the backend refuses the same specification.
 */

const BINDINGS: NotationBindings = { celPaths: {}, classNames: () => [] };

function specWith(relations: Readonly<Record<string, Omit<DislRelation, "source" | "target"> & Partial<Pick<DislRelation, "source" | "target">>>>, drawn: string): DislDocument {
  return {
    disl: "0.6",
    language: { id: "test", version: "1.0.0" },
    metamodel: {
      types: { Box: {} },
      relations: relations as DislDocument["metamodel"]["relations"],
    },
    notation: {
      nodes: { Box: { shape: "rect" } },
      edges: { [drawn]: { targetMarker: "arrow" } },
    },
  };
}

function endpointsOf(spec: DislDocument) {
  return compileNotation(spec, BINDINGS).relationTypes[0]!.endpoints;
}

describe("compileNotation, relation flags", () => {
  it("takes the ends and flags of a relation type that extends a list of supertypes along their C3 linearisation", () => {
    const spec = specWith(
      {
        Ends: { abstract: true, source: "Box", target: "Box", allowParallel: false },
        Once: { abstract: true, allowParallel: false },
        Both: { extends: ["Ends", "Once"] },
      },
      "Both",
    );
    expect(endpointsOf(spec)).toEqual({ source: { elementTypes: ["box"] }, target: { elementTypes: ["box"] }, allowSelf: false, cardinality: { perPair: "ordered" } });
  });

  it("follows inherited values for perPair and allowSelf, its ends coming from the second supertype's chain", () => {
    const spec = specWith(
      {
        Base: { abstract: true, source: "Box", target: "Box", directed: false, allowSelfLoops: true, allowParallel: false },
        Middle: { abstract: true, extends: "Base" },
        Tag: { abstract: true, directed: false, allowSelfLoops: true, allowParallel: false },
        Leaf: { extends: ["Tag", "Middle"] },
      },
      "Leaf",
    );
    expect(endpointsOf(spec)).toMatchObject({ allowSelf: true, cardinality: { perPair: "unordered" } });
  });

  it("refuses a relation type that widens an inherited flag, naming the type and the flag", () => {
    const spec = specWith({ Link: { source: "Box", target: "Box", allowParallel: false }, Strong: { extends: "Link", allowParallel: true } }, "Strong");
    expect(() => compileNotation(spec, BINDINGS)).toThrow(/"Strong".*allowParallel.*"Link"/);
  });

  it("refuses a relation type that states a directed differing from its supertype's default, naming the type and the flag", () => {
    const spec = specWith({ Link: { source: "Box", target: "Box" }, Mutual: { extends: "Link", directed: false } }, "Mutual");
    expect(() => compileNotation(spec, BINDINGS)).toThrow(/"Mutual".*directed.*"Link"/);
  });

  it("refuses a relation type whose supertypes differ in a flag it does not state, naming the type and the flag", () => {
    const spec = specWith(
      { Loop: { source: "Box", target: "Box", allowSelfLoops: true }, Plain: { source: "Box", target: "Box" }, Both: { extends: ["Loop", "Plain"] } },
      "Both",
    );
    expect(() => compileNotation(spec, BINDINGS)).toThrow(/"Both".*allowSelfLoops/);
  });
});
