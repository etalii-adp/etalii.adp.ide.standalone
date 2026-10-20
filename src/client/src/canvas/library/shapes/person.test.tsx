import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createEvent, fireEvent, render } from "@testing-library/react";
import { DiagramCanvas, DiagramCanvasCore } from "../DiagramCanvas";
import type { BuiltInShape, DiagramDefinition } from "../definition/diagramDefinition";
import type { DiagramModel } from "../api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider, TOOLBOX_DRAG_TYPE } from "@client/shell/panels/DiagramToolboxContext";

/**
 * The `person` shape, DISL 0.5 §6.7: a box with rounded corners over the element's bounds, and a round
 * head of radius 10 centred 8 above the box's top edge, so the body covers the bottom of the head.
 *
 * The head is drawn first and the body over it, which is what makes the body cover the head's bottom;
 * the order is asserted for that reason. The geometry is read from the attributes, because a shape's
 * position is an attribute and no stylesheet moves it; the paint is read as the computed style, because
 * an SVG attribute loses to CSS and only the computed value says what is filled.
 *
 * Beside the drawing, §6.7 says three things a box does not: the corner never exceeds half the smaller
 * side, relations meet the body alone, and the head is part of the node for everything that points at
 * it. A press on the head is the node's because the head is drawn inside the node's group, which no
 * jsdom test can tell from a head the browser lets presses through; what is computed from a point - a
 * drop - and from the drawing's extent - the fitted view - is tested here.
 */
describe("the person shape", () => {
  const definition: DiagramDefinition = {
    elementTypes: [{ id: "someone", shape: "person", anchors: { kind: "edge" }, sizing: "model", style: { fill: "#08427b" } }],
    relationTypes: [],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };

  function drawn(width: number, height: number) {
    const model: DiagramModel = { elements: [{ id: "p", type: "someone", x: 200, y: 100, width, height, label: "" }], connections: [] };
    const { container, unmount } = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={definition} model={model} events={{}} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );
    const element = container.querySelector('[data-element-id="p"]')!;
    const parts = [...element.querySelectorAll("circle.library-shape, rect.library-shape")];
    const result = parts.map((part) =>
      part.tagName === "circle"
        ? { part: "head", cx: Number(part.getAttribute("cx")), cy: Number(part.getAttribute("cy")), r: Number(part.getAttribute("r")), fill: getComputedStyle(part).fill }
        : {
            part: "body",
            x: Number(part.getAttribute("x")),
            y: Number(part.getAttribute("y")),
            width: Number(part.getAttribute("width")),
            height: Number(part.getAttribute("height")),
            rx: Number(part.getAttribute("rx")),
            fill: getComputedStyle(part).fill,
          },
    );
    unmount();
    return result;
  }

  it.each([[160, 90], [240, 120], [173.5, 39.6]] as const)("draws a head above a rounded body at %s by %s", (width, height) => {
    const left = 200 - width / 2;
    const top = 100 - height / 2;
    expect(drawn(width, height)).toEqual([
      { part: "head", cx: 200, cy: top - 8, r: 10, fill: "#08427b" },
      { part: "body", x: left, y: top, width, height, rx: 8, fill: "#08427b" },
    ]);
  });

  it.each([[12, 40, 6], [40, 12, 6], [16, 16, 8]] as const)("rounds the body of %s by %s by no more than half its smaller side, %s", (width, height, corner) => {
    // §6.7: the radius drawn is the smaller of `radius` and half the smaller side, so the body never
    // turns inside out; the head keeps its size whatever the bounds.
    const top = 100 - height / 2;
    expect(drawn(width, height)).toEqual([
      { part: "head", cx: 200, cy: top - 8, r: 10, fill: "#08427b" },
      { part: "body", x: 200 - width / 2, y: top, width, height, rx: corner, fill: "#08427b" },
    ]);
  });
});

/**
 * Where the head counts and where it does not. The surface is given a real size equal to the declared
 * extent - jsdom measures nothing - so a client pixel is a canvas unit and a point lands where the test
 * says. The person stands at (200, 200), 160 by 90: its body spans y 155..245 and its head, centred at
 * (200, 147) with radius 10, spans y 137..157.
 */
