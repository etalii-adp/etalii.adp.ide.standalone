import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import disText from "../definition/mindmap.dis?raw";
import { CenteredBoxElement } from "@client/canvas/elements/centered-box/CenteredBoxElement";
import { parseDisl, type DislCustomShape } from "@client/canvas/library/disl/disTypes";
import { resolvePath, type ResolvedSegment } from "@client/canvas/library/disl/geomExpr";

/**
 * The bundled specification's `topicBox` is the box the canvas draws.
 *
 * The canvas never reads the custom shape's GeomExprs: `mindmapBindings.ts` says the shape is drawn as
 * the library's `centered-box`, an SVG `rect` with an `rx`. This renders that box and proves the
 * specification's path is the outline the browser draws for it - SVG takes an absent `ry` from `rx`
 * and clamps each radius to half its own side - at a grid of sizes, from the nominal box to ones
 * narrower and lower than its corners, so neither can be changed without the other.
 */

const TOPIC_BOX: DislCustomShape = parseDisl(disText).notation.shapes!.topicBox!;

const SIZES: readonly (readonly [number, number])[] = [
  [120, 32],
  [120, 31.6],
  [32, 31.6],
  [40, 8],
  [8, 40],
  [6, 6],
  [300, 12],
];

/** The rect `CenteredBoxElement` draws for a box of this size, as the attributes it writes. */
function drawnRect(width: number, height: number): { width: number; height: number; rx: number; ry: number | undefined } {
  const markup = renderToStaticMarkup(
    createElement("svg", null, createElement(CenteredBoxElement, { x: 0, y: 0, halfWidth: width / 2, halfHeight: height / 2, text: "" })),
  );
  const rect = /<rect\b([^>]*)>/.exec(markup)?.[1] ?? "";
  const attribute = (name: string) => {
    const value = new RegExp(`\\b${name}="([^"]*)"`).exec(rect)?.[1];
    return value === undefined ? undefined : Number(value);
  };

  return { width: attribute("width")!, height: attribute("height")!, rx: attribute("rx")!, ry: attribute("ry") };
}

/** The rounded rectangle SVG draws for a rect (SVG 2, §10.2: an auto `ry` is `rx`, each clamped to half its side), from its top-left corner. */
function outlineOfRect({ width: w, height: h, rx: givenRx, ry: givenRy }: ReturnType<typeof drawnRect>): ResolvedSegment[] {
  const rx = Math.min(givenRx, w / 2);
  const ry = Math.min(givenRy ?? givenRx, h / 2);
  const arc = (x: number, y: number): ResolvedSegment => ({ op: "A", rx, ry, rotation: 0, largeArc: false, sweep: true, x, y });
  return [
    { op: "M", x: rx, y: 0 },
    { op: "L", x: w - rx, y: 0 },
    arc(w, ry),
    { op: "L", x: w, y: h - ry },
    arc(w - rx, h),
    { op: "L", x: rx, y: h },
    arc(0, h - ry),
    { op: "L", x: 0, y: ry },
    arc(rx, 0),
    { op: "Z" },
  ];
}

describe("the mind map's topic box", () => {
  it.each(SIZES)("is the rect the canvas draws, at %s by %s", (width, height) => {
    const rect = drawnRect(width, height);

    expect([rect.width, rect.height]).toEqual([width, height]);
    expect(resolvePath(TOPIC_BOX.path!.segments, { w: width, h: height, p: {} })).toEqual(outlineOfRect(rect));
  });
});
