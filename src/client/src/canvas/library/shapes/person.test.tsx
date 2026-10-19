import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvasCore } from "../DiagramCanvas";
import type { DiagramDefinition } from "../definition/diagramDefinition";
import type { DiagramModel } from "../api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * The `person` shape: a box with rounded corners of radius 8 over the element's bounds, and a round
 * head of radius 10 centred 8 above the box's top edge, so the body covers the bottom of the head.
 *
 * The head is drawn first and the body over it, which is what makes the body cover the head's bottom;
 * the order is asserted for that reason. The geometry is read from the attributes, because a shape's
 * position is an attribute and no stylesheet moves it; the paint is read as the computed style, because
 * an SVG attribute loses to CSS and only the computed value says what is filled.
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
});
