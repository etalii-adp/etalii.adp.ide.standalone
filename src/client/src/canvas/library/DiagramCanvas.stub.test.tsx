import { describe, expect, it } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition, StubSide } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "./testing/canvasHarness";

/**
 * A relation without a target, drawn as its relation type's stub (DISL 6.10 *Stubs*): a straight
 * line of the stub's length out of the middle of one side of the source, with its label beside it.
 *
 * Before the stub existed a connection with no target drew nothing at all - `connectionEnds` found
 * no element and returned null - so two modules drew their open ends as an element of their own.
 */

/** One source, centred at (100, 50), 80 by 40: its sides' middles are (140, 50), (60, 50), (100, 30) and (100, 70). */
function modelOf(targetId = ""): DiagramModel {
  return {
    elements: [
      { id: "a", type: "node", x: 100, y: 50, width: 80, height: 40, label: "A" },
      { id: "b", type: "node", x: 400, y: 50, width: 80, height: 40, label: "B" },
    ],
    connections: [{ id: "open", type: "uses", sourceId: "a", targetId, label: "lead", payload: { label: "redis" } }],
  };
}

function definitionOf(side: StubSide = "right", stub = true): DiagramDefinition {
  return {
    elementTypes: [{ id: "node", shape: "box", anchors: { kind: "edge" }, sizing: "model" }],
    relationTypes: [
      {
        id: "uses",
        route: "straight",
        className: "t-uses",
        lineClassName: "t-line",
        endpoints: { source: { elementTypes: ["node"] }, target: { elementTypes: ["node"] }, allowSelf: false },
        ...(stub
          ? {
            stub: {
              length: 48,
              side,
              className: "t-open",
              label: { text: { template: "{payload.label} (missing)" }, className: "t-open-label" },
            },
          }
          : {}),
      },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
}

function renderCanvas(definition: DiagramDefinition, model: DiagramModel) {
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

const stubOf = (container: HTMLElement) => container.querySelector('[data-connection-id="open"]');

describe("a relation without a target", () => {
  it("is drawn as its stub: a line of its length out of the middle of the source's right side", () => {
    const { container } = renderCanvas(definitionOf(), modelOf());

    const stub = stubOf(container);
    expect(stub, "nothing was drawn for the relation without a target").not.toBeNull();
    expect(stub!.getAttribute("class")).toBe("canvas-connection library-connection library-connection-stub t-uses t-open");
    const line = stub!.querySelector("path.canvas-connection-line");
    expect(line?.getAttribute("class")).toBe("canvas-connection-line library-connection-line t-line");
    expect(line?.getAttribute("d")).toBe("M 140 50 L 188 50");
    // A stub ends nowhere, so it carries no arrowhead to nothing.
    expect(line?.getAttribute("marker-end")).toBeNull();
  });

  it.each([
    ["left", "M 60 50 L 12 50"],
    ["top", "M 100 30 L 100 -18"],
    ["bottom", "M 100 70 L 100 118"],
  ] as const)("leaves the middle of the %s side", (side, d) => {
    const { container } = renderCanvas(definitionOf(side), modelOf());

    expect(stubOf(container)?.querySelector("path.canvas-connection-line")?.getAttribute("d")).toBe(d);
  });

  it("writes its label above the line, from just past the source, read from the connection's payload", () => {
    const { container } = renderCanvas(definitionOf(), modelOf());

    const label = stubOf(container)?.querySelector("text");
    expect(label?.textContent).toBe("redis (missing)");
    expect(label?.getAttribute("class")).toBe("library-connection-label t-open-label");
    expect(label?.getAttribute("x")).toBe("148");
    expect(label?.getAttribute("y")).toBe("44");
    // Inline, because `.canvas-connection text` centres every connection's text and a class loses to it.
    expect((label as SVGTextElement | null)?.style.textAnchor).toBe("start");
  });

  it("is the relation: pressing the stub selects the connection", () => {
    const { container } = renderCanvas(definitionOf(), modelOf());

    const hit = stubOf(container)!.querySelector("path.canvas-connection-hit")!;
    fireEvent(hit, pointer("pointerdown", { button: 0, clientX: 160, clientY: 50 }));
    fireEvent(hit, pointer("pointerup", { button: 0, clientX: 160, clientY: 50 }));

    expect(stubOf(container)?.classList.contains("canvas-selected")).toBe(true);
  });

  it("is not drawn where its relation type declares no stub", () => {
    const { container } = renderCanvas(definitionOf("right", false), modelOf());

    expect(stubOf(container)).toBeNull();
  });

  it("is a plain connection again once it has a target", () => {
    const { container } = renderCanvas(definitionOf(), modelOf("b"));

    expect(stubOf(container)?.getAttribute("class")).not.toContain("library-connection-stub");
    expect(stubOf(container)?.querySelector("text")?.textContent).toBe("lead");
  });
});
