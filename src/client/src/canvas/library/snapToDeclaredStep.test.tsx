import { describe, expect, it } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvas, snapToStep } from "./DiagramCanvas";
import type { DiagramDefinition, SnapDeclaration } from "./definition/diagramDefinition";
import type { DiagramModel, DiagramModelElement } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "@client/canvas/library/testing/canvasHarness";

/**
 * A dragged element comes to rest on the lattice its diagram type declares.
 *
 * ## What this is really guarding
 *
 * Not a new capability — a rule the tree already agreed on in five places, now owned once. The
 * timeline's and the dependency graph's `nearestRow` were byte-identical bodies; their two
 * backends carry the same arithmetic; the binding vocabulary's `round: "nearest"` is a third
 * statement of it. The declaration replaces the client half.
 *
 * ## The half that would rot silently
 *
 * **Halves round away from zero, and `Math.round` does not.** `Math.round(-0.5)` is `-0`, so a
 * drag one half-step ABOVE the origin lands a row low while the identical drag below it lands
 * correctly — a rounding rule that is right in the common half of the canvas and wrong in the
 * other. Every test below that could be satisfied by `Math.round` is paired with one above the
 * origin that cannot be, because that is the only place the mistake is visible.
 *
 * ## Which part of the element rests on the line
 *
 * **Its leading edge - the top for y, the left for x - never its centre.** The snap first rounded
 * the centre, and both modules that snap draw a row's element with its TOP on the row, so a
 * dragged element rested half its height above its row and dropped that half again when the
 * backend confirmed: the snap seen during the drag was not the snap on the drop (the user's
 * report). The elements here are placed with their top edge on 0, so every expectation reads as
 * an edge; the edge-versus-centre test says it outright with a height that would tell them apart.
 */

const STEP = 60;
const HEIGHT = 40;

