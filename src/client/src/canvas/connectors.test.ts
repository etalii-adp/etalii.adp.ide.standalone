import { describe, expect, it } from "vitest";
import {
  anchorsBetween,
  branchAnchorsBetween,
  edgePointOf,
  horizontalBezierPath,
  midpointOf,
  sideAnchorOf,
  straightPath,
  type ConnectorBox,
} from "./connectors";

/** A box by centre and size, which is what both layouts produce. */
function box(x: number, y: number, width = 100, height = 40): ConnectorBox {
  return { x, y, width, height };
}

describe("edgePointOf", () => {
  it("lands on the side a horizontal direction reaches", () => {
    // Act and assert.
    expect(edgePointOf(box(0, 0), 1, 0)).toEqual({ x: 50, y: 0 });
    expect(edgePointOf(box(0, 0), -1, 0)).toEqual({ x: -50, y: 0 });
  });

  it("lands on the top or bottom a vertical direction reaches", () => {
    // Act and assert.
    expect(edgePointOf(box(0, 0), 0, 1)).toEqual({ x: 0, y: 20 });
    expect(edgePointOf(box(0, 0), 0, -1)).toEqual({ x: 0, y: -20 });
  });

  it("takes whichever edge the direction meets first, not the corner", () => {
    // Act.
    // A wide, short box: a 45 degree direction leaves through the top long before it would
    // reach the side. Scaling by a radius instead of an edge is the classic way to get this
    // wrong, and it puts the arrowhead inside the box.
    const point = edgePointOf(box(0, 0, 200, 40), 1, 1);

    // Assert.
    expect(point).toEqual({ x: 20, y: 20 });
  });

  it("stays on the rectangle for a direction that is not axis-aligned", () => {
    // Act.
    const point = edgePointOf(box(0, 0, 100, 100), 2, 1);

    // Assert.
    // Reaches the right side at x = 50, and half as far vertically.
    expect(point).toEqual({ x: 50, y: 25 });
  });

  it("returns the centre for no direction at all, rather than dividing by zero", () => {
    // Act and assert.
    // Two elements laid out on exactly the same spot is a layout bug, not a reason to render
    // NaN into an SVG path and blank the canvas.
    expect(edgePointOf(box(7, 9), 0, 0)).toEqual({ x: 7, y: 9 });
  });
});

describe("anchorsBetween", () => {
  it("puts both ends on the boxes' facing edges", () => {
    // Act.
    // The property that matters: an arrow must touch the box rather than disappear under it.
    const [from, to] = anchorsBetween(box(0, 0, 100, 80), box(0, 200, 100, 80));

    // Assert.
    expect(from).toEqual({ x: 0, y: 40 });
    expect(to).toEqual({ x: 0, y: 160 });
  });

  it("is symmetric: reversing the ends reverses the anchors", () => {
    // Act.
    const forward = anchorsBetween(box(0, 0), box(300, 120));
    const backward = anchorsBetween(box(300, 120), box(0, 0));

    // Assert.
    expect(backward[0]).toEqual(forward[1]);
    expect(backward[1]).toEqual(forward[0]);
  });
});

describe("branchAnchorsBetween", () => {
  it("leaves the parent's right and arrives at the child's left when the child is to the right", () => {
    // Act.
    const [from, to] = branchAnchorsBetween(box(0, 0, 100, 40), box(300, 80, 100, 40));

    // Assert.
    // Both at mid-height, which is what keeps a tree's connectors in the corridor between
    // columns rather than cutting diagonally across whatever sits between them.
    expect(from).toEqual({ x: 50, y: 0 });
    expect(to).toEqual({ x: 250, y: 80 });
  });

  it("mirrors when the child is to the left", () => {
    // Act.
    const [from, to] = branchAnchorsBetween(box(0, 0, 100, 40), box(-300, 80, 100, 40));

    // Assert.
    expect(from).toEqual({ x: -50, y: 0 });
    expect(to).toEqual({ x: -250, y: 80 });
  });

  it("treats a child directly below as being on the right, rather than picking arbitrarily", () => {
    // Act.
    // Equal x is the tie. It has to resolve the same way every time or a node would flip sides
    // as it was dragged through its parent's column.
    const [from] = branchAnchorsBetween(box(0, 0, 100, 40), box(0, 200, 100, 40));

    // Assert.
    expect(from.x).toBe(50);
  });
});

describe("sideAnchorOf", () => {
  it("takes the middle of the named side", () => {
    // Act and assert.
    expect(sideAnchorOf(box(10, 20, 100, 40), "right")).toEqual({ x: 60, y: 20 });
    expect(sideAnchorOf(box(10, 20, 100, 40), "left")).toEqual({ x: -40, y: 20 });
  });
});

describe("path builders", () => {
  it("builds a straight path between two points", () => {
    // Act and assert.
    expect(straightPath({ x: 1, y: 2 }, { x: 3, y: 4 })).toBe("M 1 2 L 3 4");
  });

  it("builds a bezier whose control points share the horizontal midpoint", () => {
    // Act.
    const path = horizontalBezierPath({ x: 0, y: 0 }, { x: 100, y: 60 });

    // Assert.
    // Both controls at x = 50 is what makes the curve leave and arrive horizontally.
    expect(path).toBe("M 0 0 C 50 0, 50 60, 100 60");
  });

  it("keeps a bezier within the horizontal span of its ends", () => {
    // Act.
    // The reason a tree can use it: the curve never wanders outside the gap between the two
    // columns, so it cannot stray over a sibling.
    const path = horizontalBezierPath({ x: 200, y: 0 }, { x: 100, y: 60 });

    // Assert.
    expect(path).toBe("M 200 0 C 150 0, 150 60, 100 60");
  });
});

describe("midpointOf", () => {
  it("is halfway along, which is where a connector's label goes", () => {
    // Act and assert.
    expect(midpointOf({ x: 0, y: 0 }, { x: 10, y: 30 })).toEqual({ x: 5, y: 15 });
  });
});
