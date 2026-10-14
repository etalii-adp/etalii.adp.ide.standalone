/**
 * The geometry of a bowed arc: one quadratic curve between two boxes, bowed off its chord.
 *
 * <b>What DISL calls a `curved` routing</b> (§6.10), with its `curvature` and its `selfLoop`:
 * the control point sits at the chord's midpoint, offset perpendicular to the chord by a fraction
 * of the chord's length, to the LEFT of the direction of travel for a positive curvature. That
 * one rule is what keeps two opposite lines apart - `A -> B` and `B -> A` travel in opposite
 * directions, so they bow to opposite sides and a two-element cycle draws as an ellipse rather
 * than as one line with an arrowhead at each end; a longer cycle bows consistently and reads as
 * a ring.
 *
 * Each end is clipped to its box along the direction of the control point, so the curve leaves
 * and arrives along its own tangent and an arrowhead points where the curve goes. A line from a
 * box to itself has no chord: it is drawn as a loop above the box (`selfLoop`, side `top`).
 */

/** A point in canvas units. */
export interface ArcPoint {
  x: number;
  y: number;
}

/** A box in canvas units, as the canvas already computes it. */
export interface ArcBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** How far an arc bows from its chord when nothing says otherwise, as a fraction of the chord's length. */
export const ArcBow = 0.2;

/** The radius of the loop a line from a box to itself is drawn as, when nothing says otherwise. */
export const SelfLoopRadius = 26;

/** A bowed arc's drawn geometry. */
export interface BowedArc {
  /** The SVG path: one quadratic segment, or a pair of them for a line from a box to itself. */
  readonly path: string;

  /** Where the arc leaves the source box. */
  readonly start: ArcPoint;

  /** Where the arc meets the target box, which is where the arrowhead lands. */
  readonly finish: ArcPoint;

  /** The single control point. Named for what it is, so a reader can check the claim above. */
  readonly control: ArcPoint;

  /** The point halfway along the arc. */
  readonly apex: ArcPoint;

  /** The unit normal at the apex, for drawing something across the arc rather than beside it. */
  readonly apexNormal: ArcPoint;
}

/** The centre of a box. */
export function centreOf(box: ArcBox): ArcPoint {
  return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
}

/**
 * The arc for a line from <paramref name="from" /> to <paramref name="to" />.
 *
 * @param bow how far to bow, as a fraction of the chord; positive bows to the left of travel.
 * @param selfLoopRadius the radius of the loop drawn when both ends are one box.
 */
export function arcBetween(from: ArcBox, to: ArcBox, bow: number = ArcBow, selfLoopRadius: number = SelfLoopRadius): BowedArc {
  const source = centreOf(from);
  const target = centreOf(to);

  const dx = target.x - source.x;
  const dy = target.y - source.y;
  const span = Math.hypot(dx, dy);

  // A line from a box to itself has no chord to bow from, and dividing by that zero would put NaN
  // into the path and erase the whole drawing. It is drawn as a small loop above the box.
  if (span < 1) {
    return selfArc(from, selfLoopRadius);
  }

  // The left-hand normal of the direction of travel, in SVG coordinates where y runs down.
  // Consistency is the whole trick: reversing the line reverses the direction, which flips this
  // normal, which is what puts the two halves of a two-element cycle on opposite sides.
  const normal = { x: dy / span, y: -dx / span };

  const control = {
    x: (source.x + target.x) / 2 + normal.x * span * bow,
    y: (source.y + target.y) / 2 + normal.y * span * bow,
  };

  // Clip each end to its box along the direction of the control point. The tangent of a
  // quadratic at t=0 points at its control point and at t=1 points back from it, so clipping
  // this way makes the arc leave and arrive along its own tangent - the arrowhead ends up
  // pointing where the curve is actually going.
  const start = boundaryToward(from, control);
  const finish = boundaryToward(to, control);

  return {
    path: `M ${round(start.x)} ${round(start.y)} Q ${round(control.x)} ${round(control.y)} ${round(finish.x)} ${round(finish.y)}`,
    start,
    finish,
    control,
    apex: quadraticAt(start, control, finish, 0.5),
    apexNormal: quadraticNormalAt(start, control, finish, 0.5),
  };
}

