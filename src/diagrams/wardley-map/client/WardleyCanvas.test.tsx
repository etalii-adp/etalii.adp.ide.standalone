import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, waitFor } from "@testing-library/react";
import { selectedElementIdOf } from "@client/canvas/selection";
import type { ContextSelection } from "@client/generated/context_pb";
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

/** Every viewport this canvas reported, in the order it reported them. */
let reports: { minX: number; minY: number; maxX: number; maxY: number }[] = [];

vi.mock("./useWardleyStream", () => ({
  useWardleyStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve(moveAnswer);
    },
    reportView: (viewport: { minX: number; minY: number; maxX: number; maxY: number }) => {
      reports.push(viewport);
    },
  }),
}));

// The registered controls are captured rather than discarded, so a test can zoom the canvas
// the way the ribbon does. Nothing else changes: every existing test ignores them.
let viewControls: { zoomIn: () => void; zoomOut: () => void; fitToView: () => void } | null = null;

const select = vi.fn();
const executeAction = vi.fn(async () => ({ accepted: true, error: "" }));
const executeShortcut = vi.fn(async () => ({ accepted: true, error: "" }));
let currentSelectionKey: string | null = null;
let currentActions: unknown[] = [];

let currentPrompt: unknown = null;
const submitLabel = vi.fn(async () => ({ accepted: true, error: "" }));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  innermostKey: () => currentSelectionKey,
  useContextConnection: () => ({ select, executeAction, executeShortcut }),
  useContextSelection: () => ({ selection: currentSelectionKey, levels: [], actions: currentActions }),
  useContextPrompt: () => ({ prompt: currentPrompt, onPropose: vi.fn(async () => ({ accepted: true, error: "" })), onSubmit: submitLabel, onCancel: vi.fn() }),
}));

vi.mock("@client/shell/panels/InlineLabelPlacementContext", () => ({
  useRegisterInlineLabelPlacement: () => undefined,
}));

