import { describe, expect, it } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * The declared filter box: elements whose tags do not match are not drawn, and neither is any
 * connection touching them. The tags are placeholders; the library knows no notation's words.
 */

const definition: DiagramDefinition = {
  elementTypes: [{ id: "item", shape: "box", anchors: { kind: "edge" }, sizing: "model" }],
  relationTypes: [
    { id: "link", route: "straight", endpoints: { source: { elementTypes: ["item"] }, target: { elementTypes: ["item"] }, allowSelf: false } },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  filter: { field: "payload.tags", label: "Filter by tags" },
};

function modelOf(extra: DiagramModel["elements"] = []): DiagramModel {
  return {
    elements: [
      { id: "a", type: "item", x: 0, y: 0, width: 100, height: 40, payload: { tags: ["energy", "industry"] } },
      { id: "b", type: "item", x: 200, y: 0, width: 100, height: 40, payload: { tags: ["energy"] } },
      { id: "c", type: "item", x: 400, y: 0, width: 100, height: 40, payload: { tags: ["transport"] } },
      ...extra,
    ],
    connections: [
      { id: "a-b", type: "link", sourceId: "a", targetId: "b" },
      { id: "a-c", type: "link", sourceId: "a", targetId: "c" },
    ],
  };
}

function canvasOf(model: DiagramModel) {
  return (
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>
  );
}

const drawnElements = (container: HTMLElement) =>
  [...container.querySelectorAll("[data-element-id]")].map((element) => element.getAttribute("data-element-id"));
const drawnConnections = (container: HTMLElement) =>
  [...container.querySelectorAll("[data-connection-id]")].map((connection) => connection.getAttribute("data-connection-id"));
const typeFilter = (container: HTMLElement, text: string) =>
  fireEvent.change(container.querySelector(".library-filter-input")!, { target: { value: text } });

describe("a declared filter", () => {
  it("draws only the elements whose tags match", () => {
    // Arrange.
    const { container } = render(canvasOf(modelOf()));
    expect(drawnElements(container)).toEqual(["a", "b", "c"]);

    // Act.
    typeFilter(container, "energy and (transport or industry)");

    // Assert: `a` has energy and industry; `b` has energy alone; `c` has no energy.
    expect(drawnElements(container)).toEqual(["a"]);
  });

  it("draws no connection touching a filtered element", () => {
    // Arrange.
    const { container } = render(canvasOf(modelOf()));

    // Act: `c` is filtered out, `a` and `b` stay.
    typeFilter(container, "energy");

    // Assert: a -> b stays, a -> c goes with `c`.
    expect(drawnElements(container)).toEqual(["a", "b"]);
    expect(drawnConnections(container)).toEqual(["a-b"]);
  });

  it("keeps the last good filter, and says where the text went wrong, when it does not parse", () => {
    // Arrange: a good filter first.
    const { container } = render(canvasOf(modelOf()));
    typeFilter(container, "transport");
    expect(drawnElements(container)).toEqual(["c"]);

    // Act: an unclosed parenthesis.
    typeFilter(container, "energy and (industry");

    // Assert: still the transport filter, and the error names the parenthesis's position.
    expect(drawnElements(container)).toEqual(["c"]);
    expect(container.querySelector(".library-filter-error")?.textContent).toBe("The parenthesis at 12 is never closed.");
  });

  it("survives a new model: a delta does not clear what the reader typed", () => {
    // Arrange.
    const { container, rerender } = render(canvasOf(modelOf()));
    typeFilter(container, "transport");

    // Act: the model changes under the filter, with a new transport element in it.
    rerender(canvasOf(modelOf([{ id: "d", type: "item", x: 600, y: 0, width: 100, height: 40, payload: { tags: ["Transport"] } }])));

    // Assert.
    expect(drawnElements(container)).toEqual(["c", "d"]);
    expect((container.querySelector(".library-filter-input") as HTMLInputElement).value).toBe("transport");
  });

  it("shows no filter box where the definition declares none", () => {
    const { container } = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={{ ...definition, filter: undefined }} model={modelOf()} events={{}} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );
    expect(container.querySelector(".library-filter")).toBeNull();
  });
});
