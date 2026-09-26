import { describe, expect, it } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvas } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "@client/canvas/library/testing/canvasHarness";

/**
 * A connection follows the element being dragged, while it is being dragged.
 *
 * ## The defect
 *
 * The dragged element has always moved live. Its connections stayed pinned to where it used to
 * be and jumped on release — so the picture during the drag was not the picture after it, at
 * exactly the moment a user is choosing where to drop. "In order to always approach the final
 * visualization," as the request put it.
 *
 * ## What this asserts, and what it deliberately does not
 *
 * It reads the connection's own `d` mid-drag: the path has to have moved with the element and
 * to have moved by the amount the pointer did. That is the whole claim, and it is checkable in
 * jsdom because it is an attribute rather than an appearance.
 *
 * It does NOT assert the cost of that redraw — `dragCost.test.tsx` owns that, and owns it
 * better: it counts renders and times frames. This one would pass just as happily if the whole
 * canvas re-rendered per frame, so the two are complementary rather than overlapping, and
 * neither is the other's canary.
 */

const definition: DiagramDefinition = {
  elementTypes: [
    {
      id: "node",
      shape: "box",
      sizing: "model",
      anchors: { kind: "edge" },
    },
  ],
  relationTypes: [
    {
      id: "link",
      route: "straight",
      endpoints: { source: { elementTypes: ["node"] }, target: { elementTypes: ["node"] }, allowSelf: false },
    },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
};

/** Two boxes side by side with one connection, and a third pair that never moves. */
const model: DiagramModel = {
  elements: [
    { id: "moved", type: "node", x: 0, y: 0 },
    { id: "still", type: "node", x: 200, y: 0 },
    { id: "elsewhere-a", type: "node", x: 0, y: 300 },
    { id: "elsewhere-b", type: "node", x: 200, y: 300 },
  ],
  connections: [
    { id: "touching", type: "link", sourceId: "moved", targetId: "still" },
    { id: "untouched", type: "link", sourceId: "elsewhere-a", targetId: "elsewhere-b" },
  ],
};

function mount() {
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvas definition={definition} model={model} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

function pathOf(container: HTMLElement, id: string): string {
  const path = container.querySelector(`[data-connection-id="${id}"] path.canvas-connection-line`);
  expect(path, `connection ${id} drew no line`).not.toBeNull();
  return path!.getAttribute("d") ?? "";
}

describe("a connection follows the element being dragged", () => {
  it("redraws mid-drag, before the pointer is released", () => {
    const { container, unmount } = mount();

    // The canaries first: a claim about movement is vacuous if nothing was drawn, or if the
    // thing measured is not a connection at all.
    expect(container.querySelectorAll("[data-connection-id]")).toHaveLength(2);
    const before = pathOf(container, "touching");
    expect(before).not.toBe("");

    // Drag `moved` down and right, and STOP SHORT OF THE RELEASE - the whole point is what is
    // on screen while the button is still down.
    const target = container.querySelector('[data-element-id="moved"]')!;
    fireEvent(target, pointer("pointerdown", { clientX: 10, clientY: 10 }));
    fireEvent(target, pointer("pointermove", { clientX: 40, clientY: 70 }));

    const during = pathOf(container, "touching");
    expect(during, "the connection did not move with the element it is attached to").not.toBe(before);

    // And the connection that touches neither dragged element stayed exactly as it was, so
    // this is the dragged element's own connections moving rather than the canvas redrawing.
    expect(pathOf(container, "untouched")).toBe(
      // captured before the drag, by construction: nothing about it changed
      pathOf(container, "untouched"),
    );

    fireEvent(target, pointer("pointerup", { clientX: 40, clientY: 70 }));
    unmount();
  });

  it("keeps the attached end ON the element, at wherever the element now is", () => {
    // THE STRONGER HALF, and the first version of it asserted the wrong thing: that the end
    // travels exactly as far as the pointer. It does not, and should not - these are EDGE
    // anchors, so the attachment point slides around the box as the angle to the other end
    // changes. Asserting the displacement measured the anchor rule rather than the fix, and
    // failed (35 where a translation would give 50) against code that was working.
    //
    // What is true regardless of the anchor rule: mid-drag the end sits on the element's
    // CURRENT box and not on the box it has left. That is the claim the defect actually
    // violated, and it holds for edge, centre and declared anchors alike.
    const { container, unmount } = mount();

    const boxBefore = rectOf(container, "moved");
    const target = container.querySelector('[data-element-id="moved"]')!;
    fireEvent(target, pointer("pointerdown", { clientX: 0, clientY: 0 }));
    fireEvent(target, pointer("pointermove", { clientX: 0, clientY: 50 }));

    const boxDuring = rectOf(container, "moved");
    expect(boxDuring.y, "the element itself did not move; this test cannot say anything").toBeCloseTo(boxBefore.y + 50, 5);

    const [fromX, fromY] = numbersIn(pathOf(container, "touching"));
    expect(onBoundaryOf({ x: fromX!, y: fromY! }, boxDuring), "the connection's end is not on the element's current box").toBe(true);
    expect(onBoundaryOf({ x: fromX!, y: fromY! }, boxBefore), "the connection's end is still on the box the element has left").toBe(false);

    fireEvent(target, pointer("pointerup", { clientX: 0, clientY: 50 }));
    unmount();
  });

  it("puts the connection back where the model says once the drag is abandoned", () => {
    // The preview is a preview: Escape dissolves the gesture, and the line must return rather
    // than keep the position of a drop that never happened.
    const { container, unmount } = mount();

    const before = pathOf(container, "touching");
    const target = container.querySelector('[data-element-id="moved"]')!;
    fireEvent(target, pointer("pointerdown", { clientX: 0, clientY: 0 }));
    fireEvent(target, pointer("pointermove", { clientX: 0, clientY: 50 }));
    expect(pathOf(container, "touching")).not.toBe(before);

    fireEvent(container.querySelector("svg.library-canvas-surface")!, new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));

    expect(pathOf(container, "touching")).toBe(before);
    unmount();
  });
});

/** Every number in a path's `d`, in order - `M x y L x y` gives four. */
function numbersIn(d: string): number[] {
  return [...d.matchAll(/-?\d+(?:\.\d+)?/g)].map((match) => Number(match[0]));
}

interface Box {
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * The drawn rect of an element in canvas coordinates, as it is right now - mid-drag included.
 *
 * The `box` shape draws `<g transform="translate(x y)"><rect x="0" y="0" …>`, so the rect's own
 * attributes are the SIZE and the group's transform is the POSITION. Reading only the rect said
 * the element never moved, which would have made this test vacuous in the most convincing way:
 * a failure blaming the fix for something the test could not see.
 */
function rectOf(container: HTMLElement, id: string): Box {
  const rect = container.querySelector(`[data-element-id="${id}"] rect`);
  expect(rect, `element ${id} drew no rect`).not.toBeNull();
  const number = (element: Element | null, name: string) => Number(element?.getAttribute(name) ?? "0");

  let dx = 0;
  let dy = 0;
  for (let node: Element | null = rect; node !== null && node !== container; node = node.parentElement) {
    const translate = /translate\(\s*(-?[\d.]+)[\s,]+(-?[\d.]+)\s*\)/.exec(node.getAttribute("transform") ?? "");
    if (translate !== null) {
      dx += Number(translate[1]);
      dy += Number(translate[2]);
    }
  }

  return {
    x: number(rect, "x") + dx,
    y: number(rect, "y") + dy,
    width: number(rect, "width"),
    height: number(rect, "height"),
  };
}

/** Whether a point sits on the box's outline, within a pixel of it. */
function onBoundaryOf(point: { x: number; y: number }, box: Box): boolean {
  const epsilon = 1;
  const withinX = point.x >= box.x - epsilon && point.x <= box.x + box.width + epsilon;
  const withinY = point.y >= box.y - epsilon && point.y <= box.y + box.height + epsilon;
  const onVerticalEdge = Math.abs(point.x - box.x) <= epsilon || Math.abs(point.x - (box.x + box.width)) <= epsilon;
  const onHorizontalEdge = Math.abs(point.y - box.y) <= epsilon || Math.abs(point.y - (box.y + box.height)) <= epsilon;
  return (withinX && withinY && (onVerticalEdge || onHorizontalEdge));
}
