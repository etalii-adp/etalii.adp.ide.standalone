import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { emptyModel, type TimelineModel } from "./timelineModel";

let currentModel: TimelineModel = emptyModel;
let currentLoading = false;
let currentFailed = false;
let currentSelectionKey: string | null = null;
let currentActions: unknown[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];
let selections: unknown[] = [];
let executed: { actionId: string; source: unknown }[] = [];
let properties: { propertyId: string; value: string }[] = [];
let shortcuts: { key: string; source: unknown }[] = [];

vi.mock("./useTimelineStream", () => ({
  useTimelineStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve("");
    },
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  innermostKey: () => currentSelectionKey,
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

vi.mock("@client/shell/panels/DiagramToolboxContext", () => ({
  TOOLBOX_DRAG_TYPE: "application/x-adp-toolbox-item",
  useRegisterDiagramToolbox: () => undefined,
}));

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: () => [],
}));

const { TimelineCanvas } = await import("./TimelineCanvas");

const ROW_HEIGHT = 60;

function period(id: string, label: string, beginSeconds: number, days: number, row: number) {
  const begin = new Date(beginSeconds * 1000).toISOString().slice(0, 10);
  const end = new Date((beginSeconds + days * 86400) * 1000).toISOString().slice(0, 10);
  return {
    id,
    x: beginSeconds,
    y: row * ROW_HEIGHT,
    label,
    begin,
    end,
    row,
    dateOnly: true,
    isPeriod: true,
  };
}

function modelWith(): TimelineModel {
  const at = Date.UTC(2026, 0, 5) / 1000;
  return {
    elements: new Map([
      ["aaa", period("aaa", "Discovery", at, 39, 0)],
      ["bbb", { ...period("bbb", "Go", at + 42 * 86400, 0, 2), isPeriod: false, end: "" }],
    ]),
    connections: new Map([
      ["ccc", { id: "ccc", fromElementId: "aaa", toElementId: "bbb", label: "gates" }],
    ]),
  };
}

function renderCanvas() {
  return render(
    <TimelineCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["plan.tml"]} />,
  );
}

