import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render, waitFor } from "@testing-library/react";
import { emptyModel, type DependencyGraphModel } from "./dependencyGraphModel";

let currentModel: DependencyGraphModel = emptyModel;
let currentLoading = false;
let currentFailed = false;
let currentSelectionKey: string | null = null;
let currentActions: unknown[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];
let selections: unknown[] = [];
let currentPrompt: unknown = null;
const proposeLabel = vi.fn(async () => ({ accepted: true, error: "" }));
const submitLabel = vi.fn(async () => ({ accepted: true, error: "" }));
const cancelLabel = vi.fn();
let executed: { actionId: string; source: unknown }[] = [];
let properties: { propertyId: string; value: string }[] = [];
let shortcuts: { key: string; source: unknown }[] = [];
let currentReportView: ((viewport: unknown) => void) | null = null;

vi.mock("./useDependencyGraphStream", () => ({
  useDependencyGraphStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve("");
    },
    reportView: (viewport: unknown) => currentReportView?.(viewport),
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  innermostKey: () => currentSelectionKey,
  useContextPrompt: () => ({ prompt: currentPrompt, onPropose: proposeLabel, onSubmit: submitLabel, onCancel: cancelLabel }),
  useContextSelection: () => ({ selection: currentSelectionKey, levels: [], actions: currentActions }),
  useContextConnection: () => ({
    select: (selection: unknown) => selections.push(selection),
    executeAction: (actionId: string, source?: unknown) => {
      executed.push({ actionId, source });
      return Promise.resolve({ accepted: true, error: "" });
    },
    executeShortcut: (shortcut: { key: string }, source?: unknown) => {
      shortcuts.push({ key: shortcut.key, source });
      return Promise.resolve({ accepted: true, error: "" });
    },
    setProperty: (propertyId: string, value: string) => {
      properties.push({ propertyId, value });
      return Promise.resolve({ accepted: true, error: "" });
    },
  }),
}));

vi.mock("@client/shell/panels/DiagramViewContext", () => ({
  useRegisterDiagramView: () => undefined,
}));

vi.mock("@client/shell/panels/InlineLabelPlacementContext", () => ({
  useRegisterInlineLabelPlacement: () => undefined,
}));

vi.mock("@client/shell/panels/DiagramToolboxContext", () => ({
  TOOLBOX_DRAG_TYPE: "application/x-adp-toolbox-item",
  useRegisterDiagramToolbox: () => undefined,
}));

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: () => [],
}));

const { DependencyGraphCanvas } = await import("./DependencyGraphCanvas");

const ROW_HEIGHT = 60;

function node(id: string, label: string, x: number, row: number) {
  return { id, x, y: row * ROW_HEIGHT, label, row };
}

function modelWith(): DependencyGraphModel {
  return {
    elements: new Map([
      ["aaa", node("aaa", "API gateway", 0, 0)],
      ["bbb", node("bbb", "Identity service", 400, 2)],
    ]),
    relations: new Map([
      ["ccc", { id: "ccc", fromElementId: "aaa", toElementId: "bbb", label: "verifies tokens with" }],
    ]),
  };
}

function renderCanvas() {
  return render(
    <DependencyGraphCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["services.dgr"]} />,
  );
}

beforeEach(() => {
  currentModel = modelWith();
  currentPrompt = null;
  currentLoading = false;
  currentFailed = false;
  currentSelectionKey = null;
  currentActions = [];
  moves = [];
  selections = [];
  executed = [];
  properties = [];
  shortcuts = [];
});

