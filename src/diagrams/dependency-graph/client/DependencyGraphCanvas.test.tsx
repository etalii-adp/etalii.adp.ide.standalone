import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render, waitFor } from "@testing-library/react";
import { emptyModel, type DependencyGraphModel } from "./dependencyGraphModel";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";
import { fakeContextConnection, idsPushed, pointer } from "@client/canvas/library/testing/canvasHarness";

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
  useContextConnection: () => connection,
}));

const connection = fakeContextConnection({
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
});

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
    expect(container.querySelector("marker#library-arrow path")!.classList.contains("canvas-arrowhead")).toBe(true);
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
    // the thing depended upon. The marker is the library's shared arrow now, attached through
    // the relation type's endMarker style, so this is where its absence would show.
    const marker = container.querySelector("marker#library-arrow");
    expect(marker).not.toBeNull();
    expect(marker!.getAttribute("orient")).toBe("auto-start-reverse");
    const line = container.querySelector(".dependency-graph-relation-line")!;
    expect(line.getAttribute("marker-end")).toContain("library-arrow");
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

  it("leaves the unavailable state to the library's frame rather than saying it itself", () => {
    // Arrange: client-centralization Requirement 2.3 - one appearance, drawn by the library.
    currentFailed = true;

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.textContent).not.toContain("could not be opened");
  });

  it("treats a motionless press as a selection, never an edit", () => {
    // Arrange.
    const { container } = renderCanvas();
    const element = container.querySelector(".dependency-graph-element")!;

    // Act.
    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(element, pointer("pointerup", { clientX: 100, clientY: 100 }));

    // Assert.
    expect(moves).toHaveLength(0);
    expect(selections).toHaveLength(1);
  });

  it("commits a drag as one move in module coordinates, snapped to a row", () => {
    // Arrange.
    const { container } = renderCanvas();
    const element = container.querySelector(".dependency-graph-element")!;

    // Act: right by 50px, down by most of a row - close enough to snap to row 1.
    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(element, pointer("pointermove", { clientX: 150, clientY: 100 + ROW_HEIGHT - 10 }));
    fireEvent(element, pointer("pointerup", { clientX: 150, clientY: 100 + ROW_HEIGHT - 10 }));

    // Assert.
    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe("aaa");
    // y arrives as row × height - a whole row, never a pointer-precise position.
    expect(moves[0].y % ROW_HEIGHT).toBe(0);
    expect(moves[0].y / ROW_HEIGHT).toBe(1);
  });

  it("shows during a drag exactly where the node comes to rest once the backend confirms it", () => {
    // THE USER'S REPORT: the snap seen while dragging was not the snap on the drop. The library
    // snapped the node's CENTRE to whole rows, while this canvas draws a row's node with its TOP
    // on the row - so a dragged node rested half a node above its row and dropped half a node
    // when the confirmation arrived. What is asserted is the property itself, not a rule: the
    // box drawn mid-drag is the box drawn after the model says what the drop sent.
    const { container, rerender } = renderCanvas();
    const element = container.querySelector('[data-element-id="aaa"]')!;
    const boxOf = () => {
      const box = container.querySelector('[data-element-id="aaa"] .library-shape')!;
      return { x: Number(box.getAttribute("x")), y: Number(box.getAttribute("y")) };
    };

    // Down by a row and a bit, across by an odd amount: a snap on y, none on x.
    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(element, pointer("pointermove", { clientX: 137.5, clientY: 100 + ROW_HEIGHT + 7 }));
    const during = boxOf();
    fireEvent(element, pointer("pointerup", { clientX: 137.5, clientY: 100 + ROW_HEIGHT + 7 }));

    expect(moves, "the drag never dropped").toHaveLength(1);
    const sent = moves[0];
    const before = currentModel.elements.get("aaa")!;
    currentModel = { ...currentModel, elements: new Map([...currentModel.elements, ["aaa", { ...before, x: sent.x, y: sent.y, row: sent.y / ROW_HEIGHT }]]) };
    rerender(<DependencyGraphCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["services.dgr"]} />);
    const after = boxOf();

    expect(sent.y, "the drop did not change row, so there was no snap to compare").toBe(ROW_HEIGHT);
    expect(after.x).toBeCloseTo(during.x, 6);
    expect(after.y, `drawn at ${during.y} while dragging, ${after.y} once confirmed`).toBeCloseTo(during.y, 6);
  });

  it("leaves the horizontal free rather than snapping it", () => {
    // Arrange.
    // The asymmetry is the placement model: rows are a grid, x is not. Rounding x here would
    // quietly turn the canvas into one.
    const { container } = renderCanvas();
    const element = container.querySelector(".dependency-graph-element")!;

    // The zoom the canvas fitted itself to, read back off a node's drawn box rather than
    // assumed: a box is NODE_WIDTH canvas units wide, so its pixel width is the scale.
    const pixelsPerUnit = Number(container.querySelector(".dependency-graph-node")!.getAttribute("width")) / 160;

    // Act: an odd, fractional distance across.
    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(element, pointer("pointermove", { clientX: 137.5, clientY: 100 }));
    fireEvent(element, pointer("pointerup", { clientX: 137.5, clientY: 100 }));

    // Assert.
    // The coordinate that travels is the pointer's, converted and otherwise untouched - not
    // rounded, and not snapped to any step.
    expect(moves).toHaveLength(1);
    expect(moves[0].x).toBeCloseTo(37.5 / pixelsPerUnit, 10);
  });

  it("abandons a drag on Escape with nothing dispatched", () => {
    // Arrange.
    const { container } = renderCanvas();
    const element = container.querySelector(".dependency-graph-element")!;

    // Act.
    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(element, pointer("pointermove", { clientX: 300, clientY: 300 }));
    fireEvent.keyDown(container.querySelector("svg.library-canvas-surface")!, { key: "Escape" });
    fireEvent(element, pointer("pointerup", { clientX: 300, clientY: 300 }));

    // Assert.
    expect(moves).toHaveLength(0);
  });

  it("previews a drag through the library, with the row decided at release", () => {
    // Arrange. The old canvas drew its own "x · row N" hint beside the dragged node; that
    // machinery retired with the migration (a recorded loss), and the landing row is decided
    // in the release conversion - pinned by the snap test above. What remains observable
    // mid-drag is the library's preview riding the element.
    const { container } = renderCanvas();
    const element = container.querySelector('[data-element-id="aaa"]')!;

    // Act.
    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(element, pointer("pointermove", { clientX: 200, clientY: 100 + ROW_HEIGHT }));

    // Assert.
    expect(element.classList.contains("library-element-dragging")).toBe(true);
    expect(container.querySelector(".dependency-graph-hint")).toBeNull();

    fireEvent(element, pointer("pointerup", { clientX: 200, clientY: 100 + ROW_HEIGHT }));
  });

  it("relates the dependent to the dependency in one stateless call, in the gesture's direction", () => {
    // Arrange: the library renders the named anchors always, shown by the stylesheet when
    // they matter.
    const { container } = renderCanvas();
    const rightAnchor = container.querySelector('[data-element-id="aaa"] [data-anchor="right"]')!;

    // Act: drag from aaa's right anchor and release over bbb's centre - jsdom's zero-size
    // rect makes one pixel one canvas unit.
    const overBbb = { clientX: 400 + 80, clientY: 2 * ROW_HEIGHT + 18 };
    fireEvent(rightAnchor, pointer("pointerdown", { button: 0, clientX: 160, clientY: 18 }));
    fireEvent(rightAnchor, pointer("pointermove", { ...overBbb }));
    fireEvent(rightAnchor, pointer("pointerup", { ...overBbb }));

    // Assert.
    // One call carries the whole gesture, and aaa depends on bbb - the direction the drag had.
    const calls = executed.filter((call) => call.actionId === "dependencies.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toBe("rel:aaa->bbb");
  });

  it("reverses the dependency when the drag lifts from the left anchor", () => {
    // Arrange.
    const { container } = renderCanvas();
    const leftAnchor = container.querySelector('[data-element-id="aaa"] [data-anchor="left"]')!;

    // Act.
    const overBbb = { clientX: 400 + 80, clientY: 2 * ROW_HEIGHT + 18 };
    fireEvent(leftAnchor, pointer("pointerdown", { button: 0, clientX: 0, clientY: 18 }));
    fireEvent(leftAnchor, pointer("pointermove", { ...overBbb }));
    fireEvent(leftAnchor, pointer("pointerup", { ...overBbb }));

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
    const elements = container.querySelectorAll(".dependency-graph-element");

    // Act: press a node body and release - a selection, not a gesture.
    fireEvent(elements[0], pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(elements[0], pointer("pointerup", { clientX: 100, clientY: 100 }));

    // Assert.
    expect(executed.filter((call) => call.actionId === "dependencies.connect")).toHaveLength(0);
  });

  it("completes a dependency onto empty space as a create-and-relate placement", () => {
    // Arrange.
    const { container } = renderCanvas();
    const rightAnchor = container.querySelector('[data-element-id="aaa"] [data-anchor="right"]')!;

    // Act: drag from aaa's right anchor and release over nothing.
    fireEvent(rightAnchor, pointer("pointerdown", { button: 0, clientX: 160, clientY: 18 }));
    fireEvent(rightAnchor, pointer("pointermove", { clientX: 900, clientY: 400 }));
    fireEvent(rightAnchor, pointer("pointerup", { clientX: 900, clientY: 400 }));

    // Assert.
    // The landing is a placement inside the same rel: gesture - one call, one undo.
    const calls = executed.filter((call) => call.actionId === "dependencies.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^rel:aaa->new:/);
  });

  it("a left-anchor drag onto empty space puts the placement at the dependent end", () => {
    // Arrange.
    const { container } = renderCanvas();
    const leftAnchor = container.querySelector('[data-element-id="aaa"] [data-anchor="left"]')!;

    // Act.
    fireEvent(leftAnchor, pointer("pointerdown", { button: 0, clientX: 0, clientY: 18 }));
    fireEvent(leftAnchor, pointer("pointermove", { clientX: 900, clientY: 400 }));
    fireEvent(leftAnchor, pointer("pointerup", { clientX: 900, clientY: 400 }));

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
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const box = surface.getAttribute("viewBox")!.split(" ").map(Number);
    Object.defineProperty(surface, "getBoundingClientRect", {
      value: () => ({ x: 0, y: 0, top: 0, left: 0, right: box[2], bottom: box[3], width: box[2], height: box[3], toJSON: () => ({}) }),
    });

    // Act.
    const event = new MouseEvent("drop", { bubbles: true, cancelable: true, clientX: 300, clientY: 200 });
    Object.defineProperty(event, "dataTransfer", {
      value: { getData: (type: string) => (type === "application/x-adp-toolbox-item" ? "dependencies.add-element" : ""), types: ["application/x-adp-toolbox-item"] },
    });
    fireEvent(surface, event);

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

  it("pans the view from a background drag", () => {
    // Arrange. The library draws through a viewBox, so panning moves the viewBox rather than
    // every node's own x - and surface gestures are the library's now (the right-button pan
    // the old canvas offered retired with it, a recorded unification).
    const { container } = renderCanvas();
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const before = surface.getAttribute("viewBox");

    // Act.
    fireEvent(surface, pointer("pointerdown", { button: 0, clientX: 400, clientY: 300 }));
    fireEvent(surface, pointer("pointermove", { clientX: 320, clientY: 300 }));
    fireEvent(surface, pointer("pointerup", { clientX: 320, clientY: 300 }));

    // Assert.
    expect(surface.getAttribute("viewBox")).not.toBe(before);
  });

  it("clears the selection on a background press", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector("svg.library-canvas-surface")!;

    // Act: press and release, unmoved, on empty canvas.
    fireEvent(surface, pointer("pointerdown", { button: 0, clientX: 700, clientY: 300 }));
    fireEvent(surface, pointer("pointerup", { clientX: 700, clientY: 300 }));

    // Assert: the module was asked to select nothing.
    expect(selections).toEqual([null]);
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

  it("selects a dependency on a press, through its fat hit path", () => {
    // Arrange.
    const { container } = renderCanvas();
    const hit = container.querySelector(".dependency-graph-relation-hit")!;

    // Act.
    fireEvent(hit, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(hit, pointer("pointerup", { clientX: 10, clientY: 10 }));

    // Assert.
    // The press selects the dependency by its id - the same channel a node selection uses, and
    // the resolver answers for both - decided at the gesture's end, never by a click event.
    expect(selections).toHaveLength(1);
    const child = (selections[0] as { detail: { value: { id: { source: { value: { value: string } } } } } }).detail.value;
    expect(child.id.source.value.value).toBe("ccc");
  });

  it("a node drag whose trailing click lands on a dependency does not steal the selection", () => {
    // Arrange: the reported defect. Dragging a node and releasing over a dependency's line
    // makes the browser's trailing click land on the dependency's fat hit path - and a raw
    // click handler there steals a selection the gesture never meant to change.
    const { container } = renderCanvas();
    const element = container.querySelector(".dependency-graph-element")!;
    const hit = container.querySelector(".dependency-graph-relation-hit")!;

    // Act: a real drag of the node, then the trailing click as the browser delivers it -
    // targeting whatever now sits under the release point, here the dependency.
    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(element, pointer("pointermove", { clientX: 150, clientY: 120 }));
    fireEvent(element, pointer("pointerup", { clientX: 150, clientY: 120 }));
    fireEvent.click(hit);

    // Assert: the drag moved its node, and neither it nor its trailing click selected anything.
    expect(moves).toHaveLength(1);
    expect(selections).toHaveLength(0);
  });

  it("marks the selected dependency, so the selection is visible", () => {
    // Arrange.
    currentSelectionKey = "element:ccc";

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".dependency-graph-relation")!.classList.contains("canvas-selected")).toBe(true);
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
    // Arrange. The library zooms both axes through one viewBox, which is exactly the coupled
    // behaviour the old canvas built by hand (its separate vertical clamp retired with it).
    const { container } = renderCanvas();
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const before = surface.getAttribute("viewBox")!.split(" ").map(Number);

    // Act: one wheel step in.
    fireEvent.wheel(surface, { deltaY: -100 });

    // Assert: both spans shrank by the same step.
    const after = surface.getAttribute("viewBox")!.split(" ").map(Number);
    expect(after[2]).toBeCloseTo(before[2] / 1.25, 5);
    expect(after[3]).toBeCloseTo(before[3] / 1.25, 5);
  });

  it("pans horizontally from the scrollbar thumb", () => {
    // Arrange.
    const { container } = renderCanvas();
    const horizontalBar = container.querySelector(".canvas-scrollbar-horizontal")!;
    Object.defineProperty(horizontalBar, "getBoundingClientRect", {
      value: () => ({ x: 0, y: 0, top: 0, left: 0, right: 200, bottom: 10, width: 200, height: 10, toJSON: () => ({}) }),
    });
    const thumb = horizontalBar.querySelector(".canvas-scrollbar-thumb")!;
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const before = surface.getAttribute("viewBox");

    // Act.
    fireEvent.mouseDown(thumb, { clientX: 100, clientY: 300 });
    fireEvent.mouseMove(window, { clientX: 180, clientY: 300 });
    fireEvent.mouseUp(window);

    // Assert.
    expect(surface.getAttribute("viewBox")).not.toBe(before);
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

      // Act: drag the background, which moves the view several times.
      const surface = container.querySelector("svg.library-canvas-surface")!;
      fireEvent(surface, pointer("pointerdown", { button: 0, clientX: 300, clientY: 200 }));
      fireEvent(surface, pointer("pointermove", { clientX: 260, clientY: 200 }));
      fireEvent(surface, pointer("pointermove", { clientX: 220, clientY: 200 }));
      fireEvent(surface, pointer("pointermove", { clientX: 180, clientY: 200 }));
      fireEvent(surface, pointer("pointerup", { clientX: 180, clientY: 200 }));

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

describe("selection, as every canvas has it", () => {
  it("highlights a pushed element and relation, and clears on a background press (centralized-selection 9.2)", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentSelectionKey = id === null ? null : `element:${id}`;
        return renderCanvas();
      },
      pushedIds: () => idsPushed(selections),
      element: "aaa",
      connection: "ccc",
    });
  });
});
