import { describe, expect, it } from "vitest";
import disText from "../definition/supply-chain.dis?raw";
import type { DecorationDeclaration } from "@client/canvas/library/definition/diagramDefinition";
import { parseDisl, type DislCustomShape, type DislNodeNotation } from "@client/canvas/library/disl/disTypes";
import { evaluateGeom, resolveBox } from "@client/canvas/library/disl/geomExpr";
import { barWidthOf, CARD_HEIGHT, CARD_PARTS, CARD_WIDTH } from "./supplyChainBindings";

/**
 * The bundled specification's `supplyCard` is the library's rounded rectangle with the ornaments of
 * `CARD_PARTS`.
 *
 * The canvas never reads the custom shape's GeomExprs: `supplyChainBindings.ts` says the card is drawn as
 * the library's rounded rectangle with those ornaments, and that is all the canvas sees. This proves the two
 * say the same thing - each part's box, its corner and its style, and the bar's width at a range of shares -
 * so neither can be changed without the other.
 */

const SPEC = parseDisl(disText);
const CARD: DislCustomShape = SPEC.notation.shapes!.supplyCard!;
const STAGE: DislNodeNotation = SPEC.notation.nodes.Stage!;

/** The class each part's style is painted by in `supply-chain.css`. */
const STYLE_CLASSES: Readonly<Record<string, string>> = {
  stageHeader: "supply-chain-header",
  barTrack: "supply-chain-bar-track",
  stageBar: "supply-chain-bar",
};

/** The ornament's box, measured from the card's top-left as the specification measures a part. */
function boxOf(decoration: DecorationDeclaration, share: number): { x: number; y: number; w: number; h: number } {
  const width = typeof decoration.width === "number" ? decoration.width : barWidthOf(share);
  return { x: (decoration.from!.x as number) + CARD_WIDTH / 2, y: (decoration.from!.y as number) + CARD_HEIGHT / 2, w: width, h: decoration.height as number };
}

/** A part's corner: its rounded rectangle's radius, none for a plain one. */
function radiusOf(shape: unknown): number | undefined {
  return typeof shape === "object" && shape !== null && "params" in shape ? ((shape as { params: { radius: number } }).params.radius) : undefined;
}

describe("the supply chain card's geometry", () => {
  it("is fixed at the size the ornaments are measured in", () => {
    expect(STAGE.size).toMatchObject({ fixed: [CARD_WIDTH, CARD_HEIGHT] });
  });

  it("draws each part beside the body as one ornament, in order, of the same box, corner and style, at every share", () => {
    const parts = (CARD.parts ?? []).filter((part) => part.id !== "body");
    const styles = (STAGE as { partStyles?: Record<string, string> }).partStyles ?? {};

    expect(parts.map((part) => STYLE_CLASSES[styles[part.id]!])).toEqual(CARD_PARTS.map((decoration) => decoration.className));
    for (const share of [0, 0.01, 0.024, 0.5, 1]) {
      const scope = { w: CARD_WIDTH, h: CARD_HEIGHT, p: { share } };
      parts.forEach((part, index) => {
        const decoration = CARD_PARTS[index]!;
        expect(boxOf(decoration, share), `${part.id} at ${share}`).toEqual(resolveBox(part.box!, scope));
        expect(decoration.radius, part.id).toBe(radiusOf(part.shape));
      });
    }
  });

  it("draws the bar only while the node states a quantity, as the part's condition says", () => {
    const bar = (CARD.parts ?? []).find((part) => part.id === "bar")!;

    expect(bar.when).toBe("has(self.quantity)");
    expect(CARD_PARTS.at(-1)!.when).toEqual({ path: "payload.hasQuantity", is: "true" });
  });

  it("has a body that is the library's rounded rectangle, at its own corner", () => {
    const body = (CARD.parts ?? []).find((part) => part.id === "body")!;

    expect(resolveBox(body.box!, { w: CARD_WIDTH, h: CARD_HEIGHT, p: {} })).toEqual({ x: 0, y: 0, w: CARD_WIDTH, h: CARD_HEIGHT });
    expect(radiusOf(body.shape)).toBe(8);
    expect(evaluateGeom("max(4.0, (w - 28.0) * p.share)", { w: CARD_WIDTH, h: CARD_HEIGHT, p: { share: 0.5 } })).toBe(barWidthOf(0.5));
  });
});