/**
 * A point on the arc, as a fraction along it - for placing a mark near the arrowhead without
 * putting it on top of the arrowhead.
 */
export function pointAlong(arc: BowedArc, t: number): ArcPoint {
  return quadraticAt(arc.start, arc.control, arc.finish, t);
}

/** The unit normal to the arc at a fraction along it. */
export function normalAlong(arc: BowedArc, t: number): ArcPoint {
  return quadraticNormalAt(arc.start, arc.control, arc.finish, t);
}

/**
 * Where a ray from the box's centre toward <paramref name="target" /> leaves the box.
 *
 * A rectangle rather than the circle it would be tempting to use: a box holding a label is much
 * wider than it is tall, and a circular clip would leave a visible gap at the sides and overlap
 * the text at the top and bottom.
 */
function boundaryToward(box: ArcBox, target: ArcPoint): ArcPoint {
  const centre = centreOf(box);
  const dx = target.x - centre.x;
  const dy = target.y - centre.y;

  if (Math.abs(dx) < 1e-9 && Math.abs(dy) < 1e-9) {
    return centre;
  }

  const halfWidth = box.width / 2;
  const halfHeight = box.height / 2;

  // How far along the ray the first side is reached. Whichever side is nearer wins, which is
  // what makes this a rectangle clip rather than an ellipse one.
  const scaleX = Math.abs(dx) < 1e-9 ? Number.POSITIVE_INFINITY : halfWidth / Math.abs(dx);
  const scaleY = Math.abs(dy) < 1e-9 ? Number.POSITIVE_INFINITY : halfHeight / Math.abs(dy);
  const scale = Math.min(scaleX, scaleY);

  return { x: centre.x + dx * scale, y: centre.y + dy * scale };
}

/**
 * A line from a box to itself, drawn as a small loop above it.
 *
 * Two quadratic segments rather than one, because a single control point cannot return a curve
 * to where it started without collapsing to a line.
 */
function selfArc(box: ArcBox, radius: number): BowedArc {
  const centre = centreOf(box);
  const top = box.y;

  const start = { x: centre.x - box.width * 0.15, y: top };
  const finish = { x: centre.x + box.width * 0.15, y: top };
  const control = { x: centre.x, y: top - radius * 2 };

  const left = { x: start.x - radius, y: top - radius * 1.6 };
  const right = { x: finish.x + radius, y: top - radius * 1.6 };

  return {
    path:
      `M ${round(start.x)} ${round(start.y)} Q ${round(left.x)} ${round(left.y)} ${round(control.x)} ${round(control.y)}`
      + ` Q ${round(right.x)} ${round(right.y)} ${round(finish.x)} ${round(finish.y)}`,
    start,
    finish,
    control,
    apex: control,
    apexNormal: { x: 1, y: 0 },
  };
}

/** The point at fraction <paramref name="t" /> along a quadratic bezier. */
function quadraticAt(start: ArcPoint, control: ArcPoint, finish: ArcPoint, t: number): ArcPoint {
  const inverse = 1 - t;
  return {
    x: inverse * inverse * start.x + 2 * inverse * t * control.x + t * t * finish.x,
    y: inverse * inverse * start.y + 2 * inverse * t * control.y + t * t * finish.y,
  };
}

/** The unit normal at fraction <paramref name="t" />, from the curve's own derivative. */
function quadraticNormalAt(start: ArcPoint, control: ArcPoint, finish: ArcPoint, t: number): ArcPoint {
  const inverse = 1 - t;
  const dx = 2 * inverse * (control.x - start.x) + 2 * t * (finish.x - control.x);
  const dy = 2 * inverse * (control.y - start.y) + 2 * t * (finish.y - control.y);
  const length = Math.hypot(dx, dy);

  return length < 1e-9 ? { x: 0, y: -1 } : { x: dy / length, y: -dx / length };
}

/** Coordinates in the path, rounded so the DOM does not carry sixteen digits of noise. */
function round(value: number): number {
  return Math.round(value * 100) / 100;
}
