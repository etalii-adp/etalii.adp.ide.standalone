import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { BuiltInShape, DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * The shapes a node plotted on a coordinate system is drawn as: a circle, and a line across or down its
 * box. Read off the rendered SVG, so a drawing that fell back to the box would fail here.
 */

function renderShape(shape: BuiltInShape, width: number, height: number, selectable = true) {
  const definition: DiagramDefinition = {
    elementTypes: [{ id: "spot", shape, anchors: { kind: "edge" }, sizing: "model", ...(selectable ? {} : { selectable: false }) }],
    relationTypes: [],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
  const model: DiagramModel = { elements: [{ id: "s", type: "spot", x: 100, y: 50, width, height }], connections: [] };
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  ).container.querySelector('[data-element-id="s"]')!;
}

const attributesOf = (node: Element | null, names: readonly string[]) => Object.fromEntries(names.map((name) => [name, Number(node?.getAttribute(name))]));

describe("a node drawn as a circle or a line", () => {
  it("draws a circle about its centre, its radius half the box's shorter side", () => {
    const circle = renderShape("circle", 40, 20).querySelector("circle");

    expect(attributesOf(circle, ["cx", "cy", "r"])).toEqual({ cx: 100, cy: 50, r: 10 });
  });

  it("draws a horizontal line across its box's middle, edge to edge", () => {
    const line = renderShape("horizontal-line", 60, 10).querySelector("line");

    expect(attributesOf(line, ["x1", "y1", "x2", "y2"])).toEqual({ x1: 70, y1: 50, x2: 130, y2: 50 });
  });

  it("draws a vertical line down its box's middle, edge to edge", () => {
    const line = renderShape("vertical-line", 10, 60).querySelector("line");

    expect(attributesOf(line, ["x1", "y1", "x2", "y2"])).toEqual({ x1: 100, y1: 20, x2: 100, y2: 80 });
  });

  it("gives a line its box to be pressed on, where it can be picked", () => {
    expect(renderShape("horizontal-line", 60, 10).querySelector("rect.library-bodyless-hit")).not.toBeNull();
    expect(renderShape("horizontal-line", 60, 10, false).querySelector("rect.library-bodyless-hit")).toBeNull();
  });
});
