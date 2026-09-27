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
/** Chooses a tag in the filter: typed, then taken with Enter as a reader would. */
const chooseTag = (container: HTMLElement, text: string) => {
  const field = container.querySelector(".library-filter .tag-input-field")!;
  fireEvent.change(field, { target: { value: text } });
  fireEvent.keyDown(field, { key: "Enter" });
};
const filterMode = (container: HTMLElement) => container.querySelector(".library-filter-mode") as HTMLButtonElement;

describe("a declared filter", () => {
  it("draws only the elements having any of the chosen tags", () => {
    // Arrange.
    const { container } = render(canvasOf(modelOf()));
    expect(drawnElements(container)).toEqual(["a", "b", "c"]);

    // Act, assert: `c` has no industry; `a` has it.
    chooseTag(container, "industry");
    expect(drawnElements(container)).toEqual(["a"]);

    // Act, assert: any of industry or transport.
    chooseTag(container, "transport");
    expect(drawnElements(container)).toEqual(["a", "c"]);
  });

  it("draws only the elements having all of the chosen tags once switched to all", () => {
    // Arrange: energy or industry is a and b.
    const { container } = render(canvasOf(modelOf()));
    chooseTag(container, "energy");
    chooseTag(container, "industry");
    expect(drawnElements(container)).toEqual(["a", "b"]);

    // Act.
    fireEvent.click(filterMode(container));

    // Assert: only `a` has both, and the switch says so.
    expect(drawnElements(container), "the switch did not narrow the filter to all of the tags").toEqual(["a"]);
    expect(filterMode(container).textContent).toBe("All");
  });

  it("draws no connection touching a filtered element", () => {
    // Arrange.
    const { container } = render(canvasOf(modelOf()));

    // Act: `c` is filtered out, `a` and `b` stay.
    chooseTag(container, "energy");

    // Assert: a -> b stays, a -> c goes with `c`.
    expect(drawnElements(container)).toEqual(["a", "b"]);
    expect(drawnConnections(container)).toEqual(["a-b"]);
  });

  it("looks typed text up among the tags in the diagram, and a chip's x takes its tag out", () => {
    // Arrange.
    const { container, getByRole } = render(canvasOf(modelOf()));
    const field = container.querySelector(".library-filter .tag-input-field")!;

    // Act: typed text finds the diagram's tags that contain it.
    fireEvent.change(field, { target: { value: "r" } });

    // Assert: none starts with it, so alphabetically.
    expect([...container.querySelectorAll(".tag-input-suggestion")].map((option) => option.textContent)).toEqual(["energy", "industry", "transport"]);

    // Act: Enter takes the first; its x takes it out again.
    fireEvent.keyDown(field, { key: "Enter" });
    expect(drawnElements(container)).toEqual(["a", "b"]);
    fireEvent.click(getByRole("button", { name: "Remove energy" }));

    // Assert: nothing chosen, nothing hidden.
    expect(drawnElements(container)).toEqual(["a", "b", "c"]);
  });

  it("survives a new model: a delta does not clear the tags the reader chose", () => {
    // Arrange.
    const { container, rerender } = render(canvasOf(modelOf()));
    chooseTag(container, "transport");

    // Act: the model changes under the filter, with a new transport element in it.
    rerender(canvasOf(modelOf([{ id: "d", type: "item", x: 600, y: 0, width: 100, height: 40, payload: { tags: ["Transport"] } }])));

    // Assert.
    expect(drawnElements(container)).toEqual(["c", "d"]);
    expect([...container.querySelectorAll(".library-filter .tag-input-chip-text")].map((chip) => chip.textContent)).toEqual(["transport"]);
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

describe("a declared filter legend", () => {
  function legendOf(legend: NonNullable<DiagramDefinition["filter"]>["legend"]) {
    const { container } = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={{ ...definition, filter: { ...definition.filter!, legend } }} model={modelOf()} events={{}} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );
    return container;
  }

  it("draws a swatch and a caption per entry, in order, under the filter box", () => {
    // Arrange, act.
    const container = legendOf([
      { caption: "First", swatchClass: "swatch-one" },
      { caption: "Second", swatchClass: "swatch-two" },
    ]);

    // Assert: inside the filter's box, after its input.
    const legend = container.querySelector(".library-filter .library-filter-row ~ .library-filter-legend")!;
    expect(legend, "no legend under the filter box").not.toBeNull();
    const entries = [...legend.querySelectorAll(".library-filter-legend-entry")].map((entry) => ({
      caption: entry.textContent,
      swatch: entry.querySelector(".library-filter-legend-swatch")?.classList.contains(entry.textContent === "First" ? "swatch-one" : "swatch-two"),
    }));
    expect(entries).toEqual([{ caption: "First", swatch: true }, { caption: "Second", swatch: true }]);
  });

  it("draws no legend where the filter declares none", () => {
    expect(legendOf(undefined).querySelector(".library-filter-legend")).toBeNull();
  });
});
