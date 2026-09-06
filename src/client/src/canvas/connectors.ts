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

/**
 * A polyline through the given waypoints, as an SVG path - the route a user has bent by hand
 * (diagram-library Requirement 3.1). No waypoints means a straight line.
 */
export function polylinePath(from: Point, to: Point, waypoints: readonly Point[] = []): string {
  const stops = [...waypoints, to].map((point) => `L ${point.x} ${point.y}`).join(" ");
  return `M ${from.x} ${from.y} ${stops}`;
}

/**
 * An axis-aligned route: out horizontally to the midpoint, vertically across, then in
 * horizontally - the orthogonal family flow diagrams draw. A corner radius rounds the two
 * elbows with quarter-turn arcs; zero (the default) keeps them sharp.
 */
export function orthogonalPath(from: Point, to: Point, cornerRadius = 0): string {
  const midX = (from.x + to.x) / 2;
  if (cornerRadius <= 0 || from.y === to.y) {
    return `M ${from.x} ${from.y} L ${midX} ${from.y} L ${midX} ${to.y} L ${to.x} ${to.y}`;
  }

  // The radius never exceeds what the segments can host, or the arcs would overlap.
  const r = Math.min(cornerRadius, Math.abs(midX - from.x), Math.abs(to.y - from.y) / 2, Math.abs(to.x - midX));
  const xDir = Math.sign(midX - from.x) || 1;
  const yDir = Math.sign(to.y - from.y) || 1;
  const sweep1 = xDir * yDir > 0 ? 1 : 0;
  const sweep2 = xDir * yDir > 0 ? 0 : 1;
  return [
    `M ${from.x} ${from.y}`,
    `L ${midX - xDir * r} ${from.y}`,
    `A ${r} ${r} 0 0 ${sweep1} ${midX} ${from.y + yDir * r}`,
    `L ${midX} ${to.y - yDir * r}`,
    `A ${r} ${r} 0 0 ${sweep2} ${midX + xDir * r} ${to.y}`,
    `L ${to.x} ${to.y}`,
  ].join(" ");
}

/**
 * A single-control-point curve bowed perpendicular to the chord, always to the same side of
 * travel - the arc a causal-loop link draws, generalized: A -> B and B -> A bow to opposite
 * sides and enclose a lens instead of overdrawing each other.
 */
export function arcPath(from: Point, to: Point, bow = 0.25): string {
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  const control = {
    x: (from.x + to.x) / 2 - dy * bow,
    y: (from.y + to.y) / 2 + dx * bow,
  };
  return `M ${from.x} ${from.y} Q ${control.x} ${control.y} ${to.x} ${to.y}`;
}

/** A quadratic bezier with its control at the straight midpoint - a soft, single-bend curve. */
export function quadraticBezierPath(from: Point, to: Point): string {
  const mid = { x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 };
  return `M ${from.x} ${from.y} Q ${mid.x} ${mid.y} ${to.x} ${to.y}`;
}

/**
 * A smooth spline through the given waypoints: Catmull-Rom converted to the cubic segments
 * SVG can draw, so the curve passes THROUGH every waypoint rather than being pulled toward
 * it. No waypoints means a straight line, exactly as the polyline degenerates.
 */
export function splinePath(from: Point, to: Point, waypoints: readonly Point[] = []): string {
  const points = [from, ...waypoints, to];
  if (points.length === 2) {
    return straightPath(from, to);
  }

  const segments: string[] = [`M ${from.x} ${from.y}`];
  for (let i = 0; i < points.length - 1; i++) {
    const p0 = points[Math.max(i - 1, 0)];
    const p1 = points[i];
    const p2 = points[i + 1];
    const p3 = points[Math.min(i + 2, points.length - 1)];
    const c1 = { x: p1.x + (p2.x - p0.x) / 6, y: p1.y + (p2.y - p0.y) / 6 };
    const c2 = { x: p2.x - (p3.x - p1.x) / 6, y: p2.y - (p3.y - p1.y) / 6 };
    segments.push(`C ${c1.x} ${c1.y}, ${c2.x} ${c2.y}, ${p2.x} ${p2.y}`);
  }

  return segments.join(" ");
}

/** The midpoint of two points - where a connector's label usually belongs. */
export function midpointOf(from: Point, to: Point): Point {
  return { x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 };
}