describe("the person shape's head", () => {
  const EXTENT = { x: 0, y: 0, width: 800, height: 400 };
  const HEAD = { x: 200, y: 140 };

  const definitionWith = (shape: BuiltInShape): DiagramDefinition => ({
    elementTypes: [
      { id: "someone", shape, anchors: { kind: "edge" }, sizing: "model" },
      { id: "plain", shape: "box", anchors: { kind: "edge" }, sizing: "model" },
    ],
    relationTypes: [{ id: "uses", route: "straight", endpoints: { source: { elementTypes: ["plain"], anchors: "edge" }, target: { elementTypes: ["someone"], anchors: "edge" }, allowSelf: false } }],
    layout: { modes: ["manual"] },
    dragging: "disabled",
    extent: EXTENT,
    toolbox: { drops: [{ payload: "note", targets: [{ elementTypes: ["someone"], actionId: "note-on" }], refusal: "Drop a note onto a person." }] },
  });

  const model: DiagramModel = {
    elements: [
      { id: "p", type: "someone", x: 200, y: 200, width: 160, height: 90, label: "P" },
      { id: "above", type: "plain", x: 200, y: 40, width: 100, height: 40, label: "Above" },
    ],
    connections: [{ id: "above->p", type: "uses", sourceId: "above", targetId: "p" }],
  };

  let restore: (() => void) | undefined;

  beforeEach(() => {
    const original = SVGSVGElement.prototype.getBoundingClientRect;
    SVGSVGElement.prototype.getBoundingClientRect = () =>
      ({ left: 0, top: 0, x: 0, y: 0, width: EXTENT.width, height: EXTENT.height, right: EXTENT.width, bottom: EXTENT.height, toJSON: () => ({}) }) as DOMRect;
    restore = () => { SVGSVGElement.prototype.getBoundingClientRect = original; };
  });

  afterEach(() => {
    restore?.();
    vi.restoreAllMocks();
  });

  function mounted(shape: BuiltInShape, events: React.ComponentProps<typeof DiagramCanvasCore>["events"] = {}) {
    return render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={definitionWith(shape)} model={model} events={events} toolboxItems={[]} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );
  }

  it("lands a toolbox drop on its head on the person, where a box of the same bounds refuses it", () => {
    const dropAt = (shape: BuiltInShape) => {
      const onElementDropped = vi.fn();
      const { container, unmount } = mounted(shape, { onElementDropped });
      const surface = container.querySelector("svg.library-canvas-surface")!;
      const event = createEvent.drop(surface, { dataTransfer: { types: [TOOLBOX_DRAG_TYPE], getData: () => "note", dropEffect: "" } });
      Object.defineProperty(event, "clientX", { value: HEAD.x });
      Object.defineProperty(event, "clientY", { value: HEAD.y });
      fireEvent(surface, event);
      unmount();
      return onElementDropped;
    };

    expect(dropAt("person")).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ onto: { elementId: "p", actionId: "note-on" } }));
    // The control: the point is outside the bounds, so without the head nothing is there.
    expect(dropAt("box")).not.toHaveBeenCalled();
  });

  it("meets a relation at the body's top edge, not at the head", () => {
    const { container } = mounted("person");
    const d = container.querySelector('[data-connection-id="above->p"] path.canvas-connection-line')!.getAttribute("d")!;
    const ends = [...d.matchAll(/(-?[\d.]+) (-?[\d.]+)/g)].map(([, x, y]) => ({ x: Number(x), y: Number(y) }));

    // The line runs straight down from the box above; its end on the person is on the body (y 155),
    // below the head's top (y 137) that an outline including the head would stop it at.
    expect(ends.at(-1)!.x).toBeCloseTo(200, 6);
    expect(ends.at(-1)!.y).toBeGreaterThanOrEqual(155 - 12);
    expect(ends.at(-1)!.y).toBeLessThanOrEqual(155);
  });

  it("is inside the fitted view", () => {
    const fittedTop = (shape: BuiltInShape) => {
      const alone: DiagramModel = { elements: [{ id: "p", type: "someone", x: 0, y: 0, width: 160, height: 90, label: "P" }], connections: [] };
      const { container, unmount } = render(
        <DiagramViewProvider>
          <DiagramToolboxProvider>
            <DiagramCanvas definition={{ ...definitionWith(shape), extent: undefined }} model={alone} events={{}} />
          </DiagramToolboxProvider>
        </DiagramViewProvider>,
      );
      const [, y] = container.querySelector("svg[viewBox]")!.getAttribute("viewBox")!.split(" ").map(Number);
      unmount();
      return y!;
    };

    // §6.7: what measures the drawing includes the head, which stands 18 above the bounds.
    expect(fittedTop("person")).toBeCloseTo(fittedTop("box") - 18, 6);
  });
});
