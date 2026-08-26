import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { DeltaSchema } from "@client/generated/deltas_pb";
import { ElementSchema } from "@client/generated/elements_pb";
import {
  WardleyElementPayloadSchema,
  WardleyEvolutionAxisPayloadSchema,
} from "@client/generated/wardley-map_pb";
import { applyDelta, emptyModel, type WardleyModel } from "./wardleyModel";

let currentModel: WardleyModel = emptyModel;
let currentLoading = false;
let currentFailed = false;

vi.mock("./useWardleyStream", () => ({
  useWardleyStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    moveElementTo: () => Promise.resolve(""),
  }),
}));

vi.mock("@client/shell/panels/DiagramViewContext", () => ({
  useRegisterDiagramView: () => undefined,
}));

const { WardleyCanvas } = await import("./WardleyCanvas");

/** The four stages exactly as the backend derives them, which is the only place they exist. */
const STAGES = [
  { label: "Genesis", start: 0, end: 0.175 },
  { label: "Custom Built", start: 0.175, end: 0.4 },
  { label: "Product (+rental)", start: 0.4, end: 0.7 },
  { label: "Commodity (+utility)", start: 0.7, end: 1 },
];

function addDelta(id: string, type: string, payload: Uint8Array, x = 0, y = 0) {
  return create(DeltaSchema, {
    action: {
      case: "add",
      value: {
        elements: [
          create(ElementSchema, {
            id: { value: id },
            type,
            position: { x, y },
            payload: { typeUrl: `type.googleapis.com/${type}`, value: payload },
          }),
        ],
      },
    },
  });
}

/** A model carrying the axis, as every real baseline does. */
function withAxis(title = "Tea shop"): WardleyModel {
  const payload = toBinary(
    WardleyEvolutionAxisPayloadSchema,
    create(WardleyEvolutionAxisPayloadSchema, { title, stages: STAGES }),
  );
  return applyDelta(emptyModel, addDelta("axis", "wardley/map+evolution-axis", payload));
}

function renderCanvas(model: WardleyModel, options?: { loading?: boolean; failed?: boolean }) {
  currentModel = model;
  currentLoading = options?.loading ?? false;
  currentFailed = options?.failed ?? false;
  return render(
    <WardleyCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["map.adp"]} />,
  );
}

describe("WardleyCanvas chrome", () => {
  it("draws a band for every stage the backend sent", () => {
    // Act.
    const { container } = renderCanvas(withAxis());

    // Assert.
    expect(container.querySelectorAll(".wardley-band")).toHaveLength(4);
  });

  it("places each band at the boundaries the backend sent, not at constants of its own", () => {
    // Arrange. Requirement 8.2 - the boundaries are not published in the DSL, so the client
    // holds no copy. Sending deliberately different stages proves it is reading them.
    const payload = toBinary(
      WardleyEvolutionAxisPayloadSchema,
      create(WardleyEvolutionAxisPayloadSchema, {
        stages: [
          { label: "First", start: 0, end: 0.25 },
          { label: "Second", start: 0.25, end: 1 },
        ],
      }),
    );

    // Act.
    const { container } = renderCanvas(
      applyDelta(emptyModel, addDelta("axis", "wardley/map+evolution-axis", payload)),
    );

    // Assert. Drawn into a 1000-unit space, so 0.25 is x=250.
    const bands = container.querySelectorAll(".wardley-band");
    expect(bands).toHaveLength(2);
    expect(bands[1].getAttribute("x")).toBe("250");
    expect(bands[0].getAttribute("width")).toBe("250");
  });

  it("labels the four stages with the notation's own names", () => {
    // Act.
    const { container } = renderCanvas(withAxis());

    // Assert. "Product" without "(+rental)" is a different claim about the stage.
    const labels = [...container.querySelectorAll(".wardley-band-label")].map((node) => node.textContent);
    expect(labels).toEqual(["Genesis", "Custom Built", "Product (+rental)", "Commodity (+utility)"]);
  });

  it("draws a boundary line between stages but not before the first", () => {
    // Act.
    const { container } = renderCanvas(withAxis());

    // Assert. Three boundaries for four bands; a line at x=0 would be the axis, not a boundary.
    expect(container.querySelectorAll(".wardley-band-edge")).toHaveLength(3);
  });

  it("names both axes and both ends of the value chain", () => {
    // Act.
    const { container } = renderCanvas(withAxis());

    // Assert. A component's position means nothing without them.
    const text = container.textContent ?? "";
    expect(text).toContain("Value chain");
    expect(text).toContain("Evolution");
    expect(text).toContain("Visible");
    expect(text).toContain("Invisible");
  });

  it("draws the chrome before the elements, so nothing is hidden behind a band", () => {
    // Arrange. Requirement 8.4 - the axis chrome is what this module draws first.
    const payload = toBinary(WardleyElementPayloadSchema, create(WardleyElementPayloadSchema, { name: "Alpha" }));
    const model = applyDelta(withAxis(), addDelta("a", "wardley/map+element", payload, 0.5, 0.5));

    // Act.
    const { container } = renderCanvas(model);

    // Assert. In SVG, paint order is document order.
    const chrome = container.querySelector(".wardley-chrome");
    const contents = container.querySelector(".wardley-contents");
    expect(chrome).not.toBeNull();
    expect(contents).not.toBeNull();
    expect(chrome!.compareDocumentPosition(contents!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("shows the axes and nothing else for an empty map", () => {
    // Act. Requirement 1.4 - a map with no components is a valid map.
    const { container } = renderCanvas(withAxis("Empty"));

    // Assert.
    expect(container.querySelectorAll(".wardley-band")).toHaveLength(4);
    expect(container.querySelectorAll(".wardley-element")).toHaveLength(0);
  });

  it("draws no bands before the baseline has arrived", () => {
    // Act. There is nothing to draw them from, and inventing four would mean holding the
    // constants this design keeps backend-side.
    const { container } = renderCanvas(emptyModel, { loading: true });

    // Assert.
    expect(container.querySelectorAll(".wardley-band")).toHaveLength(0);
  });

  it("says so when the map cannot be opened", () => {
    // Act.
    const { container } = renderCanvas(emptyModel, { failed: true });

    // Assert.
    expect(container.textContent).toContain("could not be opened");
    expect(container.querySelector("svg")).toBeNull();
  });

  it("names the map for a screen reader when the document gave it a title", () => {
    // Act.
    const { container } = renderCanvas(withAxis("Tea shop"));

    // Assert.
    expect(container.querySelector("svg")?.getAttribute("aria-label")).toBe("Wardley map: Tea shop");
  });
});

describe("WardleyCanvas elements", () => {
  it("places a component where the document put it", () => {
    // Arrange. The backend already converted [visibility, maturity] into a canvas point, so a
    // highly visible genesis component arrives at (0.1, 0.1) and belongs top-left.
    const payload = toBinary(WardleyElementPayloadSchema, create(WardleyElementPayloadSchema, { name: "Alpha" }));
    const model = applyDelta(withAxis(), addDelta("a", "wardley/map+element", payload, 0.1, 0.1));

    // Act.
    const { container } = renderCanvas(model);

    // Assert.
    const element = container.querySelector(".wardley-element");
    expect(element?.getAttribute("cx")).toBe("100");
    expect(element?.getAttribute("cy")).toBe("100");
  });
});