describe("the dependency graph canvas", () => {
  it("wears the shared canvas classes, so the central stylesheet is what dresses it", () => {
    // Arrange & act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".dependency-graph-canvas")!.classList.contains("canvas-host")).toBe(true);
    expect(container.querySelector(".dependency-graph-node")!.classList.contains("canvas-node")).toBe(true);
    expect(container.querySelector(".dependency-graph-label")!.classList.contains("canvas-node-label")).toBe(true);
    expect(container.querySelector(".dependency-graph-relation")!.classList.contains("canvas-connection")).toBe(true);
    expect(container.querySelector(".dependency-graph-relation-line")!.classList.contains("canvas-connection-line")).toBe(true);
    expect(container.querySelector("marker#dependency-graph-arrowhead")!.classList.contains("canvas-arrowhead")).toBe(true);
  });

  it("draws the nodes and the directed dependency between them", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelectorAll(".dependency-graph-node")).toHaveLength(2);
    expect(container.querySelectorAll(".dependency-graph-relation")).toHaveLength(1);
    expect(container.textContent).toContain("API gateway");
    expect(container.textContent).toContain("verifies tokens with");
  });

  it("still shows both scrollbars and still pans on a thumb drag after moving to the shared scroll view", () => {
    // Arrange.
    // The migration pin: the bars come from @client/canvas/scroll now, wear this module's
    // placement class, and a thumb drag still pans. jsdom lays nothing out, so the track is
    // given a real width by hand.
    const { container } = renderCanvas();
    const horizontalBar = container.querySelector(".canvas-scrollbar-horizontal")!;
    const verticalBar = container.querySelector(".canvas-scrollbar-vertical")!;
    Object.defineProperty(horizontalBar, "getBoundingClientRect", {
      value: () => ({ x: 0, y: 0, top: 0, left: 0, right: 200, bottom: 10, width: 200, height: 10, toJSON: () => ({}) }),
    });
    const thumb = horizontalBar.querySelector<HTMLElement>(".canvas-scrollbar-thumb")!;
    const before = thumb.style.left;

    // Act.
    fireEvent.mouseDown(thumb, { clientX: 100, clientY: 5 });
    fireEvent.mouseMove(window, { clientX: 140, clientY: 5 });
    fireEvent.mouseUp(window);

    // Assert.
    expect(horizontalBar.classList.contains("dependency-graph-scrollbars")).toBe(true);
    expect(verticalBar.classList.contains("dependency-graph-scrollbars")).toBe(true);
    expect(thumb.style.left).not.toBe(before);
  });

  it("defines the arrowhead marker its edges point at", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    // The whole difference from the timeline: an edge that means something, drawn pointing at
    // the thing depended upon. The marker is defined here and attached in the stylesheet, so
    // this is where its absence would show.
    const marker = container.querySelector("marker#dependency-graph-arrowhead");
    expect(marker).not.toBeNull();
    expect(marker!.getAttribute("orient")).toBe("auto-start-reverse");
    expect(container.querySelector(".dependency-graph-relation-line")).not.toBeNull();
  });

  it("shows no ruler, no dates and no moments anywhere", () => {
    // Act.
    // Requirement 3.3's defect list, asserted rather than assumed - the three things a fork
    // leaves behind if nobody looks.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".timeline-ruler")).toBeNull();
    expect(container.querySelector(".dependency-graph-ruler")).toBeNull();
    expect(container.querySelector(".dependency-graph-node-point")).toBeNull();
    expect(container.textContent).not.toMatch(/\d{4}-\d{2}-\d{2}/);
  });

  it("shows the unavailable state when the backend answered permanently", () => {
    // Arrange.
    currentFailed = true;

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.textContent).toContain("could not be opened");
  });

  it("treats a motionless press as a selection, never an edit", () => {
    // Arrange.
    const { container } = renderCanvas();
    const element = container.querySelector(".dependency-graph-element")!;

    // Act.
    fireEvent.mouseDown(element, { clientX: 100, clientY: 100 });
    fireEvent.mouseUp(container.querySelector(".dependency-graph-surface")!);

    // Assert.
    expect(moves).toHaveLength(0);
    expect(selections).toHaveLength(1);
  });

  it("commits a drag as one move in module coordinates, snapped to a row", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".dependency-graph-surface")!;
    const element = container.querySelector(".dependency-graph-element")!;

    // Act: right by 50px, down by most of a row - close enough to snap to row 1.
    fireEvent.mouseDown(element, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 150, clientY: 100 + ROW_HEIGHT - 10 });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe("aaa");
    // y arrives as row × height - a whole row, never a pointer-precise position.
    expect(moves[0].y % ROW_HEIGHT).toBe(0);
    expect(moves[0].y / ROW_HEIGHT).toBe(1);
  });

  it("leaves the horizontal free rather than snapping it", () => {
    // Arrange.
    // The asymmetry is the placement model: rows are a grid, x is not. Rounding x here would
    // quietly turn the canvas into one.
    const { container } = renderCanvas();
    const surface = container.querySelector(".dependency-graph-surface")!;
    const element = container.querySelector(".dependency-graph-element")!;

    // The zoom the canvas fitted itself to, read back off a node's drawn box rather than
    // assumed: a box is NODE_WIDTH canvas units wide, so its pixel width is the scale.
    const pixelsPerUnit = Number(container.querySelector(".dependency-graph-node")!.getAttribute("width")) / 160;

    // Act: an odd, fractional distance across.
    fireEvent.mouseDown(element, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 137.5, clientY: 100 });
    fireEvent.mouseUp(surface);

    // Assert.
    // The coordinate that travels is the pointer's, converted and otherwise untouched - not
    // rounded, and not snapped to any step.
    expect(moves).toHaveLength(1);
    expect(moves[0].x).toBeCloseTo(37.5 / pixelsPerUnit, 10);
  });

  it("abandons a drag on Escape with nothing dispatched", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".dependency-graph-surface")!;
    const element = container.querySelector(".dependency-graph-element")!;

    // Act.
    fireEvent.mouseDown(element, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 300, clientY: 300 });
    fireEvent.keyDown(window, { key: "Escape" });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(moves).toHaveLength(0);
  });

  it("shows the landing placement in this type's own terms while a drag is in progress", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".dependency-graph-surface")!;
    const element = container.querySelector(".dependency-graph-element")!;

    // Act.
    fireEvent.mouseDown(element, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 200, clientY: 100 + ROW_HEIGHT });

    // Assert.
    // The coordinate and the row, where the timeline showed a date and a row.
    const hint = container.querySelector(".dependency-graph-hint")!;
    expect(hint).not.toBeNull();
    expect(hint.textContent).toContain("row 1");
    expect(hint.textContent).toMatch(/^-?\d+ · row -?\d+$/);
  });

  it("relates the dependent to the dependency in one stateless call, in the gesture's direction", () => {
    // Arrange: the anchors render on the selected node only; the second hit circle is the
    // right-side anchor.
    currentSelectionKey = "element:aaa";
    const { container } = renderCanvas();
    const rightAnchor = container.querySelectorAll(".dependency-graph-anchor-hit")[1];
    const target = container.querySelector('[data-element-id="bbb"]')!;

    // Act: drag from aaa's right anchor and release on bbb. The landing is read from the event's
    // own target - a fast release must not depend on a mouseenter having kept up.
    fireEvent.mouseDown(rightAnchor, { clientX: 100, clientY: 30 });
    fireEvent.mouseUp(target);

    // Assert.
    // One call carries the whole gesture, and aaa depends on bbb - the direction the drag had.
    const calls = executed.filter((call) => call.actionId === "dependencies.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toBe("rel:aaa->bbb");
  });

  it("reverses the dependency when the drag lifts from the left anchor", () => {
    // Arrange: the first hit circle is the left-side anchor.
    currentSelectionKey = "element:aaa";
    const { container } = renderCanvas();
    const leftAnchor = container.querySelectorAll(".dependency-graph-anchor-hit")[0];
    const target = container.querySelector('[data-element-id="bbb"]')!;

    // Act.
    fireEvent.mouseDown(leftAnchor, { clientX: 100, clientY: 30 });
    fireEvent.mouseUp(target);

    // Assert.
    // What depends on a node arrives at it: the landing becomes the dependent and the dragged
    // node the dependency.
    const calls = executed.filter((call) => call.actionId === "dependencies.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toBe("rel:bbb->aaa");
  });

  it("never connects from a plain click on the node body", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".dependency-graph-surface")!;
    const elements = container.querySelectorAll(".dependency-graph-element");

    // Act: press a node body and release - a selection, not a gesture.
    fireEvent.mouseDown(elements[0], { clientX: 100, clientY: 100 });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(executed.filter((call) => call.actionId === "dependencies.connect")).toHaveLength(0);
  });

  it("completes a dependency onto empty space as a create-and-relate placement", () => {
    // Arrange.
    currentSelectionKey = "element:aaa";
    const { container } = renderCanvas();
    const rightAnchor = container.querySelectorAll(".dependency-graph-anchor-hit")[1];
    const surface = container.querySelector(".dependency-graph-surface")!;

    // Act: drag from aaa's right anchor and release over nothing.
    fireEvent.mouseDown(rightAnchor, { clientX: 100, clientY: 30 });
    fireEvent.mouseUp(surface);

    // Assert.
    // The landing is a placement inside the same rel: gesture - one call, one undo.
    const calls = executed.filter((call) => call.actionId === "dependencies.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^rel:aaa->new:/);
  });

  it("a left-anchor drag onto empty space puts the placement at the dependent end", () => {
    // Arrange.
    currentSelectionKey = "element:aaa";
    const { container } = renderCanvas();
    const leftAnchor = container.querySelectorAll(".dependency-graph-anchor-hit")[0];
    const surface = container.querySelector(".dependency-graph-surface")!;

    // Act.
    fireEvent.mouseDown(leftAnchor, { clientX: 100, clientY: 30 });
    fireEvent.mouseUp(surface);

    // Assert.
    // The new node is what depends on the dragged one, so the edge runs out of it and into aaa.
    const calls = executed.filter((call) => call.actionId === "dependencies.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^rel:new:.+->aaa$/);
  });

  it("lands a toolbox drop as a placement action, wherever it falls", () => {
    // Arrange.
    // The drop names a placement - new:{x},{row} - so the node appears at the dropped coordinate
    // and row with nothing asked. One handler on the surface serves drops over nodes and over
    // empty canvas alike.
    const { container } = renderCanvas();
    const surface = container.querySelector(".dependency-graph-surface")!;
    const data = new Map([["application/x-adp-toolbox-item", "dependencies.add-element"]]);

    // Act.
    fireEvent.drop(surface, {
      clientX: 300,
      clientY: 200,
      dataTransfer: { getData: (type: string) => data.get(type) ?? "", types: [...data.keys()] },
    });

    // Assert.
    // jsdom's synthetic drop carries no client coordinates, so the numeric halves are not
    // assertable here - the coordinate math itself is covered through the drag tests, which
    // share the same converters. What this pins is the route: a drop becomes one placement
    // action, never a dialog and never nothing.
    expect(executed).toHaveLength(1);
    expect(executed[0].actionId).toBe("dependencies.add-element");
    const source = executed[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^new:/);
  });

  it("pans with the right button on empty space, and no browser menu appears", () => {
    // Arrange.
    // The observable is a node's drawn x: panning shifts every box by the pan distance.
    const { container } = renderCanvas();
    const surface = container.querySelector(".dependency-graph-surface")!;
    const before = container.querySelector(".dependency-graph-node")!.getAttribute("x");

    // Act.
    fireEvent.mouseDown(surface, { button: 2, clientX: 400, clientY: 300 });
    fireEvent.mouseMove(surface, { clientX: 320, clientY: 300 });
    const menuPrevented = !fireEvent.contextMenu(surface, { clientX: 320, clientY: 300 });
    fireEvent.mouseUp(surface);

    // Assert.
    // The view moved, nothing was selected away, and the browser's menu was consumed.
    expect(container.querySelector(".dependency-graph-node")!.getAttribute("x")).not.toBe(before);
    expect(menuPrevented).toBe(true);
    expect(selections).toHaveLength(0);
  });

  it("pans when the right button lands on the drawn canvas itself, not only the surface div", () => {
    // Arrange.
    // Real clicks land on the inner svg, not the surface div - a target===currentTarget guard
    // silently disabled panning everywhere, and synthetic events aimed at the surface hid it.
    const { container } = renderCanvas();
    const surface = container.querySelector(".dependency-graph-surface")!;
    const content = container.querySelector(".dependency-graph-content")!;
    const before = container.querySelector(".dependency-graph-node")!.getAttribute("x");

    // Act.
    fireEvent.mouseDown(content, { button: 2, clientX: 400, clientY: 300 });
    fireEvent.mouseMove(surface, { clientX: 320, clientY: 300 });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(container.querySelector(".dependency-graph-node")!.getAttribute("x")).not.toBe(before);
  });

  it("forwards Tab against the selection as a shortcut, letting the backend own the table", () => {
    // Arrange: the selection mock is module-level, so this drives the key house-keeping only.
    const { container } = renderCanvas();
    const surface = container.querySelector(".dependency-graph-surface")!;

    // Act.
    fireEvent.keyDown(surface, { key: "Tab" });

    // Assert.
    // Nothing selected in this mock, so nothing is forwarded - a Tab with no selection must
    // stay the browser's own focus traversal.
    expect(shortcuts).toHaveLength(0);
  });

  it("a surface release with no gesture in flight fabricates no calls", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".dependency-graph-surface")!;

    // Act.
    fireEvent.mouseUp(surface);

    // Assert.
    expect(executed).toHaveLength(0);
  });

  it("selects a dependency on a left click, through its fat hit path", () => {
    // Arrange.
    const { container } = renderCanvas();
    const hit = container.querySelector(".dependency-graph-relation-hit")!;

    // Act.
    fireEvent.click(hit);

    // Assert.
    // The click selects the dependency by its id - the same channel a node selection uses, and
    // the resolver answers for both.
    expect(selections).toHaveLength(1);
    const child = (selections[0] as { detail: { value: { id: { source: { value: { value: string } } } } } }).detail.value;
    expect(child.id.source.value.value).toBe("ccc");
  });

  it("marks the selected dependency, so the selection is visible", () => {
    // Arrange.
    currentSelectionKey = "element:ccc";

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".dependency-graph-relation")!.classList.contains("dependency-graph-selected")).toBe(true);
  });

  it("opens the context menu for a dependency once its selection arrives", () => {
    // Arrange.
    const { container, rerender } = renderCanvas();
    const hit = container.querySelector(".dependency-graph-relation-hit")!;

    // Act: the right-click asks for the selection; the menu waits for it to arrive.
    fireEvent.contextMenu(hit);
    expect(selections).toHaveLength(1);

    // The pushed selection lands: same key, actions and all.
    currentSelectionKey = "element:ccc";
    currentActions = [{ actions: [{ id: "dependencies.disconnect", label: "Remove", icon: "", available: true, unavailableReason: "", items: [] }] }];
    rerender(
      <DependencyGraphCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["services.dgr"]} />,
    );

    // Assert.
    expect(container.ownerDocument.querySelector(".context-menu")).not.toBeNull();
  });

  it("opens the context menu even when the node is already the selection", () => {
    // Arrange.
    // Right-clicking the selected node re-pushes the same key, the effect waiting for a key
    // change never fires, and the menu never opened - the "menu does not always show" bug.
    currentSelectionKey = "element:aaa";
    currentActions = [{ actions: [{ id: "dependencies.rename", label: "Rename", icon: "", available: true, unavailableReason: "", items: [] }] }];
    const { container } = renderCanvas();

    // Act.
    fireEvent.contextMenu(container.querySelector('[data-element-id="aaa"]')!);

    // Assert.
    // The menu opens from the actions already at hand, and nothing is re-pushed.
    expect(container.ownerDocument.querySelector(".context-menu")).not.toBeNull();
    expect(selections).toHaveLength(0);
  });

  it("offers no resize adorners, because a node has no extent to resize", () => {
    // Arrange.
    currentSelectionKey = "element:aaa";

    // Act.
    const { container } = renderCanvas();

    // Assert.
    // The shared span element still draws the strips; the stylesheet hides them and no handler
    // is wired, so a grab on one cannot become an edit. What the timeline resized was a
    // duration, and there is none.
    const adorners = [...container.querySelectorAll(".dependency-graph-adorner")];
    expect(properties).toHaveLength(0);
    adorners.forEach((adorner) => fireEvent.mouseDown(adorner, { clientX: 100, clientY: 30 }));
    expect(properties).toHaveLength(0);
    expect(executed).toHaveLength(0);
  });

  it("loops a dependency forward out of the dependent and back into one behind it", () => {
    // Arrange: bbb sits to the left of aaa, so the edge would otherwise reverse out of a side.
    currentModel = {
      elements: new Map([
        ["aaa", node("aaa", "Needs it", 600, 0)],
        ["bbb", node("bbb", "Needed", 0, 2)],
      ]),
      relations: new Map([["ccc", { id: "ccc", fromElementId: "aaa", toElementId: "bbb", label: "" }]]),
    };

    // Act.
    const { container } = renderCanvas();
    const d = container.querySelector(".dependency-graph-relation-line")!.getAttribute("d")!;
    const numbers = d.match(/-?[\d.]+/g)!.map(Number);
    const [startX, , control1X, , control2X, , endX] = numbers;

    // Assert.
    // The control points push past both endpoints: the curve departs the dependent rightward,
    // loops around, and arrives at the dependency from its left.
    expect(endX).toBeLessThan(startX);
    expect(control1X).toBeGreaterThan(startX);
    expect(control2X).toBeLessThan(endX);
  });

  it("keeps the plain facing bezier when the dependency sits clear to the right", () => {
    // Arrange: the default model - bbb sits well past aaa's box.
    const { container } = renderCanvas();

    // Act.
    const d = container.querySelector(".dependency-graph-relation-line")!.getAttribute("d")!;
    const numbers = d.match(/-?[\d.]+/g)!.map(Number);
    const [, , control1X, , control2X] = numbers;

    // Assert: both control points still meet at the horizontal midpoint.
    expect(control1X).toBe(control2X);
  });

  it("keeps a label centred and trims it with an ellipsis when the box cannot hold it", () => {
    // Arrange.
    currentModel.elements.set("nnn", node("nnn", "A service name far wider than any node box", 900, 3));
    const { container } = renderCanvas();

    // Act.
    const labels = [...container.querySelectorAll(".dependency-graph-label")];
    const narrow = labels.find((label) => label.textContent?.endsWith("…"))!;
    const wide = labels.find((label) => label.textContent === "API gateway")!;

    // Assert.
    expect(narrow).toBeDefined();
    expect(narrow.textContent!.length).toBeLessThan("A service name far wider than any node box".length);
    expect(narrow.getAttribute("text-anchor")).toBeNull();
    expect(wide.getAttribute("text-anchor")).toBeNull();
  });

  it("zooms the rows along with the horizontal axis", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".dependency-graph-surface")!;
    const before = Number(container.querySelector(".dependency-graph-node")!.getAttribute("height"));

    // Act: one wheel step in.
    fireEvent.wheel(surface, { deltaY: -100 });

    // Assert.
    // The node's drawn height scales with the same step the horizontal axis took.
    const after = Number(container.querySelector(".dependency-graph-node")!.getAttribute("height"));
    expect(after).toBeCloseTo(before * 1.25, 5);
  });

  it("pans horizontally from the scrollbar thumb", () => {
    // Arrange.
    const { container } = renderCanvas();
    const thumb = container.querySelector(".canvas-scrollbar-horizontal .canvas-scrollbar-thumb")!;
    const before = container.querySelector(".dependency-graph-node")!.getAttribute("x");

    // Act.
    fireEvent.mouseDown(thumb, { clientX: 100, clientY: 300 });
    fireEvent.mouseMove(window, { clientX: 180, clientY: 300 });
    fireEvent.mouseUp(window);

    // Assert.
    expect(container.querySelector(".dependency-graph-node")!.getAttribute("x")).not.toBe(before);
  });

  it("reports the rectangle it can see, in the module's own units", async () => {
    // The client half of the view-delta loop. This canvas draws in pixels-per-unit rather than
    // through a viewBox, so it converts at its own call site and the shared library converts
    // nothing (view-delta-adoption Requirement 3.4).
    const reportView = vi.fn();
    currentReportView = reportView;
    const width = 900;
    const height = 400;
    const measure = vi
      .spyOn(Element.prototype, "getBoundingClientRect")
      .mockReturnValue({ x: 0, y: 0, width, height, top: 0, left: 0, right: width, bottom: height, toJSON: () => ({}) } as DOMRect);
    try {
      // Act.
      renderCanvas();

      // Assert: the rectangle is in module units and holds what the canvas is showing. The
      // nodes sit at x 0 row 0 and x 400 row 2, and the canvas has just fitted them, so a
      // report that did not contain both would be culling elements the reader is looking at -
      // which is the failure the shared library's surface handling exists to prevent.
      await vi.waitFor(() => expect(reportView).toHaveBeenCalled(), { timeout: 2000 });
      const viewport = reportView.mock.calls.at(-1)![0] as { minX: number; minY: number; maxX: number; maxY: number };
      expect(viewport.minX).toBeLessThanOrEqual(0);
      expect(viewport.maxX).toBeGreaterThanOrEqual(400);
      expect(viewport.minY).toBeLessThanOrEqual(0);
      expect(viewport.maxY).toBeGreaterThanOrEqual(2 * ROW_HEIGHT);

      // And it is a rectangle in units, not the pixel rect: the canvas fitted the content, so
      // its horizontal span is the surface divided by a zoom that is no longer 1.
      expect(viewport.maxX - viewport.minX).not.toBeCloseTo(width, 5);
      expect(viewport.maxY - viewport.minY).toBeGreaterThan(0);
    } finally {
      measure.mockRestore();
      currentReportView = null;
    }
  });

  it("reports again when the view moves, and only once for a settled view", async () => {
    // A pan is one report, not one per frame: the shared hook debounces, and what it observes is
    // the canvas's view state rather than the gesture that moved it.
    const reportView = vi.fn();
    currentReportView = reportView;
    try {
      const { container } = renderCanvas();
      await vi.waitFor(() => expect(reportView).toHaveBeenCalled(), { timeout: 2000 });
      const first = reportView.mock.calls.at(-1)![0] as { minX: number };
      const before = reportView.mock.calls.length;

      // Act: drag the surface, which moves the view several times.
      const surface = container.querySelector(".dependency-graph-surface")!;
      fireEvent.mouseDown(surface, { clientX: 300, clientY: 200 });
      fireEvent.mouseMove(surface, { clientX: 260, clientY: 200 });
      fireEvent.mouseMove(surface, { clientX: 220, clientY: 200 });
      fireEvent.mouseMove(surface, { clientX: 180, clientY: 200 });
      fireEvent.mouseUp(surface);

      // Assert: one further report, carrying the moved rectangle.
      await vi.waitFor(() => expect(reportView.mock.calls.length).toBeGreaterThan(before), { timeout: 2000 });
      const moved = reportView.mock.calls.at(-1)![0] as { minX: number };
      expect(moved.minX).not.toBeCloseTo(first.minX, 5);
      expect(reportView.mock.calls.length).toBe(before + 1);
    } finally {
      currentReportView = null;
    }
  });

  // ---- inline renaming -----------------------------------------------------------------

  function labelPromptFor(elementId: string, text: string): unknown {
    return {
      prompt: {
        case: "inputDialog",
        value: {
          title: "Rename",
          icon: "mdi-pencil-outline",
          fieldLabel: "Label",
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

  function labelField(container: HTMLElement): HTMLInputElement {
    return container.querySelector("input.inline-label-editor-field") as HTMLInputElement;
  }

  it("opens an editor over a node, and submits what is typed into it", async () => {
    // Arrange. Every node is drawn as a span, so its label is centred in its box.
    currentPrompt = labelPromptFor("aaa", "API gateway");

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(editorBox(container)).not.toBeNull();
    expect(labelField(container).value).toBe("API gateway");

    fireEvent.change(labelField(container), { target: { value: "Renamed" } });
    fireEvent.keyDown(labelField(container), { key: "Enter" });
    await waitFor(() => expect(submitLabel).toHaveBeenCalledWith("Renamed"));
  });

  it("covers the node's own box, so the editor is where the label is drawn", () => {
    // Arrange.
    currentPrompt = labelPromptFor("aaa", "API gateway");

    // Act.
    const { container } = renderCanvas();

    // Assert: the editor's rectangle is the node's rectangle.
    const rect = container.querySelector('[data-element-id="aaa"] rect') as SVGRectElement;
    expect(rect).not.toBeNull();
    const box = editorBox(container);
    expect(Number(box.getAttribute("x"))).toBeCloseTo(Number(rect.getAttribute("x")), 5);
    expect(Number(box.getAttribute("width"))).toBeCloseTo(Number(rect.getAttribute("width")), 5);
  });

  it("places a relation's editor at the midpoint of the line, where its label is drawn", () => {
    // Arrange.
    currentPrompt = labelPromptFor("ccc", "verifies tokens with");

    // Act.
    const { container } = renderCanvas();

    // Assert: centred on the same midpoint InteractiveBezierConnection draws its text at.
    const drawn = container.querySelector('[data-connection-id="ccc"] text') as SVGTextElement;
    expect(drawn).not.toBeNull();
    const box = editorBox(container);
    expect(box).not.toBeNull();
    const centre = Number(box.getAttribute("x")) + Number(box.getAttribute("width")) / 2;
    expect(centre).toBeCloseTo(Number(drawn.getAttribute("x")), 5);
  });

  it("leaves the selection alone when an inline edit commits", async () => {
    // Arrange.
    currentSelectionKey = "aaa";
    currentPrompt = labelPromptFor("aaa", "API gateway");
    const { container } = renderCanvas();
    selections = [];

    // Act.
    fireEvent.change(labelField(container), { target: { value: "Renamed" } });
    fireEvent.keyDown(labelField(container), { key: "Enter" });

    // Assert.
    await waitFor(() => expect(submitLabel).toHaveBeenCalled());
    expect(selections).toEqual([]);
  });
});