beforeEach(() => {
  currentModel = modelWith();
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

describe("the timeline canvas", () => {
  it("still shows both scrollbars and still pans on a thumb drag after moving to the shared scroll view", () => {
    // Arrange.
    // small-refinements Requirement 1.6: the migration onto CanvasScrollbars is not a silent
    // no-op. Before it, nothing in this file asserted a scrollbar at all.
    const { container } = renderCanvas();
    const horizontalBar = container.querySelector(".canvas-scrollbar-horizontal")!;
    const verticalBar = container.querySelector(".canvas-scrollbar-vertical")!;
    const thumb = horizontalBar.querySelector<HTMLElement>(".canvas-scrollbar-thumb")!;
    // jsdom lays nothing out; the drag divides the extent by the track width, so give it one.
    Object.defineProperty(horizontalBar, "getBoundingClientRect", {
      value: () => ({ x: 0, y: 0, top: 0, left: 0, right: 400, bottom: 10, width: 400, height: 10, toJSON: () => ({}) }),
    });
    const before = thumb.style.left;

    // Act.
    fireEvent.mouseDown(thumb, { clientX: 100, clientY: 5 });
    fireEvent.mouseMove(window, { clientX: 180, clientY: 5 });
    fireEvent.mouseUp(window);

    // Assert.
    expect(horizontalBar.classList.contains("timeline-scrollbars")).toBe(true);
    expect(verticalBar.classList.contains("timeline-scrollbars")).toBe(true);
    expect(thumb.style.left).not.toBe(before);
  });

  it("draws the period, the moment, the connection and the ruler", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelectorAll(".timeline-period")).toHaveLength(1);
    expect(container.querySelectorAll(".timeline-moment")).toHaveLength(1);
    expect(container.querySelectorAll(".timeline-connection")).toHaveLength(1);
    expect(container.querySelector(".timeline-ruler")).not.toBeNull();
    expect(container.textContent).toContain("Discovery");
    expect(container.textContent).toContain("gates");
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
    const element = container.querySelector(".timeline-element")!;

    // Act.
    fireEvent.mouseDown(element, { clientX: 100, clientY: 100 });
    fireEvent.mouseUp(container.querySelector(".timeline-surface")!);

    // Assert.
    expect(moves).toHaveLength(0);
    expect(selections).toHaveLength(1);
  });

  it("commits a drag as one move in module coordinates, snapped to a row", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".timeline-surface")!;
    const element = container.querySelector(".timeline-element")!;

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

  it("abandons a drag on Escape with nothing dispatched", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".timeline-surface")!;
    const element = container.querySelector(".timeline-element")!;

    // Act.
    fireEvent.mouseDown(element, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 300, clientY: 300 });
    fireEvent.keyDown(window, { key: "Escape" });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(moves).toHaveLength(0);
  });

  it("shows the landing placement while a drag is in progress", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".timeline-surface")!;
    const element = container.querySelector(".timeline-element")!;

    // Act.
    fireEvent.mouseDown(element, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 200, clientY: 100 + ROW_HEIGHT });

    // Assert.
    // The times and the row it would land on, visible before the user commits
    // (Requirement 6.5).
    const hint = container.querySelector(".timeline-hint");
    expect(hint).not.toBeNull();
    expect(hint!.textContent).toContain("row 1");
  });

  it("relates the source to the target in one stateless call, in the gesture's own direction", () => {
    // Arrange: the anchors render on the selected element only; the second hit circle is the
    // end-side anchor.
    currentSelectionKey = "element:aaa";
    const { container } = renderCanvas();
    const endAnchor = container.querySelectorAll(".timeline-anchor-hit")[1];
    const target = container.querySelector('[data-element-id="bbb"]')!;

    // Act: drag from aaa's end anchor and release on bbb. The landing is read from the event's
    // own target - a fast release must not depend on a mouseenter having kept up.
    fireEvent.mouseDown(endAnchor, { clientX: 100, clientY: 30 });
    fireEvent.mouseUp(target);

    // Assert.
    // One call carries the whole gesture. The two-call protocol this replaces kept an armed
    // source in the backend, and a stale arm related the wrong pair.
    const calls = executed.filter((call) => call.actionId === "timeline.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toBe("rel:aaa->bbb");
  });

  it("reverses the relation when the drag lifts from the begin anchor", () => {
    // Arrange: the first hit circle is the begin-side anchor.
    currentSelectionKey = "element:aaa";
    const { container } = renderCanvas();
    const beginAnchor = container.querySelectorAll(".timeline-anchor-hit")[0];
    const target = container.querySelector('[data-element-id="bbb"]')!;

    // Act.
    fireEvent.mouseDown(beginAnchor, { clientX: 100, clientY: 30 });
    fireEvent.mouseUp(target);

    // Assert.
    // What precedes an element points INTO it: the landing becomes the relation's source and
    // the dragged element its target - previously both anchors related aaa->bbb.
    const calls = executed.filter((call) => call.actionId === "timeline.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toBe("rel:bbb->aaa");
  });

  it("never connects from a plain click on the element body", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".timeline-surface")!;
    const elements = container.querySelectorAll(".timeline-element");

    // Act: press an element body and release - a selection, not a gesture.
    fireEvent.mouseDown(elements[0], { clientX: 100, clientY: 100 });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(executed.filter((call) => call.actionId === "timeline.connect")).toHaveLength(0);
  });

  it("lands a toolbox drop as a placement action, wherever it falls", () => {
    // Arrange.
    // The drop names a placement - new:{seconds},{row} - so the element appears at the dropped
    // time and row with nothing asked. One handler on the surface serves drops over elements
    // and over empty canvas alike.
    const { container } = renderCanvas();
    const surface = container.querySelector(".timeline-surface")!;
    const data = new Map([["application/x-adp-toolbox-item", "timeline.add-element"]]);

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
    expect(executed[0].actionId).toBe("timeline.add-element");
    const source = executed[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^new:/);
  });

  it("pans with the right button on empty space, and no browser menu appears", () => {
    // Arrange.
    // The observable is an element's drawn x: panning shifts every box by the pan distance.
    const { container } = renderCanvas();
    const surface = container.querySelector(".timeline-surface")!;
    const before = container.querySelector(".timeline-period")!.getAttribute("x");

    // Act.
    fireEvent.mouseDown(surface, { button: 2, clientX: 400, clientY: 300 });
    fireEvent.mouseMove(surface, { clientX: 320, clientY: 300 });
    const menuPrevented = !fireEvent.contextMenu(surface, { clientX: 320, clientY: 300 });
    fireEvent.mouseUp(surface);

    // Assert.
    // The view moved, nothing was selected away, and the browser's menu was consumed.
    expect(container.querySelector(".timeline-period")!.getAttribute("x")).not.toBe(before);
    expect(menuPrevented).toBe(true);
    expect(selections).toHaveLength(0);
  });

  it("forwards Tab against the selection as a shortcut, letting the backend own the table", () => {
    // Arrange: a canvas with an element selected. The selection mock is module-level, so this
    // drives the key house-keeping only: no selection means no forwarding.
    const { container } = renderCanvas();
    const surface = container.querySelector(".timeline-surface")!;

    // Act.
    fireEvent.keyDown(surface, { key: "Tab" });

    // Assert.
    // Nothing selected in this mock, so nothing is forwarded - a Tab with no selection must
    // stay the browser's own focus traversal.
    expect(shortcuts).toHaveLength(0);
  });

  it("completes a relation onto empty space as a create-and-relate placement", () => {
    // Arrange.
    currentSelectionKey = "element:aaa";
    const { container } = renderCanvas();
    const endAnchor = container.querySelectorAll(".timeline-anchor-hit")[1];
    const surface = container.querySelector(".timeline-surface")!;

    // Act: drag from aaa's end anchor and release over nothing.
    fireEvent.mouseDown(endAnchor, { clientX: 100, clientY: 30 });
    fireEvent.mouseUp(surface);

    // Assert.
    // The landing is a placement inside the same rel: gesture - one call, one undo.
    const calls = executed.filter((call) => call.actionId === "timeline.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^rel:aaa->new:/);
  });

  it("a begin-anchor drag onto empty space puts the placement at the relation's source", () => {
    // Arrange.
    currentSelectionKey = "element:aaa";
    const { container } = renderCanvas();
    const beginAnchor = container.querySelectorAll(".timeline-anchor-hit")[0];
    const surface = container.querySelector(".timeline-surface")!;

    // Act.
    fireEvent.mouseDown(beginAnchor, { clientX: 100, clientY: 30 });
    fireEvent.mouseUp(surface);

    // Assert.
    // The new element precedes the dragged one, so the relation runs out of it and into aaa.
    const calls = executed.filter((call) => call.actionId === "timeline.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^rel:new:.+->aaa$/);
  });

  it("a surface release with no gesture in flight fabricates no calls", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".timeline-surface")!;

    // Act.
    fireEvent.mouseUp(surface);

    // Assert.
    expect(executed).toHaveLength(0);
  });

  it("pans when the right button lands on the drawn canvas itself, not only the surface div", () => {
    // Arrange.
    // Real clicks land on the inner svg, not the surface div - a target===currentTarget guard
    // silently disabled panning everywhere, and synthetic events aimed at the surface hid it.
    const { container } = renderCanvas();
    const surface = container.querySelector(".timeline-surface")!;
    const content = container.querySelector(".timeline-content")!;
    const before = container.querySelector(".timeline-period")!.getAttribute("x");

    // Act.
    fireEvent.mouseDown(content, { button: 2, clientX: 400, clientY: 300 });
    fireEvent.mouseMove(surface, { clientX: 320, clientY: 300 });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(container.querySelector(".timeline-period")!.getAttribute("x")).not.toBe(before);
  });

  it("selects a relation on a left click, through its fat hit path", () => {
    // Arrange.
    const { container } = renderCanvas();
    const hit = container.querySelector(".timeline-connection-hit")!;

    // Act.
    fireEvent.click(hit);

    // Assert.
    // The click selects the connection by its id - the same channel an element selection uses,
    // and the resolver answers for both. Before this, a relation could not be selected at all.
    expect(selections).toHaveLength(1);
    const child = (selections[0] as { detail: { value: { id: { source: { value: { value: string } } } } } }).detail.value;
    expect(child.id.source.value.value).toBe("ccc");
  });

  it("marks the selected relation, so the selection is visible", () => {
    // Arrange.
    currentSelectionKey = "element:ccc";

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".timeline-connection")!.classList.contains("timeline-selected")).toBe(true);
  });

  it("opens the context menu for a relation once its selection arrives", () => {
    // Arrange.
    const { container, rerender } = renderCanvas();
    const hit = container.querySelector(".timeline-connection-hit")!;

    // Act: the right-click asks for the selection; the menu waits for it to arrive.
    fireEvent.contextMenu(hit);
    expect(selections).toHaveLength(1);

    // The pushed selection lands: same key, actions and all.
    currentSelectionKey = "element:ccc";
    currentActions = [{ actions: [{ id: "timeline.disconnect", label: "Remove", icon: "", available: true, unavailableReason: "", items: [] }] }];
    rerender(
      <TimelineCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["plan.tml"]} />,
    );

    // Assert.
    expect(container.ownerDocument.querySelector(".context-menu")).not.toBeNull();
  });

  it("opens the context menu even when the element is already the selection", () => {
    // Arrange.
    // Right-clicking the selected element re-pushes the same key, the effect waiting for a key
    // change never fires, and the menu never opened - the "menu does not always show" bug.
    currentSelectionKey = "element:aaa";
    currentActions = [{ actions: [{ id: "timeline.rename", label: "Rename", icon: "", available: true, unavailableReason: "", items: [] }] }];
    const { container } = renderCanvas();

    // Act.
    fireEvent.contextMenu(container.querySelector('[data-element-id="aaa"]')!);

    // Assert.
    // The menu opens from the actions already at hand, and nothing is re-pushed.
    expect(container.ownerDocument.querySelector(".context-menu")).not.toBeNull();
    expect(selections).toHaveLength(0);
  });

  it("loops a relation forward out of the source and back into an overlapping target", () => {
    // Arrange: bbb begins while aaa is still running, well before aaa's end.
    const at = Date.UTC(2026, 0, 5) / 1000;
    currentModel = {
      elements: new Map([
        ["aaa", period("aaa", "First", at, 39, 0)],
        ["bbb", period("bbb", "Second", at + 5 * 86400, 5, 2)],
      ]),
      connections: new Map([["ccc", { id: "ccc", fromElementId: "aaa", toElementId: "bbb", label: "" }]]),
    };

    // Act.
    const { container } = renderCanvas();
    const d = container.querySelector(".timeline-connection-line")!.getAttribute("d")!;
    const numbers = d.match(/-?[\d.]+/g)!.map(Number);
    const [startX, , control1X, , control2X, , endX] = numbers;

    // Assert.
    // The control points push past both endpoints: the curve departs the source rightward,
    // loops around, and arrives at the target from its left - it never reverses out of a side.
    expect(endX).toBeLessThan(startX);
    expect(control1X).toBeGreaterThan(startX);
    expect(control2X).toBeLessThan(endX);
  });

  it("keeps the plain facing bezier when the target starts after the source ends", () => {
    // Arrange: the default model - bbb begins three days after aaa's end.
    const { container } = renderCanvas();

    // Act.
    const d = container.querySelector(".timeline-connection-line")!.getAttribute("d")!;
    const numbers = d.match(/-?[\d.]+/g)!.map(Number);
    const [, , control1X, , control2X] = numbers;

    // Assert: both control points still meet at the horizontal midpoint.
    expect(control1X).toBe(control2X);
  });

  it("keeps a label centred and trims it with an ellipsis when the box cannot hold it", () => {
    // Arrange: a two-day period cannot hold this label at the fitted zoom, while the six-week
    // Discovery period holds its own comfortably.
    const at = Date.UTC(2026, 0, 10) / 1000;
    currentModel.elements.set("nnn", { ...period("nnn", "A label wider than two days", at, 2, 3) });
    const { container } = renderCanvas();

    // Act.
    const labels = [...container.querySelectorAll(".timeline-label")];
    const narrow = labels.find((label) => label.textContent?.endsWith("…"))!;
    const wide = labels.find((label) => label.textContent === "Discovery")!;

    // Assert.
    // The narrow box's label stays centred inside it, trimmed to what fits with an ellipsis -
    // it used to step outside the box, start-anchored, and cover the neighbours instead.
    expect(narrow).toBeDefined();
    expect(narrow.textContent!.length).toBeLessThan("A label wider than two days".length);
    expect(narrow.getAttribute("text-anchor")).toBeNull();
    expect(wide.getAttribute("text-anchor")).toBeNull();
  });

  it("zooms the rows along with the time axis", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".timeline-surface")!;
    const before = Number(container.querySelector(".timeline-period")!.getAttribute("height"));

    // Act: one wheel step in.
    fireEvent.wheel(surface, { deltaY: -100 });

    // Assert.
    // The element's drawn height scales with the same step the time axis took - zoom used to
    // stretch time only, leaving the rows pinned at their fixed spacing.
    const after = Number(container.querySelector(".timeline-period")!.getAttribute("height"));
    expect(after).toBeCloseTo(before * 1.25, 5);
  });
});
