import { describe, expect, it } from "vitest";
import { libraryShapeOf } from "@client/canvas/library/disl/shapeCatalog";
import type { DislShapeRef } from "@client/canvas/library/disl/disTypes";
import disText from "../definition/agent-activity-diagram.dis?raw";
import { AAD_DEFINITION } from "./aadDefinition";

/**
 * The canvas's definition is declared by hand until the client's DISL reader reads 0.4, and the
 * bundled `.dis` is what every other host reads. This holds the two together where they can be
 * compared (agent-activity-diagram Requirement 10.2): a change to the definition that the canvas
 * does not follow, or a change to the canvas the definition does not have, fails here.
 */

interface Spec {
  metamodel: {
    enums: Record<string, { values: Record<string, { label?: string }> }>;
    relations: Record<string, { source: string; target: string }>;
  };
  notation: {
    nodes: Record<string, {
      shape: DislShapeRef;
      compartments?: { id: string; title?: string; groupBy?: { attribute: string }; itemOrder?: { by: string; direction: string } }[];
      badges?: { id: string }[];
      labels?: { id: string; link?: unknown }[];
    }>;
    canvas: { filters: Record<string, { label: string; control: string; persist?: boolean }> };
  };
  layout: { algorithms: Record<string, { algorithm: string; force?: { tiers: string[] } }>; default: string; respect?: string };
}

const SPEC = JSON.parse(disText) as Spec;

/** A type's id on the canvas: its name in the definition, in lower case. */
const idOf = (name: string) => name.toLowerCase();

/**
 * Shapes the definition names that this library does not draw from DISL yet, and what stands in.
 * An entry here is a debt: the test below fails when the library can draw the shape itself.
 */
const STAND_INS: Readonly<Record<string, string>> = {
  // DISL's folder: no library shape. The superellipse is the one no other kind of element has.
  Project: "superellipse",
  // DISL's roundedRect: the library has the shape, and its DISL catalog does not list it yet.
  Specification: "rounded-rectangle",
};

const typeOf = (name: string) => AAD_DEFINITION.elementTypes.find((type) => type.id === idOf(name));

describe("the agent activity diagram's canvas definition, held to the bundled .dis", () => {
  it("draws the definition's element types, and no other", () => {
    expect(AAD_DEFINITION.elementTypes.map((type) => type.id).sort()).toEqual(Object.keys(SPEC.notation.nodes).map(idOf).sort());
  });

  it("draws each in the shape the definition names, or in a stand-in the library still owes", () => {
    for (const [name, node] of Object.entries(SPEC.notation.nodes)) {
      const fromDisl = (() => {
        try {
          return libraryShapeOf(node.shape);
        } catch {
          return undefined; // a built-in asked for with parameters the library does not draw
        }
      })();
      const standIn = STAND_INS[name];

      if (standIn === undefined) {
        expect(fromDisl, `${name}: the library draws no "${JSON.stringify(node.shape)}"; name a stand-in`).toBeDefined();
        expect(typeOf(name)?.shape, name).toBe(fromDisl);
      } else {
        expect(fromDisl, `${name}: the library now draws this shape itself; drop the stand-in`).toBeUndefined();
        expect(typeOf(name)?.shape, name).toBe(standIn);
      }
    }

    // Requirement 7.2: a kind is told apart by its shape alone.
    const shapes = AAD_DEFINITION.elementTypes.map((type) => type.shape);
    expect(new Set(shapes).size).toBe(shapes.length);
  });

  it("places the types on the definition's rings, in its order, and keeps what the reader locked", () => {
    const rings = SPEC.layout.algorithms[SPEC.layout.default]!;

    expect(rings.algorithm).toBe("force");
    expect(AAD_DEFINITION.layout.modes).toEqual(["tiered-force"]);
    expect(AAD_DEFINITION.layout.tiers).toEqual(rings.force!.tiers.map((type) => [idOf(type)]));
    expect(SPEC.layout.respect).toBe("pinned");
    expect(AAD_DEFINITION.layout.pinned).toBe("payload.pinned");
  });

  it("joins exactly the pairs the definition's relations join, each end the type the definition gives it", () => {
    const declared = Object.values(SPEC.metamodel.relations).map((relation) => `${idOf(relation.source)}>${idOf(relation.target)}`).sort();
    const drawn = AAD_DEFINITION.relationTypes.map((type) => `${type.endpoints.source.elementTypes.join("|")}>${type.endpoints.target.elementTypes.join("|")}`).sort();

    expect(drawn).toEqual(declared);
    expect(AAD_DEFINITION.relationTypes.every((type) => type.endpoints.allowSelf === false)).toBe(true);
  });

  it("lists a specification's tasks under the definition's statuses, in its order and under its words", () => {
    const tasks = SPEC.notation.nodes.Specification!.compartments!.find((compartment) => compartment.id === "tasks")!;
    const statuses = Object.entries(SPEC.metamodel.enums.TaskStatus!.values);
    const drawn = typeOf("Specification")!.compartments!.find((compartment) => compartment.id === "tasks")!;

    expect(tasks.groupBy!.attribute).toBe("status");
    expect(drawn.groupBy!.path).toBe("status");
    expect(drawn.groupBy!.groups).toEqual(statuses.map(([value, member]) => ({ value, title: member.label })));
  });

  it("orders every list as the definition orders it", () => {
    for (const [name, node] of Object.entries(SPEC.notation.nodes)) {
      for (const compartment of (node.compartments ?? []).filter((candidate) => candidate.itemOrder !== undefined)) {
        const drawn = typeOf(name)!.compartments!.find((candidate) => candidate.id === compartment.id);

        expect(drawn, `${name}.${compartment.id}`).toBeDefined();
        expect(`item.${drawn!.orderBy!.path}`, `${name}.${compartment.id}`).toBe(compartment.itemOrder!.by);
        expect(drawn!.orderBy!.direction, `${name}.${compartment.id}`).toBe(compartment.itemOrder!.direction);
        if (compartment.title !== undefined) {
          expect(drawn!.title).toBe(compartment.title);
        }
      }
    }
  });

  it("gives a link symbol to every type the definition gives a link badge", () => {
    for (const [name, node] of Object.entries(SPEC.notation.nodes)) {
      const badged = (node.badges ?? []).some((badge) => badge.id === "link");

      expect((typeOf(name)!.links ?? []).length > 0, name).toBe(badged);
    }
  });

  it("puts one switch on the canvas for each filter the definition keeps, under its caption", () => {
    const kept = Object.values(SPEC.notation.canvas.filters).filter((filter) => filter.control === "switch" && filter.persist === true);

    expect((AAD_DEFINITION.chrome?.switches ?? []).map((declared) => declared.caption)).toEqual(kept.map((filter) => filter.label));
  });
});
