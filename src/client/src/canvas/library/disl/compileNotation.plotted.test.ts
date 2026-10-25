import { describe, expect, it } from "vitest";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument, DislNodeNotation } from "./disTypes";

/**
 * Nodes plotted on a coordinate system, and what they carry: a circle and a line as built-in shapes
 * (DISL §6.7), a bar beside a node as a badge at a side (§6.9), a reference attribute drawn as an edge
 * (§6.10), a node placed by one attribute and one coordinate the backend computes (§5.8), a label at a
 * corner or at an offset bound to the element and aligned from its point (§6.9), and a canvas menu that
 * is there wherever the diagram can be edited (§7).
 */

function specOf(nodes: Record<string, Partial<DislNodeNotation>> = {}): DislDocument {
  return {
    disl: "0.3",
    language: { id: "test", version: "1.0.0" },
    metamodel: {
      types: {
        Spot: { attributes: { name: { type: "string" }, across: { type: "number" }, up: { type: "number" }, held: { type: "bool" }, shift: { type: "Shift" } } },
        Target: { attributes: { spot: { type: "Spot" }, across: { type: "number" } } },
      },
    },
    notation: {
      nodes: {
        Spot: { shape: "rect", size: { fixed: [18, 18] }, ...nodes.Spot },
        Target: { shape: "rect", size: { fixed: [18, 18] }, ...nodes.Target },
      },
    },
  } as DislDocument;
}

const BINDINGS: NotationBindings = {
  celPaths: { "has(self.spot) ? self.spot.up : 0.0": "element.y", "has(self.shift) ? [self.shift.dx, self.shift.dy] : [15.0, 4.0]": "payload.shift" },
  classNames: () => [],
  badgeClassName: (_type, badge) => `t-${badge.id}`,
};

const typeOf = (spec: DislDocument, name: string) => compileNotation(spec, BINDINGS).elementTypes.find((type) => type.id === name.toLowerCase())!;

