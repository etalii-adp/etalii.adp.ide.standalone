import { describe, expect, it } from "vitest";
import { centredLabelPlacement, insetLabelPlacement, midpointLabelPlacement } from "./labelPlacement";

describe("centredLabelPlacement", () => {
  it("turns a centred box into the corner-anchored rectangle an editor is drawn in", () => {
    // A canvas stores an element by its centre; an editor is positioned by its top-left.
    // Getting this backwards puts the editor a half-box down and right of its element.
    const placement = centredLabelPlacement({ x: 100, y: 50, width: 80, height: 20 }, "Alpha");

    expect(placement.x).toBe(60);
    expect(placement.y).toBe(40);
    expect(placement.width).toBe(80);
    expect(placement.height).toBe(20);
    expect(placement.text).toBe("Alpha");
  });
});

describe("insetLabelPlacement", () => {
  it("places the line at its own offset below the box's top, with its own height", () => {
    // The failure this pins is an editor covering the whole card: a name line, a type line and
    // a description, three lines of text open for editing one of them.
    const placement = insetLabelPlacement({ x: 0, y: 0, width: 200, height: 80 }, 6, 20, "Alpha");

    expect(placement.y).toBe(-34); // the box's top is -40; the line sits 6 below it
    expect(placement.height).toBe(20); // the line's height, never the box's
  });

  it("insets evenly when the caller narrows the box, because the box is centred", () => {
    // This is how a caller applies horizontal padding without the shared helper carrying
    // anyone's padding constant. A narrower centred box must inset both sides equally - if the
    // helper treated x as a left edge instead, the label would drift right by the full inset.
    const full = insetLabelPlacement({ x: 0, y: 0, width: 200, height: 80 }, 6, 20, "Alpha");
    const narrowed = insetLabelPlacement({ x: 0, y: 0, width: 200 - 4 * 2, height: 80 }, 6, 20, "Alpha");

    expect(narrowed.x).toBe(full.x + 4);
    expect(narrowed.width).toBe(full.width - 8);
  });
});

describe("midpointLabelPlacement", () => {
  it("centres the label on the line's midpoint and sits it above the offset", () => {
    // The line runs from (0, 40) to (0, 160), so the midpoint is (0, 100). A label offset by
    // -6 sits above that point rather than starting at it, which is why its own height is
    // subtracted: an editor placed at the offset alone overlaps the line it labels.
    const placement = midpointLabelPlacement({ x: 0, y: 40 }, { x: 0, y: 160 }, -6, "Uses");

    expect(placement.x + placement.width / 2).toBe(0);
    expect(placement.y).toBe(100 - 6 - 16);
    expect(placement.height).toBe(16);
  });

  it("uses the caller's measured width when it has one", () => {
    // A connection label is a bare text node with no box, so a real browser's measurement beats
    // any estimate. Ignoring it makes the editor the wrong width for every non-average string.
    const placement = midpointLabelPlacement({ x: 0, y: 0 }, { x: 200, y: 0 }, -6, "Uses", 137);

    expect(placement.width).toBe(137);
    expect(placement.x).toBe(100 - 137 / 2);
  });

  it("estimates per character when nothing was measured, and never narrower than the floor", () => {
    // jsdom implements no getBBox, so this is the branch every unit test takes - including the
    // ones above. An empty description still needs somewhere to type.
    const long = midpointLabelPlacement({ x: 0, y: 0 }, { x: 0, y: 0 }, 0, "a".repeat(40));
    const empty = midpointLabelPlacement({ x: 0, y: 0 }, { x: 0, y: 0 }, 0, "");

    expect(long.width).toBe(40 * 7);
    expect(empty.width).toBe(80);
  });
});
