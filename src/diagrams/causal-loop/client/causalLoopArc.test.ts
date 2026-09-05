import { describe, expect, it } from "vitest";

import { ArcBow, arcBetween, centreOf, normalAlong, pointAlong, type ArcBox } from "./causalLoopArc";

/**
 * The geometry of a causal link.
 *
 * The bug these were written for: a two-variable feedback loop drew no loop at all. Both links
 * took the shared tree connector, which anchors on the sides facing each other, so `A -> B` and
 * `B -> A` rendered as one line with an arrowhead at each end.
 */
describe("causal loop arcs", () => {
  const boxAt = (x: number, y: number): ArcBox => ({ x: x - 60, y: y - 20, width: 120, height: 40 });

  const left = boxAt(0, 0);
  const right = boxAt(400, 0);

  /** How many control points an SVG path segment declares: `Q` is one, `C` is two. */
  const segments = (path: string) => path.match(/[QC]/g) ?? [];

  describe("the shape is an arc, not an S", () => {
    it("draws one quadratic segment, which is one control point", () => {
      // Act.
      const arc = arcBetween(left, right);

      // Assert.
      // The hint that started this: one control point, not two. A cubic bezier bulges out and
      // back, which is what made a ring of links read as a concertina.
      expect(segments(arc.path)).toEqual(["Q"]);
      expect(arc.path).not.toContain("C");
    });

    it("bows the control point perpendicular to the chord, by the stated fraction", () => {
      // Act.
      const arc = arcBetween(left, right);

      // Assert.
      const chord = Math.hypot(
        centreOf(right).x - centreOf(left).x,
        centreOf(right).y - centreOf(left).y,
      );

      // Perpendicular: the chord here is horizontal, so all of the offset is vertical.
      expect(arc.control.x).toBeCloseTo(200, 6);
      expect(Math.abs(arc.control.y)).toBeCloseTo(chord * ArcBow, 6);
    });

    it("bows further as the chord grows, so a long link is not a near-straight line", () => {
      // Act.
      const near = arcBetween(left, boxAt(200, 0));
      const far = arcBetween(left, boxAt(800, 0));

      // Assert.
      expect(Math.abs(far.control.y)).toBeGreaterThan(Math.abs(near.control.y) * 2);
    });
  });

  describe("a two-variable loop draws a loop", () => {
    /**
     * The defect, stated as a test. Reversing a link reverses the direction of travel, which
     * flips the normal, which puts the two arcs on opposite sides of the chord.
     */
    it("puts the two directions on opposite sides of the chord", () => {
      // Act.
      const there = arcBetween(left, right);
      const back = arcBetween(right, left);

      // Assert.
      expect(Math.sign(there.control.y)).not.toBe(Math.sign(back.control.y));
      expect(there.control.y).toBeCloseTo(-back.control.y, 6);
    });

    it("separates the two arcs at their apexes rather than overlaying them", () => {
      // Act.
      const there = arcBetween(left, right);
      const back = arcBetween(right, left);

      // Assert.
      // Before the fix these two points were the same, which is precisely why no loop appeared.
      const apart = Math.hypot(there.apex.x - back.apex.x, there.apex.y - back.apex.y);
      expect(apart).toBeGreaterThan(60);
    });

    it("encloses an area, which is what makes it read as a loop", () => {
      // Arrange.
      const there = arcBetween(left, right);
      const back = arcBetween(right, left);

      // Act.
      // The enclosed height at the midpoint: how far apart the two arcs are where they are
      // widest. A lens this thin would not read as a loop; a lens this thick does.
      const gap = Math.abs(there.apex.y - back.apex.y);

      // Assert.
      expect(gap).toBeGreaterThan(50);
    });

    it("holds whichever way round the two variables sit", () => {
      // Act.
      // The old connector keyed its anchors off which box was further right, so mirroring the
      // pair changed the drawing. This one keys off the direction of travel, which does not care.
      const rightward = arcBetween(left, right);
      const leftward = arcBetween(right, left);
      const mirroredRightward = arcBetween(boxAt(400, 0), boxAt(0, 0));

      // Assert.
      expect(Math.sign(leftward.control.y)).toBe(Math.sign(mirroredRightward.control.y));
      expect(Math.sign(rightward.control.y)).not.toBe(Math.sign(mirroredRightward.control.y));
    });
  });

  describe("a cycle bows consistently, so it reads as a ring", () => {
    it("bows every link of a ring to the same side of travel", () => {
      // Arrange.
      // Four variables on a circle, linked in order - the commonest larger shape.
      const ring = [0, 1, 2, 3].map((index) => {
        const angle = (index * Math.PI) / 2;
        return boxAt(Math.round(300 * Math.cos(angle)), Math.round(300 * Math.sin(angle)));
      });

      // Act.
      const arcs = ring.map((box, index) => arcBetween(box, ring[(index + 1) % ring.length]!));

      // Assert.
      // Each control point sits further from the ring's centre than the chord's midpoint does:
      // the arcs bow outward together, which is the ring. One bowing inward would pinch it.
      for (const [index, arc] of arcs.entries()) {
        const from = centreOf(ring[index]!);
        const to = centreOf(ring[(index + 1) % ring.length]!);
        const midpoint = { x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 };

        expect(Math.hypot(arc.control.x, arc.control.y)).toBeGreaterThan(
          Math.hypot(midpoint.x, midpoint.y),
        );
      }
    });
  });

  describe("the ends meet the boxes", () => {
    /** On the boundary of the box, on whichever side the arc actually departs through. */
    const onBoundary = (box: ArcBox, point: { x: number; y: number }) => {
      const onVertical = Math.abs(Math.abs(point.x - centreOf(box).x) - box.width / 2) < 1e-6;
      const onHorizontal = Math.abs(Math.abs(point.y - centreOf(box).y) - box.height / 2) < 1e-6;
      return onVertical || onHorizontal;
    };

    it("starts and finishes on a box edge rather than at its centre", () => {
      // Act.
      const arc = arcBetween(left, right);

      // Assert.
      // Which edge is not fixed, and should not be: the arc leaves along its own tangent, which
      // points at the control point, so a bowed link departs through the top rather than the
      // side. Asserting "the right-hand side" would be asserting the old tree connector's
      // behaviour, which is the thing being replaced.
      expect(onBoundary(left, arc.start)).toBe(true);
      expect(onBoundary(right, arc.finish)).toBe(true);

      // On the edge, not inside it: an arrowhead drawn at the centre would sit under the label.
      expect(arc.finish.x).toBeLessThan(centreOf(right).x);
      expect(arc.start.x).toBeGreaterThan(centreOf(left).x);
    });

    it("clips to the rectangle rather than to a circle around it", () => {
      // Act.
      // Directly above: a circular clip would leave the arc short of a box that is 120 wide and
      // 40 tall, with a visible gap.
      const arc = arcBetween(left, boxAt(0, -400));

      // Assert.
      expect(arc.start.y).toBeCloseTo(left.y, 6);
      expect(Math.abs(arc.start.x)).toBeLessThanOrEqual(left.width / 2 + 1e-6);
    });
  });

  describe("what is drawn along the arc follows the arc", () => {
    it("puts the apex on the curve, not on the chord", () => {
      // Act.
      const arc = arcBetween(left, right);

      // Assert.
      // A delay mark placed at the chord's midpoint would float beside the line it is meant to
      // cross. The apex is off that chord by a visible margin.
      const chordMidpoint = {
        x: (arc.start.x + arc.finish.x) / 2,
        y: (arc.start.y + arc.finish.y) / 2,
      };
      expect(Math.abs(arc.apex.y - chordMidpoint.y)).toBeGreaterThan(10);

      // And it is where the curve's own arithmetic puts it: halfway between that chord midpoint
      // and the control point, which is the defining property of a quadratic at t = 0.5.
      expect(arc.apex.x).toBeCloseTo((chordMidpoint.x + arc.control.x) / 2, 6);
      expect(arc.apex.y).toBeCloseTo((chordMidpoint.y + arc.control.y) / 2, 6);
    });

    it("gives a unit normal, so a stroke across the arc is the same length wherever it is", () => {
      // Act.
      const arc = arcBetween(left, right);

      // Assert.
      for (const t of [0.1, 0.5, 0.9]) {
        const normal = normalAlong(arc, t);
        expect(Math.hypot(normal.x, normal.y)).toBeCloseTo(1, 6);
      }
    });

    it("walks the arc from its start to its finish", () => {
      // Act.
      const arc = arcBetween(left, right);

      // Assert.
      expect(pointAlong(arc, 0)).toEqual(arc.start);
      expect(pointAlong(arc, 1)).toEqual(arc.finish);
      expect(pointAlong(arc, 0.5)).toEqual(arc.apex);
    });
  });

  describe("degenerate shapes do not produce NaN", () => {
    /**
     * A link from a variable to itself has no chord, and dividing by that zero would put NaN into
     * the path - which does not draw a broken link, it erases the whole drawing.
     */
    it("draws a link from a variable to itself as a loop above it", () => {
      // Act.
      const arc = arcBetween(left, left);

      // Assert.
      expect(arc.path).not.toContain("NaN");
      expect(segments(arc.path)).toEqual(["Q", "Q"]);

      // Above the box, so it does not sit on the label.
      expect(arc.apex.y).toBeLessThan(left.y);
    });

    it("survives two variables placed on top of each other", () => {
      // Act.
      const arc = arcBetween(left, boxAt(0, 0));

      // Assert.
      expect(arc.path).not.toContain("NaN");
      expect(arc.path).not.toContain("Infinity");
    });

    it("never emits NaN for any placement", () => {
      // Arrange.
      const placements: [number, number][] = [
        [0, 0], [400, 0], [-400, 0], [0, 400], [0, -400], [300, 300], [-300, -300], [1, 1],
      ];

      // Act & assert.
      for (const [x, y] of placements) {
        const arc = arcBetween(left, boxAt(x, y));
        expect(arc.path, `placement ${x},${y}`).not.toContain("NaN");
        expect(Number.isFinite(arc.apex.x)).toBe(true);
        expect(Number.isFinite(arc.apex.y)).toBe(true);
      }
    });
  });
});