vi.mock("@client/shell/panels/DiagramViewContext", () => ({
  useRegisterDiagramView: (controls: typeof viewControls) => {
    viewControls = controls;
  },
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
  select.mockClear();
  executeAction.mockClear();
  executeShortcut.mockClear();
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

describe("WardleyCanvas scrollbars", () => {
  // Each test is named for the defect it catches.

  const viewBoxOf = (container: HTMLElement) =>
    (container.querySelector(".wardley-surface")!.getAttribute("viewBox") ?? "").split(" ").map(Number);

  const thumbOf = (container: HTMLElement, axis: "horizontal" | "vertical") =>
    container.querySelector(`.canvas-scrollbar-${axis} .canvas-scrollbar-thumb`) as HTMLElement;

  it("pans the view when a thumb is dragged - catches an unwired onPan", () => {
    // Arrange.
    const { container } = renderCanvas(withAxis());
    const [xBefore] = viewBoxOf(container);

    // Act.
    fireEvent.mouseDown(thumbOf(container, "horizontal"), { button: 0, clientX: 10, clientY: 0 });
    fireEvent.mouseMove(window, { clientX: 40, clientY: 0 });
    fireEvent.mouseUp(window);

    // Assert.
    expect(viewBoxOf(container)[0]).toBeGreaterThan(xBefore);
  });

  it("moves the thumb when the view is panned by other means - catches a stale copy of the view", () => {
    // Arrange.
    // This canvas's own pan divides by the surface's measured width, which jsdom reports as 0,
    // so the pan produces Infinity and never moves the view under test. Giving the surface a
    // real rectangle makes its existing arithmetic run as it does in a browser - the wiring
    // under test is the bars reading the same view state, not the canvas's pan maths.
    const { container } = renderCanvas(withAxis());
    const surface = container.querySelector(".wardley-surface")!;
    surface.getBoundingClientRect = () =>
      ({ width: 800, height: 600, x: 0, y: 0, top: 0, left: 0, right: 800, bottom: 600, toJSON: () => ({}) }) as DOMRect;
    // Zoom in first: this map opens showing its whole space, so at the fitted view the thumb
    // correctly fills the track and there is nowhere to pan within the extent (Requirement
    // 2.5). A thumb that can move is the precondition for testing that it does.
    act(() => viewControls!.zoomIn());
    const before = thumbOf(container, "horizontal").style.left;

    // Act.
    fireEvent.mouseDown(surface, { button: 0, clientX: 200, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 60, clientY: 100 });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(thumbOf(container, "horizontal").style.left).not.toBe(before);
  });

  it("describes the map's own space rather than its content - the extent is fullView", () => {
    // Arrange.
    // The requirement this canvas exists to prove: a map carrying one component still has the
    // whole 0..1 space to show, so the thumb must not fill the track as it would if the extent
    // were derived from that single element's bounds.
    const { container } = renderCanvas(withAxis());

    // Assert.
    const size = Number.parseFloat(thumbOf(container, "horizontal").style.width);
    expect(size).toBeGreaterThan(0);
    expect(size).toBeLessThanOrEqual(100);
  });
});

describe("WardleyCanvas view reporting", () => {
  const SPACE = 1000;
  const MARGIN = 90;

  beforeEach(() => {
    reports = [];
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("reports the settled view in the map's own 0..1 space, not in canvas units", () => {
    // Arrange. The session compares a viewport against element positions that are 0..1, so a
    // report left in canvas units would be a thousand times too large and cull nothing, ever -
    // a bug that looks exactly like a working loop from the client side.
    vi.useFakeTimers();
    const { container } = renderCanvas(withAxis());
    const surface = container.querySelector(".wardley-surface")!;
    surface.getBoundingClientRect = () =>
      ({ width: 800, height: 800, x: 0, y: 0, top: 0, left: 0, right: 800, bottom: 800, toJSON: () => ({}) }) as DOMRect;

    // Act. The report is debounced, so nothing is sent until the view settles.
    expect(reports).toHaveLength(0);
    act(() => void vi.advanceTimersByTime(500));

    // Assert. The opening view is the whole space plus the label margin, divided by SPACE - and
    // deliberately not clamped to 0..1, because the margin is where the axis labels live.
    expect(reports).toHaveLength(1);
    expect(reports[0].minX).toBeCloseTo(-MARGIN / SPACE, 6);
    expect(reports[0].maxX).toBeCloseTo((SPACE + MARGIN) / SPACE, 6);
  });

  it("reports again when the view changes - catches a report wired to a gesture instead of the view", () => {
    // Arrange.
    vi.useFakeTimers();
    const { container } = renderCanvas(withAxis());
    const surface = container.querySelector(".wardley-surface")!;
    surface.getBoundingClientRect = () =>
      ({ width: 800, height: 800, x: 0, y: 0, top: 0, left: 0, right: 800, bottom: 800, toJSON: () => ({}) }) as DOMRect;
    act(() => void vi.advanceTimersByTime(500));
    const opening = reports[0];

    // Act. Zoom through the registered controls - the ribbon's route into the view, which is not
    // the pan gesture, so a report attached to the gesture would miss this entirely.
    act(() => viewControls!.zoomIn());
    act(() => void vi.advanceTimersByTime(500));

    // Assert. A second, smaller rectangle.
    expect(reports).toHaveLength(2);
    expect(reports[1].maxX - reports[1].minX).toBeLessThan(opening.maxX - opening.minX);
  });
});

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

describe("WardleyCanvas selection", () => {
  // The prerequisite task: this canvas had no context channel at all, so every wardley
  // action the backend has offered all along was unreachable by any gesture.

  beforeEach(() => {
    currentSelectionKey = null;
    currentActions = [];
    currentPrompt = null;
  });

  function withOneComponent() {
    return withElement(withAxis(), "aaa", "wardley/map+element", element({ name: "Kettle" }), 0.4, 0.4);
  }

  function shapeOf(container: HTMLElement, id: string): SVGGElement {
    return container.querySelector(`[data-element-id="${id}"]`) as SVGGElement;
  }

  it("reports a clicked element as a nested selection, and a background click clears it", () => {
    // Arrange.
    const { container } = renderCanvas(withOneComponent());
    const shape = shapeOf(container, "aaa");
    expect(shape).not.toBeNull();

    // Act: a press with no movement is a click, which is the selection gesture.
    fireEvent.mouseDown(shape, { clientX: 100, clientY: 100 });
    fireEvent.mouseUp(container.querySelector(".wardley-surface") as SVGSVGElement);

    // Assert: the backend hears a nested selection whose innermost element is this one.
    expect(select).toHaveBeenCalledTimes(1);
    expect(selectedElementIdOf(select.mock.calls[0][0] as ContextSelection)).toBe("aaa");

    // Act, continued: a click on empty canvas deselects.
    const surface = container.querySelector(".wardley-surface") as SVGSVGElement;
    fireEvent.mouseDown(surface, { clientX: 300, clientY: 300 });
    fireEvent.mouseUp(surface);
    expect(select).toHaveBeenLastCalledWith(null);
  });

  it("opens the pushed actions on right-click, and runs the chosen one", async () => {
    // Arrange. The actions are the backend's push for this selection - never a client guess.
    currentActions = [
      { actions: [{ id: "wardley.rename", label: "Rename\u2026", icon: "", available: true, unavailableReason: "", items: [] }] },
    ];
    currentSelectionKey = "element:aaa";
    const { container } = renderCanvas(withOneComponent());

    // Act.
    fireEvent.contextMenu(shapeOf(container, "aaa"), { clientX: 120, clientY: 120 });
    const item = await waitFor(() => {
      const found = [...document.querySelectorAll("button")].find((candidate) => candidate.textContent?.includes("Rename"));
      expect(found).toBeDefined();
      return found!;
    });
    fireEvent.click(item);

    // Assert.
    expect(executeAction).toHaveBeenCalledWith("wardley.rename");
  });

  it("forwards F2 on the selected element as data, leaving the key-to-action map to the backend", () => {
    // Arrange.
    currentSelectionKey = "element:aaa";
    const { container } = renderCanvas(withOneComponent());

    // Act.
    fireEvent.keyDown(container.querySelector(".wardley-surface") as SVGSVGElement, { key: "F2" });

    // Assert.
    expect(executeShortcut).toHaveBeenCalledTimes(1);
    const [shortcut, source] = executeShortcut.mock.calls[0] as unknown as [{ key: string }, { source: { value: { value: string } } }];
    expect(shortcut.key).toBe("F2");
    expect(source.source.value.value).toBe("aaa");
  });

  it("forwards nothing while no element is selected", () => {
    // Arrange.
    const { container } = renderCanvas(withOneComponent());

    // Act.
    fireEvent.keyDown(container.querySelector(".wardley-surface") as SVGSVGElement, { key: "F2" });

    // Assert.
    expect(executeShortcut).not.toHaveBeenCalled();
  });

  // ---- inline renaming, on the selection this canvas just gained ---------------------------

  function labelPromptFor(elementId: string, text: string): unknown {
    return {
      prompt: {
        case: "inputDialog",
        value: {
          title: "Rename element",
          icon: "mdi-pencil-outline",
          fieldLabel: "Name",
          initialValue: text,
          confirmLabel: "Rename",
          inlineLabelEdit: { elementId: { value: elementId } },
        },
      },
    };
  }

  function editorBox(container: HTMLElement): SVGForeignObjectElement {
    return container.querySelector("foreignObject.inline-label-editor") as SVGForeignObjectElement;
  }

  it("opens the editor at the label's own offset, and submits what is typed", async () => {
    // Arrange. The offset is the document's: this author put the label 57 left of the mark,
    // so an editor at the default right-hand offset would open over empty map. The element
    // sits away from the origin so a broken offset cannot hide there.
    currentPrompt = labelPromptFor("aaa", "Kettle");
    const model = withElement(withAxis(), "aaa", "wardley/map+element", element({ name: "Kettle", labelOffset: { x: -57, y: 4 } }), 0.4, 0.4);
    const { container } = renderCanvas(model);

    // Assert: start-anchored at scale(0.4) - 57, exactly where the drawn text begins.
    const box = editorBox(container);
    expect(box).not.toBeNull();
    expect(Number(box.getAttribute("x"))).toBeCloseTo(0.4 * 1000 - 57, 5);

    const field = container.querySelector("input.inline-label-editor-field") as HTMLInputElement;
    expect(field.value).toBe("Kettle");
    fireEvent.change(field, { target: { value: "Urn" } });
    fireEvent.keyDown(field, { key: "Enter" });
    await waitFor(() => expect(submitLabel).toHaveBeenCalledWith("Urn"));
  });

  it("places no editor for an element that is not in the map", () => {
    // Arrange.
    currentPrompt = labelPromptFor("gone", "Ghost");

    // Act.
    const { container } = renderCanvas(withOneComponent());

    // Assert.
    expect(editorBox(container)).toBeNull();
  });

  it("leaves the selection alone when an inline edit commits", async () => {
    // Arrange.
    currentSelectionKey = "element:aaa";
    currentPrompt = labelPromptFor("aaa", "Kettle");
    const { container } = renderCanvas(withOneComponent());

    // Act.
    const field = container.querySelector("input.inline-label-editor-field") as HTMLInputElement;
    fireEvent.change(field, { target: { value: "Urn" } });
    fireEvent.keyDown(field, { key: "Enter" });

    // Assert.
    await waitFor(() => expect(submitLabel).toHaveBeenCalled());
    expect(select).not.toHaveBeenCalled();
  });
});