describe("compileNotation, for nodes plotted on a coordinate system", () => {
  it("draws a circle as the library's circle", () => {
    expect(typeOf(specOf({ Spot: { shape: "circle" } }), "Spot").shape).toBe("circle");
  });

  it("draws a line across or down its box, by its orientation", () => {
    expect(typeOf(specOf({ Spot: { shape: { type: "line", params: { orientation: "horizontal" } } } }), "Spot").shape).toBe("horizontal-line");
    expect(typeOf(specOf({ Spot: { shape: { type: "line", params: { orientation: "vertical" } } } }), "Spot").shape).toBe("vertical-line");
  });

  it("refuses a line that states no orientation", () => {
    expect(() => compileNotation(specOf({ Spot: { shape: "line" } }), BINDINGS)).toThrow(/draws "line" along its "orientation"/);
  });

  it("draws a line badge at a side, centred on the side's middle, as long as its size, while its attribute holds", () => {
    const spot = typeOf(
      specOf({
        Spot: {
          badges: [
            { id: "bar", position: "right", offset: [4, 0], shape: { type: "line", params: { orientation: "vertical" } }, size: 30, visible: { attribute: "held" }, tooltip: "Held" } as never,
          ],
        },
      }),
      "Spot",
    );

    expect(spot.decorations).toEqual([
      {
        glyph: "line",
        anchor: "canvas",
        from: { x: { path: "bounds.right", number: { plus: 4 } }, y: { path: "bounds.centreY", number: { plus: -15 } } },
        to: { x: { path: "bounds.right", number: { plus: 4 } }, y: { path: "bounds.centreY", number: { plus: 15 } } },
        className: "t-bar",
        tooltip: { template: "Held" },
        when: { path: "payload.held", is: "true" },
      },
    ]);
  });

  it("draws a line badge across the top, along it", () => {
    const spot = typeOf(specOf({ Spot: { badges: [{ id: "bar", position: "top", shape: { type: "line", params: { orientation: "horizontal" } }, size: 10 } as never] } }), "Spot");

    expect(spot.decorations?.[0]).toMatchObject({
      from: { x: { path: "bounds.centreX", number: { plus: -5 } }, y: { path: "bounds.top", number: { plus: 0 } } },
      to: { x: { path: "bounds.centreX", number: { plus: 5 } }, y: { path: "bounds.top", number: { plus: 0 } } },
    });
  });

  it("still refuses a badge at a side that is not a line", () => {
    expect(() => compileNotation(specOf({ Spot: { badges: [{ id: "dot", position: "right" }] } }), BINDINGS)).toThrow(/badge "dot" sits "right"/);
  });

  it("draws a reference attribute as an edge from the node that holds it to the node it names", () => {
    const spec = specOf();
    spec.notation.edges = { "Target.spot": { line: { routing: "straight" }, targetMarker: "none" } };

    const relation = compileNotation(spec, BINDINGS).relationTypes.find((type) => type.id === "target.spot")!;
    expect(relation.endpoints.source.elementTypes).toEqual(["target"]);
    expect(relation.endpoints.target.elementTypes).toEqual(["spot"]);
  });

  it("refuses an edge for an attribute that is no reference", () => {
    const spec = specOf();
    spec.notation.edges = { "Target.across": { line: { routing: "straight" } } };

    expect(() => compileNotation(spec, BINDINGS)).toThrow(/edge "Target.across" draws the attribute "across" of "Target", which names no node type/);
  });

  it("places a node by an attribute on one axis and by the backend on the other", () => {
    const target = typeOf(specOf({ Target: { placement: { x: { attribute: "across" }, y: { cel: "has(self.spot) ? self.spot.up : 0.0" } } } }), "Target");

    expect(target).not.toHaveProperty("centreOf");
  });

  it("refuses a computed coordinate beside an attribute that the module's celPaths do not say the backend computes", () => {
    expect(() => compileNotation(specOf({ Target: { placement: { x: { attribute: "across" }, y: { cel: "self.across * 2.0" } } } }), BINDINGS)).toThrow(
      /places its y at "self.across \* 2.0", which has no entry element.y in the module's celPaths/,
    );
  });

  it("puts a label at a corner inside its node", () => {
    const labels = typeOf(
      specOf({
        Target: {
          labels: [
            { id: "a", text: { attribute: "across" }, position: "top-left" },
            { id: "b", text: { attribute: "across" }, position: "bottom-right" },
          ],
        },
      }),
      "Target",
    ).labels;

    expect(labels?.[0]).toMatchObject({ anchorTo: "top", align: "start", offset: { x: 0, y: 20 } });
    expect(labels?.[1]).toMatchObject({ anchorTo: "bottom", align: "end", offset: { x: 0, y: -10 } });
  });

  it("writes a label from an offset its element binds, aligned from the offset's point", () => {
    const label = typeOf(
      specOf({
        Spot: {
          labels: [
            { id: "name", text: { attribute: "name" }, position: { anchor: [0.5, 0.5], offset: { cel: "has(self.shift) ? [self.shift.dx, self.shift.dy] : [15.0, 4.0]" }, align: "start" } as never },
          ],
        },
      }),
      "Spot",
    ).labels?.[0];

    expect(label).toMatchObject({ anchorTo: "centre", offsetBinding: { path: "payload.shift" }, textAnchor: "start" });
    expect(label).not.toHaveProperty("offset");
  });

  it("aligns a label at a fixed offset from the offset's point", () => {
    const label = typeOf(
      specOf({ Spot: { labels: [{ id: "name", text: { attribute: "name" }, position: { anchor: [0.5, 0.5], offset: [15, 4], align: "start" } as never }] } }),
      "Spot",
    ).labels?.[0];

    expect(label).toMatchObject({ anchorTo: "centre", offset: { x: 15, y: 4 }, textAnchor: "start" });
  });

  it("has a canvas menu wherever the diagram can be edited", () => {
    const spec = specOf();
    spec.toolbox = { contextMenus: [{ for: ["diagram"], when: "!env.readOnly", tools: [] }] } as never;

    expect(compileNotation(spec, BINDINGS).backgroundMenu).toBe(true);
  });
});
