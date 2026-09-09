import { describe, expect, it } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvas, snapToStep } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

/**
 * A dragged element comes to rest on the step its diagram type declares.
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
 */

const STEP = 60;

const definition: DiagramDefinition = {
  elementTypes: [{ id: "node", shape: "box", sizing: "model", anchors: { kind: "edge" } }],
  relationTypes: [],
  snap: { y: { step: STEP } },
  layout: { modes: ["manual"] },
  dragging: "enabled",
};

const model: DiagramModel = {
  elements: [{ id: "a", type: "node", x: 0, y: 0 }],
  connections: [],
};

function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, button: 0, ...init });
}

/** Drags `a` by `dy` and returns the y the canvas reported on release. */
function dragBy(dy: number): number | null {
  let landed: number | null = null;
  const { container, unmount } = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvas
          definition={definition}
          model={model}
          events={{ onElementMoved: ({ position }) => void (landed = position.y) }}
        />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );

  const target = container.querySelector('[data-element-id="a"]')!;
  fireEvent(target, pointer("pointerdown", { clientX: 0, clientY: 0 }));
  fireEvent(target, pointer("pointermove", { clientX: 0, clientY: dy }));
  fireEvent(target, pointer("pointerup", { clientX: 0, clientY: dy }));
  unmount();
  return landed;
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
    expect(dragBy(70)).toBe(60);
    expect(dragBy(20)).toBe(0);
  });

  it("snaps a drag ABOVE the origin the same way", () => {
    // The same asymmetry, end to end rather than in the helper: a canvas whose origin is not
    // at its top edge - every timeline with a row above the first - would otherwise land one
    // row out in only that direction, which reads as an intermittent bug.
    expect(dragBy(-70)).toBe(-60);
    expect(dragBy(-30)).toBe(-60);
    expect(dragBy(-20)).toBe(0);
  });

  it("shows the snapped position DURING the drag, not only on release", () => {
    // The user chose snap-during-drag over snap-on-drop, for the same reason the connectors
    // follow the element: the picture during the gesture should be the picture after it. A
    // preview that ignored the step would show a position the drop then quietly changes.
    const { container, unmount } = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvas definition={definition} model={model} events={{}} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );

    const target = container.querySelector('[data-element-id="a"]')!;
    fireEvent(target, pointer("pointerdown", { clientX: 0, clientY: 0 }));
    fireEvent(target, pointer("pointermove", { clientX: 0, clientY: 70 }));

    // 70 is nearer 60 than 120, so the element must already be sitting at 60 - not at 70.
    const group = container.querySelector('[data-element-id="a"] g[transform]');
    const translate = /translate\(\s*(-?[\d.]+)[\s,]+(-?[\d.]+)\s*\)/.exec(group?.getAttribute("transform") ?? "");
    expect(translate, "the box drew no transform to read a position from").not.toBeNull();

    // Measured against the box's OWN height rather than the size the definition asks for: the
    // library falls back to its own default where a type does not set one, and hard-coding the
    // expected half-height made this fail against working code (40 where 50 was asserted).
    const height = Number(container.querySelector('[data-element-id="a"] rect')?.getAttribute("height") ?? "0");
    expect(height).toBeGreaterThan(0);
    expect(Number(translate![2])).toBeCloseTo(60 - height / 2, 5);

    fireEvent(target, pointer("pointerup", { clientX: 0, clientY: 70 }));
    unmount();
  });
});
