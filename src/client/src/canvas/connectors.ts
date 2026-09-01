/**
 * Where a connector between two boxes starts, where it ends, and what shape it takes on the way.
 *
 * Every diagram type draws lines between boxes, and every one of them faces the same two
 * questions first: which point on the box does the line touch, and what curve joins the two
 * points. Those answers are geometry rather than notation, so they live here rather than being
 * written once per canvas — a diagram module decides *which* of these it wants and what to
 * style the result with, and never has to work out edge intersections again.
 *
 * Coordinates are the ones the canvases already use: `x` and `y` are the **centre** of a box,
 * which is what both the mindmap and the C4 layouts produce. Everything returns plain numbers
 * or an SVG path string, so nothing here knows about React, CSS or a particular notation.
 */

/** A box on a canvas, positioned by its centre. */
export interface ConnectorBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface Point {
  x: number;
  y: number;
}

/** Which vertical side of a box a connector leaves from. */
export type HorizontalSide = "left" | "right";

/**
 * The point on a box's edge in the direction `(dx, dy)` from its centre.
 *
 * Scales the direction until it meets whichever edge it reaches first, so the result sits on the
 * rectangle rather than on the ellipse a naive radius calculation would give. A zero direction
 * has no edge to find and returns the centre.
 */
export function edgePointOf(box: ConnectorBox, dx: number, dy: number): Point {
  if (dx === 0 && dy === 0) {
    return { x: box.x, y: box.y };
  }

  const halfWidth = box.width / 2;
  const halfHeight = box.height / 2;
  const scale = Math.min(
    dx === 0 ? Number.POSITIVE_INFINITY : halfWidth / Math.abs(dx),
    dy === 0 ? Number.POSITIVE_INFINITY : halfHeight / Math.abs(dy),
  );
  return { x: box.x + dx * scale, y: box.y + dy * scale };
}

/**
 * Where a straight connector between two boxes should start and end: on each box's edge, along
 * the line between their centres.
 *
 * This is what stops an arrowhead disappearing under the box it points at. Use it for a graph,
 * where any box may connect to any other and there is no meaningful "side".
 */
export function anchorsBetween(source: ConnectorBox, destination: ConnectorBox): [Point, Point] {
  const dx = destination.x - source.x;
  const dy = destination.y - source.y;
  return [edgePointOf(source, dx, dy), edgePointOf(destination, -dx, -dy)];
}

/** The middle of a box's left or right side. */
export function sideAnchorOf(box: ConnectorBox, side: HorizontalSide): Point {
  return { x: side === "right" ? box.x + box.width / 2 : box.x - box.width / 2, y: box.y };
}

/**
 * Where a connector between a parent and one of its children should start and end, for a layout
 * that puts children in a column beside their parent.
 *
 * Both ends anchor on a vertical side at mid-height — the parent's side facing the child, and
 * the child's side facing back — rather than on the line between the two centres. That is the
 * difference between a tree and a graph: in a tree the direction is known before the geometry
 * is, so a connector can leave sideways and stay in the corridor between the two columns
 * instead of cutting across whatever sits between them.
 */
export function branchAnchorsBetween(parent: ConnectorBox, child: ConnectorBox): [Point, Point] {
  const childOnRight = child.x >= parent.x;
  return [
    sideAnchorOf(parent, childOnRight ? "right" : "left"),
    sideAnchorOf(child, childOnRight ? "left" : "right"),
  ];
}

/**
 * Where a connector between two boxes at the same conceptual level should start and end: on the
 * vertical sides that face each other, at mid-height.
 *
 * Unlike {@link branchAnchorsBetween} there is no parent and child here - just two boxes side by
 * side, each lending the side nearest the other. The timeline's relations read this way, and any
 * other row-oriented notation would too.
 */
export function facingAnchorsBetween(from: ConnectorBox, to: ConnectorBox): [Point, Point] {
  const toIsRight = to.x >= from.x;
  return [sideAnchorOf(from, toIsRight ? "right" : "left"), sideAnchorOf(to, toIsRight ? "left" : "right")];
}

/**
 * A horizontal cubic bezier from one point to another, as an SVG path.
 *
 * Both control points sit at the horizontal midpoint, which is what makes the curve leave and
 * arrive horizontally: it bulges sideways rather than diagonally, so the whole curve stays
 * within the horizontal span between its two ends. For a tree drawn in columns that means every
 * connector stays inside the gap between two columns and clear of the siblings above and below.
 */
export function horizontalBezierPath(from: Point, to: Point): string {
  const midX = (from.x + to.x) / 2;
  return `M ${from.x} ${from.y} C ${midX} ${from.y}, ${midX} ${to.y}, ${to.x} ${to.y}`;
}

/**
 * A cubic bezier that always departs `from` rightward and arrives at `to` from its left.
 *
 * With `to` well to the right of `from` this is exactly the horizontal bezier: both control
 * points meet at the horizontal midpoint. As `to` moves back over or behind `from`, the control
 * points push outward instead - one past `from` to the right, one past `to` to the left, further
 * the further back `to` sits - so the curve still leaves forward, loops around, and arrives
 * backward, rather than reversing straight out of a box's side.
 */
export function forwardBezierPath(from: Point, to: Point): string {
  const span = to.x - from.x;
  const reach = Math.max(span / 2, 40, -span * 0.6);
  return `M ${from.x} ${from.y} C ${from.x + reach} ${from.y}, ${to.x - reach} ${to.y}, ${to.x} ${to.y}`;
}

/** A straight line from one point to another, as an SVG path. */
export function straightPath(from: Point, to: Point): string {
  return `M ${from.x} ${from.y} L ${to.x} ${to.y}`;
}

/** The midpoint of two points - where a connector's label usually belongs. */
export function midpointOf(from: Point, to: Point): Point {
  return { x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 };
}
