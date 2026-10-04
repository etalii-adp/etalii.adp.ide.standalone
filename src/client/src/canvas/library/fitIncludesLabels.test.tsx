import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvas } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { widthOf } from "../label/textMetrics";

/**
 * Fit to View shows an element's labels, not only its box.
 *
 * ## The defect
 *
 * The fitted view was the union of the element boxes. A label drawn beside its element rather than
 * inside it - the Sankey diagram names its first column's bars to their left and its last column's
 * to their right - fell outside that union, so the opened diagram had its outermost names cut off
 * at the edge of the pane, and Fit to View, the one control meant to show everything, cut them too.
 */

const NAME = "A rather long name";
const FONT_SIZE = 13;

const definitionWith = (align: "start" | "end"): DiagramDefinition => ({
  elementTypes: [
    {
      id: "bar",
      shape: "box",
      sizing: "model",
      anchors: { kind: "edge" },
      // Pushed past the box's own edge, as a Sankey bar's name is: right-aligned and inset by more
      // than the bar is wide, so the text runs out of the box on the side it is aligned to.
      labels: [{ text: { path: "payload.name" }, align, insetX: 30, typography: { fontSize: FONT_SIZE } }],
    },
  ],
  relationTypes: [],
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

const model: DiagramModel = {
  elements: [{ id: "a", type: "bar", x: 0, y: 0, width: 20, height: 100, payload: { name: NAME } }],
  connections: [],
};

function fittedViewBox(definition: DiagramDefinition): { x: number; y: number; w: number; h: number } {
  const { container, unmount } = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvas definition={definition} model={model} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
  const [x, y, w, h] = container.querySelector("svg[viewBox]")!.getAttribute("viewBox")!.split(" ").map(Number);
  unmount();
  return { x: x!, y: y!, w: w!, h: h! };
}

describe("Fit to View includes the labels drawn beside an element", () => {
  const text = widthOf(NAME, FONT_SIZE);

  it("reaches a label that runs out of the box to the left", () => {
    // The box spans -10..10; the name ends 10 left of it (inset 30 from the right edge) and runs
    // its whole width further left.
    const view = fittedViewBox(definitionWith("end"));
    expect(view.x).toBeLessThanOrEqual(-20 - text);
  });

  it("reaches a label that runs out of the box to the right", () => {
    const view = fittedViewBox(definitionWith("start"));
    expect(view.x + view.w).toBeGreaterThanOrEqual(20 + text);
  });
});
