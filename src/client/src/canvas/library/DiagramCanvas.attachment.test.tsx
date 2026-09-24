import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { BuiltInShape, DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { isInsideOutline, outlineOf } from "./shapes/outline";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * Where a connector touches a shape that is not a rectangle.
 *
 * <b>The point is read off the rendered path rather than from the resolver</b>, because the drawn
 * line is what a reader sees and the canvas has exactly one attachment resolver behind it
 * (Requirement 3.4) - so the path is evidence about the resolver, while a direct call on the
 * resolver would not be evidence about the path.
 *
 * The test is written against `isInsideOutline` rather than against the intersection arithmetic. A
 * guard that recomputed the crossing would be a second copy of the implementation and would agree
 * with it however wrong both were; "inside the outline, and half a unit further out is outside it"
 * is a statement about the drawn shape that holds whatever the arithmetic looks like.
 */

/** The trapezoid: 160 by 48 centred on the origin, so its bounding box is x -80..80, y -24..24. */
const BOUNDS = { x: -80, y: -24, width: 160, height: 48 };

/**
 * A partner element down and to the LEFT, at a shallow angle.
 *
 * The angle is chosen so the ray leaves through the trapezoid's slanted left side rather than
 * through its bottom edge: a steeper approach exits the bottom, where the outline and the bounding
 * box agree and the whole question is invisible.
 */
const TOWARDS = { x: -400, y: 60 };

function definitionOf(shape: BuiltInShape): DiagramDefinition {
  return {
    elementTypes: [
      { id: "shaped", shape, anchors: { kind: "edge" }, sizing: "model" },
      { id: "plain", shape: "box", anchors: { kind: "edge" }, sizing: "model" },
    ],
    relationTypes: [
      {
        id: "links",
        route: "straight",
        endpoints: { source: { elementTypes: ["shaped"] }, target: { elementTypes: ["plain"] }, allowSelf: false },
      },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
}

const model: DiagramModel = {
  elements: [
    { id: "t", type: "shaped", x: 0, y: 0, width: 160, height: 48, label: "T" },
    { id: "far", type: "plain", x: TOWARDS.x, y: TOWARDS.y, width: 100, height: 40, label: "Far" },
  ],
  connections: [{ id: "t->far", type: "links", sourceId: "t", targetId: "far" }],
};

/** Where the drawn line starts, which is where it touches the source element. */
function attachmentFor(shape: BuiltInShape): { x: number; y: number } {
  const { container } = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definitionOf(shape)} model={model} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );

  const d = container.querySelector('[data-connection-id="t->far"] path.canvas-connection-line')!.getAttribute("d")!;
  const [, x, y] = /^M (-?[\d.]+) (-?[\d.]+)/.exec(d)!;

  return { x: Number(x), y: Number(y) };
}

/** The same point pushed half a unit further along the ray, away from the centre. */
function halfAUnitOut(point: { x: number; y: number }): { x: number; y: number } {
  const length = Math.hypot(TOWARDS.x, TOWARDS.y);

  return { x: point.x + (0.5 * TOWARDS.x) / length, y: point.y + (0.5 * TOWARDS.y) / length };
}

describe("a connector meets the outline, not the bounding box", () => {
  it("lands on a trapezoid's slanted side, on the outline and within half a unit of it", () => {
    // Arrange, act.
    const point = attachmentFor("trapezoid");
    const outline = outlineOf("trapezoid", BOUNDS);

    // Assert: on the shape...
    expect(isInsideOutline(point, outline), `${point.x},${point.y} is not on or inside the trapezoid`).toBe(true);
    // ...and no more than half a unit inside it, or a line would stop short of the edge it points at.
    expect(isInsideOutline(halfAUnitOut(point), outline)).toBe(false);
    // ...on the SLANTED side rather than on a bounding-box edge or a corner: strictly inside the
    // box on both axes, which only the slant allows. `x` at -80 is today's answer, and at y 12 the
    // trapezoid's own left boundary is at -62 - so the arrowhead sits beside the shape it points at.
    expect(point.x).toBeGreaterThan(BOUNDS.x);
    expect(point.y).toBeLessThan(BOUNDS.y + BOUNDS.height);
  });

  it("leaves a box's attachment point exactly where it was", () => {
    // Arrange, act: the same element, the same partner, the one shape with no outline of its own.
    const point = attachmentFor("box");

    // Assert: the bounding-box answer, unchanged - the ray meets x = -80 at y = 12. A version that
    // gave `box` an outline would move every rectangle in every diagram in the product.
    expect(point.x).toBeCloseTo(-80, 5);
    expect(point.y).toBeCloseTo(12, 5);
  });

  it("lands on the diamond and the superellipse too, not only on the shape the task named", () => {
    // The change is written against `outlineOf`, so it holds for every shape that has an outline -
    // and a guard naming one shape cannot tell a general implementation from a special case for
    // trapezoids. These two were bounding-box attached before this change as much as the
    // trapezoid was.
    for (const shape of ["diamond", "superellipse"] as const) {
      const point = attachmentFor(shape);
      const outline = outlineOf(shape, BOUNDS);
      expect(isInsideOutline(point, outline), shape).toBe(true);
      expect(isInsideOutline(halfAUnitOut(point), outline), shape).toBe(false);
      expect(point.x, shape).toBeGreaterThan(BOUNDS.x);
    }
  });
});
