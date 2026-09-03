import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, waitFor } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { DeltaSchema } from "@client/generated/deltas_pb";
import { ElementSchema } from "@client/generated/elements_pb";
import {
  WardleyAnnotationPayloadSchema,
  WardleyAttitudeKind,
  WardleyAttitudePayloadSchema,
  WardleyDecorator,
  WardleyElementKind,
  WardleyElementPayloadSchema,
  WardleyEvolutionAxisPayloadSchema,
  WardleyLinkPayloadSchema,
} from "@client/generated/wardley-map_pb";
import { applyDelta, emptyModel, type WardleyModel } from "./wardleyModel";
import { ToolboxItemSchema, type ToolboxItem } from "@client/generated/diagrams_pb";
import { DiagramToolboxProvider, useDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";

let currentModel: WardleyModel = emptyModel;
let currentLoading = false;
let currentFailed = false;
let moves: { elementId: string; x: number; y: number }[] = [];
let moveAnswer = "";

vi.mock("./useWardleyStream", () => ({
  useWardleyStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve(moveAnswer);
    },
  }),
}));

vi.mock("@client/shell/panels/DiagramViewContext", () => ({
  useRegisterDiagramView: () => undefined,
}));

let currentToolboxItems: ToolboxItem[] = [];
let toolboxRequests: (readonly string[])[] = [];

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: (_projectId: Uint8Array, path: readonly string[]) => {
    toolboxRequests.push(path);
    return currentToolboxItems;
  },
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
  moves = [];
  moveAnswer = "";
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

/** Adds one element of `type` to a model that already carries the axis. */
function withElement(
  model: WardleyModel,
  id: string,
  type: string,
  payload: Uint8Array,
  x = 0.5,
  y = 0.5,
): WardleyModel {
  return applyDelta(model, addDelta(id, type, payload, x, y));
}

function element(fields: Parameters<typeof create<typeof WardleyElementPayloadSchema>>[1]) {
  return toBinary(WardleyElementPayloadSchema, create(WardleyElementPayloadSchema, fields));
}

