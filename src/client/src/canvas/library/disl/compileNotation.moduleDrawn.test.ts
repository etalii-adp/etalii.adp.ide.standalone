import { describe, expect, it } from "vitest";
import type { DecorationDeclaration, ElementTypeDefinition, LabelDeclaration } from "../definition/diagramDefinition";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument } from "./disTypes";

/**
 * What a module draws itself where the notation only approximates the drawing, and the parts of a custom
 * shape the library shape is drawn with:
 * - a label, an edge's mid-markers and its target marker named in `drawnByModule` are left out of the
 *   compiled definition, and a name that states nothing is refused;
 * - the module's own labels (`elementLabels`) follow the specification's, and its adornment of an edge
 *   (`edgeAdornments`) is the relation's `adorn`;
 * - a custom shape's binding gives the ornaments its parts are drawn as (`decorations`), first among the
 *   element's decorations, with the node's notation and its shape's parameters to read them from;
 * - a node in DISL's `sides` mode offers a handle at the middle of each allowed side where the module asks;
 * - an element type of `extras` is added after the compiled ones.
 */

const BINDINGS: NotationBindings = { celPaths: {}, classNames: () => [] };

function spec(): DislDocument {
  return {
    disl: "0.3",
    language: { id: "test", version: "1.0.0" },
    metamodel: {
      types: { Card: { attributes: { name: { type: "string" }, share: { type: "number" } } } },
      relations: { Link: { source: "Card", target: "Card" } },
    },
    notation: {
      shapes: {
        bandedCard: {
          params: { share: { type: "number", default: 0 } },
          parts: [
            { id: "body", shape: { type: "roundedRect" } as never, box: { x: 0, y: 0, w: "w", h: "h" } },
            { id: "band", shape: { type: "roundedRect" } as never, box: { x: 0, y: 0, w: "w", h: 20 } },
          ],
        },
      },
      nodes: {
        Card: {
          shape: { type: "bandedCard", params: { share: { attribute: "share" } } },
          size: { fixed: [100, 50] },
          partStyles: { band: "bandStyle" },
          labels: [
            { id: "name", text: { attribute: "name" } },
            { id: "amount", text: { attribute: "share" }, position: "bottom" },
          ],
        } as never,
      },
      edges: {
        Link: {
          targetMarker: "arrowFilled",
          midMarkers: [{ marker: "dot", repeat: { spacing: 14 } }] as never,
          labels: [{ id: "first", text: { attribute: "name" } }, { id: "second", text: { attribute: "name" } }] as never,
        },
      },
    },
  };
}

const CARD: NotationBindings = {
  ...BINDINGS,
  customShapes: {
    bandedCard: {
      shape: "rounded-rectangle",
      decorations: ({ shape, params, bind, node }) => [
        {
          glyph: "rect",
          from: { x: -50, y: -25 },
          width: shape.parts!.length * 50,
          height: 20,
          className: `${String((node as { partStyles?: Record<string, string> }).partStyles?.band)} ${JSON.stringify(bind(params.share))}`,
        },
      ],
    },
  },
};

/** What the edge states that the library cannot draw on a straight line, left to the module. */
const EDGE_EXTRAS = ["Link/labels/second", "Link/midMarkers"];

const DRAWN_BY_MODULE: NotationBindings = {
  ...CARD,
  drawnByModule: ["Card/labels/amount", ...EDGE_EXTRAS, "Link/targetMarker"],
};

function card(bindings: NotationBindings): ElementTypeDefinition {
  return compileNotation(spec(), { drawnByModule: EDGE_EXTRAS, ...bindings }).elementTypes[0]!;
}

describe("compileNotation, what the module draws itself", () => {
  it("draws a custom shape's parts as the ornaments its binding gives, reading the node and the shape's parameters", () => {
    expect(card(CARD).decorations).toEqual<DecorationDeclaration[]>([
      { glyph: "rect", from: { x: -50, y: -25 }, width: 100, height: 20, className: 'bandStyle {"path":"payload.share"}' },
    ]);
  });

  it("puts the parts first, before the module's own ornaments", () => {
    const mark: DecorationDeclaration = { glyph: "marker", className: "mine" };
    const decorations = card({ ...CARD, elementDecorations: () => [mark] }).decorations!;

    expect(decorations.map((decoration) => decoration.className)).toEqual(['bandStyle {"path":"payload.share"}', "mine"]);
  });

  it("leaves out a label the module draws, and adds the module's labels after the specification's", () => {
    const unit: LabelDeclaration = { text: { path: "payload.unit" }, className: "unit" };
    const labels = card({ ...DRAWN_BY_MODULE, elementLabels: () => [unit] }).labels!;

    expect(labels.map((label) => label.text)).toEqual([{ path: "payload.name" }, { path: "payload.unit" }]);
  });

  it("leaves an edge's second label, its mid-markers and its target marker to the module, and adorns the line with the module's adornment", () => {
    const adorn = () => null;
    const relation = compileNotation(spec(), { ...DRAWN_BY_MODULE, edgeAdornments: { Link: adorn } }).relationTypes[0]!;

    expect(relation.style).toEqual({ endMarker: "none" });
    expect(relation.adorn).toBe(adorn);
    expect(relation.label).toBeDefined();
  });

  it("refuses an edge of two labels and of mid-markers on a straight line while the module draws neither", () => {
    expect(() => compileNotation(spec(), CARD)).toThrow(/markers|labels/);
  });

  it("refuses a name the specification does not state", () => {
    expect(() => compileNotation(spec(), { ...DRAWN_BY_MODULE, drawnByModule: ["Card/labels/nothing"] })).toThrow(/"Card\/labels\/nothing"/);
    expect(() => compileNotation(spec(), { ...DRAWN_BY_MODULE, drawnByModule: ["Card/midMarkers"] })).toThrow(/"Card\/midMarkers"/);
  });

  it("offers a handle at the middle of each side a sides-mode node allows, where the module asks for them", () => {
    const sides = spec();
    const node = { ...sides.notation.nodes.Card!, anchors: { mode: "sides" as const, sides: ["left" as const, "right" as const] } };
    const withSides: DislDocument = { ...sides, notation: { ...sides.notation, nodes: { Card: node } } };

    expect(compileNotation(withSides, { ...DRAWN_BY_MODULE, sideHandles: () => true }).elementTypes[0]!.anchors).toEqual({ kind: "compass", positions: ["e", "w"] });
    expect(compileNotation(withSides, DRAWN_BY_MODULE).elementTypes[0]!.anchors).toEqual({ kind: "edge", edgeSides: "horizontal" });
  });

  it("adds the element types of extras after the compiled ones", () => {
    const extra: ElementTypeDefinition = { id: "button", shape: "ellipse", anchors: { kind: "edge", enabled: false, visible: false }, sizing: "model" };
    const definition = compileNotation(spec(), { ...DRAWN_BY_MODULE, extras: () => ({ elementTypes: [extra] }) });

    expect(definition.elementTypes.map((type) => type.id)).toEqual(["card", "button"]);
  });
});