function definitionWith(snap: SnapDeclaration): DiagramDefinition {
  return {
    elementTypes: [{ id: "node", shape: "box", sizing: "model", anchors: { kind: "edge" } }],
    relationTypes: [],
    snap,
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
}

// Top edge on 0 and left edge on 0: the centre sits half the box further in on each axis.
const resting: DiagramModelElement = { id: "a", type: "node", x: 50, y: HEIGHT / 2, width: 100, height: HEIGHT };

function mount(definition: DiagramDefinition, elements: DiagramModelElement[], onMoved?: (id: string, x: number, y: number) => void) {
  const model: DiagramModel = { elements, connections: [] };
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvas
          definition={definition}
          model={model}
          events={{ onElementMoved: ({ elementId, position }) => onMoved?.(elementId, position.x, position.y) }}
        />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

/** Drags `id` by (dx, dy) and returns the CENTRE the canvas reported on release. */
function dragBy(definition: DiagramDefinition, elements: DiagramModelElement[], id: string, dx: number, dy: number): { x: number; y: number } | null {
  let landed: { x: number; y: number } | null = null;
  const { container, unmount } = mount(definition, elements, (moved, x, y) => {
    if (moved === id) {
      landed = { x, y };
    }
  });

  const target = container.querySelector(`[data-element-id="${id}"]`)!;
  fireEvent(target, pointer("pointerdown", { clientX: 0, clientY: 0 }));
  fireEvent(target, pointer("pointermove", { clientX: dx, clientY: dy }));
  fireEvent(target, pointer("pointerup", { clientX: dx, clientY: dy }));
  unmount();
  return landed;
}

/** Where the top edge came to rest after a vertical drag of `resting` by `dy`, on the row lattice. */
function topAfter(dy: number): number | null {
  const landed = dragBy(definitionWith({ y: { step: STEP } }), [resting], "a", 0, dy);
  return landed === null ? null : landed.y - HEIGHT / 2;
}

describe("snapToStep - the rule itself", () => {
  it("rounds to the nearest multiple", () => {
    expect(snapToStep(0, STEP)).toBe(0);
    expect(snapToStep(20, STEP)).toBe(0);
    expect(snapToStep(40, STEP)).toBe(60);
    expect(snapToStep(61, STEP)).toBe(60);
  });

  it("rounds a half AWAY from zero on BOTH sides of the origin", () => {
    // THE ONE THAT CATCHES `Math.round`. The positive half passes either way; the negative
    // half is the whole point - `Math.round(-0.5)` is -0, which would answer 0 here.
    expect(snapToStep(STEP / 2, STEP)).toBe(STEP);
    expect(snapToStep(-STEP / 2, STEP)).toBe(-STEP);
    expect(snapToStep(-STEP * 1.5, STEP)).toBe(-STEP * 2);
  });

  it("leaves the value alone when no step is declared", () => {
    // Every diagram that declares no snap must be untouched by this, which is most of them.
    expect(snapToStep(37, undefined)).toBe(37);
    expect(snapToStep(37, 0)).toBe(37);
    expect(snapToStep(37, -10)).toBe(37);
  });
});

describe("a drag lands on the declared step", () => {
  it("reports a snapped position on release", () => {
    expect(topAfter(70)).toBe(60);
    expect(topAfter(20)).toBe(0);
  });

  it("snaps a drag ABOVE the origin the same way", () => {
    // The same asymmetry, end to end rather than in the helper: a canvas whose origin is not
    // at its top edge - every timeline with a row above the first - would otherwise land one
    // row out in only that direction, which reads as an intermittent bug.
    expect(topAfter(-70)).toBe(-60);
    expect(topAfter(-30)).toBe(-60);
    expect(topAfter(-20)).toBe(0);
  });

  it("puts the element's TOP EDGE on the line, not its centre", () => {
    // THE USER'S REPORT, in the library's own terms. A box 36 high with its top on row 0, dragged
    // down 67: its top rests on 60, so its centre is at 78. Snapping the centre instead answers
    // 60 - half a box above the row the drop then places it on.
    const box: DiagramModelElement = { id: "a", type: "node", x: 50, y: 18, width: 100, height: 36 };

    const landed = dragBy(definitionWith({ y: { step: STEP } }), [box], "a", 0, 67);

    expect(landed?.y).toBe(78);
  });

  it("shows the snapped position DURING the drag, not only on release", () => {
    // The user chose snap-during-drag over snap-on-drop, for the same reason the connectors
    // follow the element: the picture during the gesture should be the picture after it. A
    // preview that ignored the step would show a position the drop then quietly changes.
    const { container, unmount } = mount(definitionWith({ y: { step: STEP } }), [resting]);

    const target = container.querySelector('[data-element-id="a"]')!;
    fireEvent(target, pointer("pointerdown", { clientX: 0, clientY: 0 }));
    fireEvent(target, pointer("pointermove", { clientX: 0, clientY: 70 }));

    // The top was on 0 and moved 70, which is nearer 60 than 120: the box must already sit at 60.
    const group = container.querySelector('[data-element-id="a"] g[transform]');
    const translate = /translate\(\s*(-?[\d.]+)[\s,]+(-?[\d.]+)\s*\)/.exec(group?.getAttribute("transform") ?? "");
    expect(translate, "the box drew no transform to read a position from").not.toBeNull();
    expect(Number(translate![2])).toBeCloseTo(60, 5);

    fireEvent(target, pointer("pointerup", { clientX: 0, clientY: 70 }));
    unmount();
  });
});

describe("the lattice is declared per axis, and may be read per element", () => {
  it("puts a line at the declared origin rather than at 0", () => {
    // Lines at 10, 70, 130: a top moved to 75 rests on 70, where a lattice through 0 says 60.
    const landed = dragBy(definitionWith({ y: { step: STEP, origin: 10 } }), [resting], "a", 0, 75);

    expect(landed === null ? null : landed.y - HEIGHT / 2).toBe(70);
  });

  it("snaps x on the element's LEFT edge when x is declared", () => {
    // Left on 0, 100 wide, moved 37 on a 25 lattice: the left rests on 25 and the centre on 75.
    const landed = dragBy(definitionWith({ x: { step: 25 } }), [resting], "a", 37, 0);

    expect(landed?.x).toBe(75);
  });

  it("reads a bound step from each element, and leaves an element without one free", () => {
    // The timeline's case: a date-only element snaps to days, an element with a time of day does
    // not. The step is a payload field only the first carries.
    const definition = definitionWith({ x: { step: { path: "payload.stepUnits" } } });
    const snapping: DiagramModelElement = { ...resting, id: "a", payload: { stepUnits: 25 } };
    const free: DiagramModelElement = { ...resting, id: "b", y: 200, payload: {} };

    expect(dragBy(definition, [snapping, free], "a", 37, 0)?.x).toBe(75);
    expect(dragBy(definition, [snapping, free], "b", 37, 0)?.x).toBe(87);
  });

  it("leaves the axis free when the step does not resolve to a positive number", () => {
    const zero = dragBy(definitionWith({ x: { step: 0 } }), [resting], "a", 37, 0);
    const missing = dragBy(definitionWith({ x: { step: { path: "payload.none" } } }), [{ ...resting, payload: {} }], "a", 37, 0);

    expect(zero?.x).toBe(87);
    expect(missing?.x).toBe(87);
  });
});
