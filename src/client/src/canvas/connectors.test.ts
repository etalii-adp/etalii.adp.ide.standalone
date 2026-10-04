import { describe, expect, it } from "vitest";
import {
  anchorsBetween,
  branchAnchorsBetween,
  edgePointOf,
  horizontalBezierPath,
  midpointOf,
  orthogonalPath,
  quadraticBezierPath,
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

/**
 * THE DECLARED `quadratic-bezier` ROUTE ACTUALLY CURVES.
 *
 * It did not. The control point sat at the chord's own midpoint, and a quadratic whose control
 * point lies on the chord IS the chord: the route drew a straight line under a name promising a
 * curve, beside a comment calling it "a soft, single-bend curve". No module declares it yet, so
 * nothing on screen was wrong - which is precisely what made it a trap rather than a
 * limitation: the first module to declare it would get a straight line and a comment saying
 * otherwise, with no test anywhere to disagree.
 *
 * These assert the geometric property, never the exact control coordinates, so a later choice of
 * how much the curve bows does not have to edit a guard that is about whether it bows at all.
 */
describe("quadraticBezierPath", () => {
  /** The three points of `M x0 y0 Q cx cy x1 y1`. */
  function pointsOf(path: string): { from: { x: number; y: number }; control: { x: number; y: number }; to: { x: number; y: number } } {
    const n = [...path.matchAll(/-?\d+(?:\.\d+)?/g)].map((m) => Number(m[0]));
    expect(n, `not a single quadratic segment: ${path}`).toHaveLength(6);
    return { from: { x: n[0]!, y: n[1]! }, control: { x: n[2]!, y: n[3]! }, to: { x: n[4]!, y: n[5]! } };
  }

  /** How far the control point stands off the chord, as a signed perpendicular distance. */
  function offChord(path: string): number {
    const { from, control, to } = pointsOf(path);
    const dx = to.x - from.x;
    const dy = to.y - from.y;
    const length = Math.hypot(dx, dy);
    return ((control.x - from.x) * dy - (control.y - from.y) * dx) / length;
  }

  it("starts and ends exactly where it is asked to", () => {
    // Act.
    const { from, to } = pointsOf(quadraticBezierPath({ x: 10, y: 20 }, { x: 210, y: 120 }));

    // Assert.
    expect(from).toEqual({ x: 10, y: 20 });
    expect(to).toEqual({ x: 210, y: 120 });
  });

  it("bends: its control point stands off the chord rather than on it", () => {
    // THE DEFECT. A control point on the chord makes the whole segment a straight line, and the
    // earlier version put it at the chord's midpoint - distance zero, for every input.
    expect(Math.abs(offChord(quadraticBezierPath({ x: 0, y: 0 }, { x: 200, y: 0 })))).toBeGreaterThan(1);
    expect(Math.abs(offChord(quadraticBezierPath({ x: 0, y: 0 }, { x: 0, y: 200 })))).toBeGreaterThan(1);
    expect(Math.abs(offChord(quadraticBezierPath({ x: 30, y: 40 }, { x: 170, y: 190 })))).toBeGreaterThan(1);
  });

  it("bows in proportion to its length, so a long link and a short one look like the same curve", () => {
    // A fixed offset would make a short connection a hairpin and a long one nearly straight.
    const short = Math.abs(offChord(quadraticBezierPath({ x: 0, y: 0 }, { x: 100, y: 0 })));
    const long = Math.abs(offChord(quadraticBezierPath({ x: 0, y: 0 }, { x: 400, y: 0 })));
    expect(long / short).toBeCloseTo(4, 5);
  });

  it("stays soft - noticeably gentler than a causal-loop arc", () => {
    // "Soft, single-bend" is what the route's own documentation promises. The causal loop's arc
    // bows a quarter of the chord; this must bow clearly less, or it is that arc under another
    // name and a module choosing between the two would be choosing nothing.
    const chord = 200;
    const bow = Math.abs(offChord(quadraticBezierPath({ x: 0, y: 0 }, { x: chord, y: 0 })));
    expect(bow).toBeLessThan(chord * 0.25);
  });

  it("bows B to A to the opposite side from A to B, so a pair encloses a lens rather than overdrawing", () => {
    // The property that makes a curved route worth having between two elements that link both
    // ways: the two connections separate instead of being drawn on top of each other.
    const there = offChord(quadraticBezierPath({ x: 0, y: 0 }, { x: 200, y: 0 }));
    const back = offChord(quadraticBezierPath({ x: 200, y: 0 }, { x: 0, y: 0 }));
    const { control: c1 } = pointsOf(quadraticBezierPath({ x: 0, y: 0 }, { x: 200, y: 0 }));
    const { control: c2 } = pointsOf(quadraticBezierPath({ x: 200, y: 0 }, { x: 0, y: 0 }));
    expect(Math.sign(there)).toBe(Math.sign(back)); // same side OF TRAVEL...
    expect(Math.sign(c1.y)).toBe(-Math.sign(c2.y)); // ...which is opposite sides on the page
  });
});

describe("orthogonalPath", () => {
  it("runs out, across and in sideways by default, for ends on the left and right sides", () => {
    // Act and assert.
    expect(orthogonalPath({ x: 0, y: 0 }, { x: 100, y: 60 })).toBe("M 0 0 L 50 0 L 50 60 L 100 60");
  });

  it("runs down, across and down when asked for vertical, for ends on a top and a bottom edge", () => {
    // Act and assert: a parent's bottom to a child's top, the line square to both edges.
    expect(orthogonalPath({ x: 0, y: 0 }, { x: 100, y: 60 }, 0, "vertical")).toBe("M 0 0 L 0 30 L 100 30 L 100 60");
  });

  it("rounds a vertical route's elbows turning the way the route turns", () => {
    // Act.
    const d = orthogonalPath({ x: 0, y: 0 }, { x: 100, y: 60 }, 10, "vertical");

    // Assert: heading down then right is a left turn on screen (sweep 0), then right then down a right turn (sweep 1).
    expect(d).toBe("M 0 0 L 0 20 A 10 10 0 0 0 10 30 L 90 30 A 10 10 0 0 1 100 40 L 100 60");
  });
});