describe("WardleyCanvas elements", () => {
  it("places a component where the document put it", () => {
    // Arrange. The backend already converted [visibility, maturity] into a canvas point, so a
    // highly visible genesis component arrives at (0.1, 0.1) and belongs top-left.
    const model = withElement(withAxis(), "a", "wardley/map+element", element({ name: "Alpha" }), 0.1, 0.1);

    // Act.
    const { container } = renderCanvas(model);

    // Assert.
    const shape = container.querySelector(".wardley-element");
    expect(shape?.getAttribute("cx")).toBe("100");
    expect(shape?.getAttribute("cy")).toBe("100");
  });

  it("tells the three kinds apart by shape rather than by colour", () => {
    // Arrange. Requirement 8.3 - a map printed in grey still has to say which is which.
    let model = withElement(withAxis(), "c", "wardley/map+element", element({ name: "C", kind: WardleyElementKind.COMPONENT }));
    model = withElement(model, "a", "wardley/map+element", element({ name: "A", kind: WardleyElementKind.ANCHOR }), 0.2, 0.2);
    model = withElement(model, "s", "wardley/map+element", element({ name: "S", kind: WardleyElementKind.SUBMAP }), 0.3, 0.3);

    // Act.
    const { container } = renderCanvas(model);

    // Assert. A circle, a square, and a ringed circle.
    expect(container.querySelector(".wardley-kind-component circle.wardley-element")).not.toBeNull();
    expect(container.querySelector(".wardley-kind-anchor rect.wardley-element")).not.toBeNull();
    expect(container.querySelector(".wardley-kind-submap .wardley-element-outer")).not.toBeNull();
  });

  it("spells out the decorators and inertia rather than using a glyph", () => {
    // Arrange. Requirement 6.9 - legible without entering an edit mode, and a symbol the reader
    // has to learn is not legible.
    const model = withElement(
      withAxis(),
      "p",
      "wardley/map+element",
      element({ name: "Payment", decorators: [WardleyDecorator.BUY], inertia: true }),
    );

    // Act.
    const { container } = renderCanvas(model);

    // Assert.
    expect(container.querySelector(".wardley-element-badges")?.textContent).toBe("buy · inertia");
    expect(container.querySelector(".wardley-inertia")).not.toBeNull();
  });

  it("draws an evolving component at both positions, joined", () => {
    // Arrange. Requirement 6.1 - the pair is the point of the statement.
    const model = withElement(
      withAxis(),
      "d",
      "wardley/map+element",
      element({
        name: "Datacentre",
        evolve: { maturity: 0.83, evolutionStage: "Commodity (+utility)", overrideName: "Cloud Hosting" },
      }),
      0.2,
      0.5,
    );

    // Act.
    const { container } = renderCanvas(model);

    // Assert. The current position, the target, the line between them, and the arrival name.
    expect(container.querySelector(".wardley-evolve")).not.toBeNull();
    expect(container.querySelector(".wardley-evolve-target")?.getAttribute("cx")).toBe("830");
    expect(container.textContent).toContain("Cloud Hosting");
  });

  it("draws a link between two elements and marks a flow link differently", () => {
    // Arrange.
    let model = withElement(withAxis(), "a", "wardley/map+element", element({ name: "A" }), 0.2, 0.2);
    model = withElement(model, "b", "wardley/map+element", element({ name: "B" }), 0.8, 0.8);
    const link = toBinary(
      WardleyLinkPayloadSchema,
      create(WardleyLinkPayloadSchema, { sourceId: "a", targetId: "b", isFlow: true }),
    );
    model = applyDelta(model, addDelta("l", "wardley/map+link", link));

    // Act.
    const { container } = renderCanvas(model);

    // Assert.
    expect(container.querySelectorAll(".wardley-link")).toHaveLength(1);
    expect(container.querySelector(".wardley-link-flow")).not.toBeNull();
  });

  it("does not draw a link whose endpoint does not resolve", () => {
    // Arrange. Requirement 3.5 - the map still opens; there is simply nowhere to draw the line
    // to, and the validator reports the dangling name.
    let model = withElement(withAxis(), "a", "wardley/map+element", element({ name: "A" }), 0.2, 0.2);
    const link = toBinary(
      WardleyLinkPayloadSchema,
      create(WardleyLinkPayloadSchema, { sourceId: "a", targetId: "", targetName: "Nowhere" }),
    );
    model = applyDelta(model, addDelta("l", "wardley/map+link", link));

    // Act.
    const { container } = renderCanvas(model);

    // Assert. No line, and the rest of the map is still drawn.
    expect(container.querySelectorAll(".wardley-link")).toHaveLength(0);
    expect(container.querySelectorAll(".wardley-element")).toHaveLength(1);
  });

  it("honours a label offset in pixels rather than map coordinates", () => {
    // Arrange. Requirement 5.5 - the offset is a property of the format, reproduced rather than
    // corrected. At a 1000-unit space, a -57px offset must not be read as -57 units of map.
    const model = withElement(
      withAxis(),
      "k",
      "wardley/map+element",
      element({ name: "Kettle", labelOffset: { x: -57, y: 4 } }),
      0.5,
      0.5,
    );

    // Act.
    const { container } = renderCanvas(model);

    // Assert.
    const label = container.querySelector(".wardley-element-label");
    expect(label?.getAttribute("x")).toBe("443");
    expect(label?.getAttribute("y")).toBe("504");
  });

  it("draws every occurrence of a multi-position annotation", () => {
    // Arrange. Requirement 6.8 - one annotation, several pins, none of them lost.
    const payload = toBinary(
      WardleyAnnotationPayloadSchema,
      create(WardleyAnnotationPayloadSchema, {
        number: 1,
        text: "Standardising power",
        occurrences: [
          { x: 0.49, y: 0.57 },
          { x: 0.79, y: 0.92 },
        ],
      }),
    );
    const model = applyDelta(withAxis(), addDelta("n", "wardley/map+annotation", payload, 0.49, 0.57));

    // Act.
    const { container } = renderCanvas(model);

    // Assert. Two marks, both numbered 1.
    const marks = container.querySelectorAll(".wardley-annotation");
    expect(marks).toHaveLength(2);
    expect([...marks].every((mark) => mark.textContent?.includes("1"))).toBe(true);
  });

  it("draws an attitude region behind the elements it covers", () => {
    // Arrange. Requirement 6.4.
    const payload = toBinary(
      WardleyAttitudePayloadSchema,
      create(WardleyAttitudePayloadSchema, {
        kind: WardleyAttitudeKind.PIONEERS,
        opposite: { x: 0.55, y: 0.8 },
      }),
    );
    let model = applyDelta(withAxis(), addDelta("att", "wardley/map+attitude", payload, 0.2, 0.3));
    model = withElement(model, "a", "wardley/map+element", element({ name: "A" }), 0.3, 0.4);

    // Act.
    const { container } = renderCanvas(model);

    // Assert. Present, sized from the two corners, and painted before the element.
    const region = container.querySelector(".wardley-attitude");
    expect(region?.getAttribute("width")).toBe("350");
    const shape = container.querySelector(".wardley-element-group");
    expect(region!.compareDocumentPosition(shape!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});

describe("WardleyCanvas dragging", () => {
  /** A surface whose size makes one screen pixel one canvas unit, so pixels convert cleanly. */
  function sizeSurface(surface: SVGSVGElement) {
    vi.spyOn(surface, "getBoundingClientRect").mockReturnValue({
      width: 1180,
      height: 1180,
      top: 0,
      left: 0,
      right: 1180,
      bottom: 1180,
      x: 0,
      y: 0,
      toJSON: () => ({}),
    } as DOMRect);
  }

  function renderDraggable() {
    const model = withElement(withAxis(), "a", "wardley/map+element", element({ name: "Alpha" }), 0.5, 0.5);
    const rendered = renderCanvas(model);
    sizeSurface(rendered.container.querySelector("svg")!);
    return rendered;
  }

  it("sends a drag as a document edit, in canvas coordinates", async () => {
    // Arrange.
    const { container } = renderDraggable();
    const surface = container.querySelector("svg")!;

    // Act. 100px right and down; the space is 1000 units, so 0.1 of the map each way.
    fireEvent.mouseDown(container.querySelector("[data-element-id='a']")!, { clientX: 500, clientY: 500 });
    fireEvent.mouseMove(surface, { clientX: 600, clientY: 600 });
    fireEvent.mouseUp(surface);

    // Assert. Requirement 7.2 - position is meaning here, so this is an edit rather than a view
    // change, and the backend converts the point back into the document's own axes.
    await waitFor(() => expect(moves).toHaveLength(1));
    expect(moves[0].elementId).toBe("a");
    expect(moves[0].x).toBeCloseTo(0.6, 5);
    expect(moves[0].y).toBeCloseTo(0.6, 5);
  });

  it("writes nothing for a press that never moved", () => {
    // Arrange. The other side of the same requirement: a click is not a drag, and a click must
    // not put a component somewhere.
    const { container } = renderDraggable();

    // Act.
    fireEvent.mouseDown(container.querySelector("[data-element-id='a']")!, { clientX: 500, clientY: 500 });
    fireEvent.mouseUp(container.querySelector("svg")!);

    // Assert.
    expect(moves).toHaveLength(0);
  });

  it("clamps the shape inside the map while the pointer is still down", () => {
    // Arrange. Requirement 7.3 - a component cannot be more evolved than commodity, and the
    // user must not be shown a position that cannot exist.
    const { container } = renderDraggable();

    // Act. Far past the right-hand edge.
    fireEvent.mouseDown(container.querySelector("[data-element-id='a']")!, { clientX: 500, clientY: 500 });
    fireEvent.mouseMove(container.querySelector("svg")!, { clientX: 2000, clientY: 500 });

    // Assert.
    expect(container.querySelector("[data-element-id='a'] circle")?.getAttribute("cx")).toBe("1000");
  });

  it("moves the links that reach an element with it", () => {
    // Arrange. If the dragged position is not substituted everywhere, the line detaches from
    // the shape while the gesture is in flight.
    let model = withElement(withAxis(), "a", "wardley/map+element", element({ name: "A" }), 0.5, 0.5);
    model = withElement(model, "b", "wardley/map+element", element({ name: "B" }), 0.9, 0.9);
    const link = toBinary(
      WardleyLinkPayloadSchema,
      create(WardleyLinkPayloadSchema, { sourceId: "a", targetId: "b" }),
    );
    const { container } = renderCanvas(applyDelta(model, addDelta("l", "wardley/map+link", link)));
    const surface = container.querySelector("svg")!;
    sizeSurface(surface);
    const before = container.querySelector(".wardley-link")?.getAttribute("d");

    // Act.
    fireEvent.mouseDown(container.querySelector("[data-element-id='a']")!, { clientX: 500, clientY: 500 });
    fireEvent.mouseMove(surface, { clientX: 400, clientY: 500 });

    // Assert.
    expect(container.querySelector(".wardley-link")?.getAttribute("d")).not.toBe(before);
  });

  it("shows a refusal rather than swallowing it", async () => {
    // Arrange. Requirement 7.5 - a read-only map refuses, and the user is told why.
    const { container } = renderDraggable();
    moveAnswer = "This map is read-only.";
    const surface = container.querySelector("svg")!;

    // Act.
    fireEvent.mouseDown(container.querySelector("[data-element-id='a']")!, { clientX: 500, clientY: 500 });
    fireEvent.mouseMove(surface, { clientX: 600, clientY: 500 });
    fireEvent.mouseUp(surface);

    // Assert. The shape snaps back because the model never changed, and the reason is visible.
    await waitFor(() => expect(container.textContent).toContain("This map is read-only."));
  });

  it("does not pan the surface while an element is being dragged", () => {
    // Arrange. Both gestures begin with a mouse down; the element has to take it.
    const { container } = renderDraggable();
    const surface = container.querySelector("svg")!;
    const before = surface.getAttribute("viewBox");

    // Act.
    fireEvent.mouseDown(container.querySelector("[data-element-id='a']")!, { clientX: 500, clientY: 500 });
    fireEvent.mouseMove(surface, { clientX: 600, clientY: 600 });

    // Assert.
    expect(surface.getAttribute("viewBox")).toBe(before);
  });
});

/** Reads what the shell's Toolbox panel reads: null is what renders the "Open a diagram" placeholder. */
function ToolboxProbe() {
  const items = useDiagramToolbox();
  return <div data-testid="toolbox-probe">{items === null ? "placeholder" : "palette:" + items.map((item) => item.label).join(",")}</div>;
}

describe("WardleyCanvas toolbox", () => {
  it("registers the backend-described palette with the shell while mounted", () => {
    // Arrange. The Toolbox panel shows its placeholder until a mounted canvas registers -
    // which an open wardley map must therefore do (tests.md, documentation task 9: this
    // palette stayed on the placeholder while c4 and mindmap filled theirs on the same flow).
    currentModel = withAxis();
    currentToolboxItems = [
      create(ToolboxItemSchema, { id: "wardley.toolbox.component", label: "Component", dropActionId: "wardley.add-component" }),
      create(ToolboxItemSchema, { id: "wardley.toolbox.anchor", label: "Anchor", dropActionId: "wardley.add-anchor" }),
    ];
    toolboxRequests = [];
    const path = ["diagrams", "wardley-map", "example 1", "tea.adp"];

    // Act.
    const { getByTestId } = render(
      <DiagramToolboxProvider>
        <WardleyCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={path} />
        <ToolboxProbe />
      </DiagramToolboxProvider>,
    );

    // Assert: the shell sees this canvas's palette, asked for this diagram's own path.
    expect(getByTestId("toolbox-probe").textContent).toBe("palette:Component,Anchor");
    expect(toolboxRequests[0]).toEqual(path);
  });
});
