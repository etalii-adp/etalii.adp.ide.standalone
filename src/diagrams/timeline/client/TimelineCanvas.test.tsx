import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render, waitFor } from "@testing-library/react";
import { emptyModel, type TimelineModel } from "./timelineModel";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";
import { fakeContextConnection, idsPushed, pointer } from "@client/canvas/library/testing/canvasHarness";

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
let currentReportView: ((viewport: unknown) => void) | null = null;
let currentPrompt: unknown = null;
const proposeLabel = vi.fn(async () => ({ accepted: true, error: "" }));
const submitLabel = vi.fn(async () => ({ accepted: true, error: "" }));
const cancelLabel = vi.fn();

vi.mock("./useTimelineStream", () => ({
  useTimelineStream: () => ({
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

const { TimelineCanvas, timelineScaleOf, nearestRow } = await import("./TimelineCanvas");

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

/** Where the frozen scale draws things - computed with the module's own exported mapping. */
function unitsOf(model: TimelineModel) {
  const scale = timelineScaleOf(model);
  return {
    x: (seconds: number) => (seconds - scale.originSeconds) / scale.secondsPerUnit,
    y: (moduleY: number) => moduleY - scale.originY,
    seconds: (units: number) => scale.originSeconds + units * scale.secondsPerUnit,
  };
}

function renderCanvas() {
  return render(
    <TimelineCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["plan.tml"]} />,
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
  currentReportView = null;
});

function press(target: Element, init: MouseEventInit = {}) {
  fireEvent(target, pointer("pointerdown", { button: 0, ...init }));
  fireEvent(target, pointer("pointerup", { ...init }));
}

function drag(target: Element, fromX: number, fromY: number, toX: number, toY: number) {
  fireEvent(target, pointer("pointerdown", { button: 0, clientX: fromX, clientY: fromY }));
  fireEvent(target, pointer("pointermove", { clientX: toX, clientY: toY }));
  fireEvent(target, pointer("pointerup", { clientX: toX, clientY: toY }));
}

const elementOn = (container: HTMLElement, id: string) => container.querySelector(`[data-element-id="${id}"]`)!;
const anchorOn = (container: HTMLElement, id: string, name: string) =>
  container.querySelector(`[data-element-id="${id}"] [data-anchor="${name}"]`)!;
const surfaceOf = (container: HTMLElement) => container.querySelector("svg.library-canvas-surface")!;

describe("the timeline canvas, on the library", () => {
  it("wears the shared canvas classes, so the central stylesheet is what dresses it", () => {
    const { container } = renderCanvas();

    expect(container.querySelector(".timeline-canvas")!.classList.contains("canvas-host")).toBe(true);
    expect(container.querySelector(".timeline-surface")!.classList.contains("canvas-viewport")).toBe(true);
    expect(container.querySelector(".timeline-period")!.classList.contains("canvas-node")).toBe(true);
    expect(container.querySelector(".timeline-label")!.classList.contains("canvas-node-label")).toBe(true);
    expect(container.querySelector(".timeline-connection")!.classList.contains("canvas-connection")).toBe(true);
    expect(container.querySelector(".timeline-connection-line")!.classList.contains("canvas-connection-line")).toBe(true);
  });

  it("still shows both scrollbars and still pans on a thumb drag", () => {
    const { container } = renderCanvas();
    const horizontalBar = container.querySelector(".canvas-scrollbar-horizontal")!;
    const verticalBar = container.querySelector(".canvas-scrollbar-vertical")!;
    const thumb = horizontalBar.querySelector<HTMLElement>(".canvas-scrollbar-thumb")!;
    Object.defineProperty(horizontalBar, "getBoundingClientRect", {
      value: () => ({ x: 0, y: 0, top: 0, left: 0, right: 400, bottom: 10, width: 400, height: 10, toJSON: () => ({}) }),
    });
    const before = thumb.style.left;

    fireEvent.mouseDown(thumb, { clientX: 100, clientY: 5 });
    fireEvent.mouseMove(window, { clientX: 180, clientY: 5 });
    fireEvent.mouseUp(window);

    expect(horizontalBar.classList.contains("timeline-scrollbars")).toBe(true);
    expect(verticalBar.classList.contains("timeline-scrollbars")).toBe(true);
    expect(thumb.style.left).not.toBe(before);
  });

  it("lays the ruler out across the surface's real width, not an assumed one", () => {
    // The defect this guards: the ruler was handed the constant the frozen scale is normalized
    // against (1200) as though it were the surface's width. The ladder was then laid out for a
    // 1200px ruler whatever the pane really was, and the labels stopped agreeing with the
    // elements they date - measured live at 469px, a milestone at 2026-01-06 drew 130px to the
    // left of where the ruler put "2026".
    //
    // jsdom computes no layout, so the surface's width is stubbed here; without the stub the
    // measurement reads zero and the fallback stands, which is what every other test in this
    // file relies on.
    const measured = 400;
    const original = Element.prototype.getBoundingClientRect;
    Element.prototype.getBoundingClientRect = function (this: Element) {
      return this.classList?.contains("timeline-canvas")
        ? ({ x: 0, y: 0, top: 0, left: 0, right: measured, bottom: 300, width: measured, height: 300, toJSON: () => ({}) } as DOMRect)
        : original.call(this);
    };

    try {
      const { container } = renderCanvas();
      const offsets = [...container.querySelectorAll<HTMLElement>(".timeline-ruler-tick")].map((tick) => Number.parseFloat(tick.style.left));

      expect(offsets.length).toBeGreaterThan(0);
      // A ladder built for 1200px reaches roughly three times as far across a 400px surface.
      // The last label may sit a little beyond the edge; three times beyond it is the bug.
      expect(Math.max(...offsets)).toBeLessThanOrEqual(measured * 1.2);
    } finally {
      Element.prototype.getBoundingClientRect = original;
    }
  });

  it("draws the period, the moment, the connection and the ruler", () => {
    const { container } = renderCanvas();

    expect(container.querySelectorAll(".timeline-period")).toHaveLength(1);
    expect(container.querySelectorAll(".timeline-moment")).toHaveLength(1);
    expect(container.querySelectorAll(".timeline-connection")).toHaveLength(1);
    expect(container.querySelector(".timeline-ruler")).not.toBeNull();
    expect(container.textContent).toContain("Discovery");
    expect(container.textContent).toContain("gates");
  });

  it("leaves the unavailable state to the library's frame rather than saying it itself", () => {
    // client-centralization Requirement 2.3 - one appearance, drawn by the library.
    currentFailed = true;
    const { container } = renderCanvas();

    expect(container.textContent).not.toContain("could not be opened");
  });

  it("treats a motionless press as a selection, never an edit", () => {
    const { container } = renderCanvas();
    const units = unitsOf(currentModel);

    press(elementOn(container, "aaa"), { clientX: 200, clientY: units.y(0) + 18 });

    expect(moves).toHaveLength(0);
    expect(selections).toHaveLength(1);
  });

  it("commits a drag as one move in module coordinates, snapped to a row", () => {
    const { container } = renderCanvas();
    const units = unitsOf(currentModel);
    const y = units.y(0) + 18;

    // Right by 50 units, down by most of a row - close enough to snap to row 1. jsdom's zero
    // rect makes one pointer pixel one canvas unit.
    drag(elementOn(container, "aaa"), 200, y, 250, y + ROW_HEIGHT - 10);

    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe("aaa");
    // y arrives as row × height - a whole row, never a pointer-precise position.
    expect(moves[0].y % ROW_HEIGHT).toBe(0);
    expect(moves[0].y / ROW_HEIGHT).toBe(1);
  });

  it("shows during a drag exactly where the element comes to rest once the backend confirms it", () => {
    // THE USER'S REPORT: the snap seen while dragging was not the snap on the drop, twice over.
    // The library snapped the element's CENTRE to whole rows while this canvas draws a row's
    // element with its TOP on the row, so a dragged span rested half a span above its row; and
    // x moved freely while the backend lands a date-only begin on the start of its day. What is
    // asserted is the property itself: the box drawn mid-drag is the box drawn after the model
    // says what the drop sent - and, for a date-only element, what was sent is a whole day, so
    // the backend's own day rule has nothing left to change.
    const { container, rerender } = renderCanvas();
    const units = unitsOf(currentModel);
    const y = units.y(0) + 18;
    const element = elementOn(container, "aaa");
    const boxOf = () => {
      const box = container.querySelector('[data-element-id="aaa"] .library-shape')!;
      return { x: Number(box.getAttribute("x")), y: Number(box.getAttribute("y")) };
    };

    // Down by a row and a bit, across by a fraction of a day.
    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 200, clientY: y }));
    fireEvent(element, pointer("pointermove", { clientX: 237, clientY: y + ROW_HEIGHT + 7 }));
    const during = boxOf();
    fireEvent(element, pointer("pointerup", { clientX: 237, clientY: y + ROW_HEIGHT + 7 }));

    expect(moves, "the drag never dropped").toHaveLength(1);
    const sent = moves[0];
    expect(sent.y, "the drop did not change row, so there was no snap to compare").toBe(ROW_HEIGHT);
    // "aaa" is date-only: a begin the backend would move to midnight is a jump nobody saw.
    // Whole seconds first, as TimelineScale.ToTime rounds them before it takes the day.
    expect(Math.round(sent.x) % 86400, `sent begin ${sent.x} is not a whole day`).toBe(0);

    const before = currentModel.elements.get("aaa")!;
    currentModel = { ...currentModel, elements: new Map([...currentModel.elements, ["aaa", { ...before, x: Math.round(sent.x), y: sent.y, row: sent.y / ROW_HEIGHT }]]) };
    rerender(<TimelineCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["plan.tml"]} />);
    const after = boxOf();

    expect(after.x, `drawn at x ${during.x} while dragging, ${after.x} once confirmed`).toBeCloseTo(during.x, 3);
    expect(after.y, `drawn at y ${during.y} while dragging, ${after.y} once confirmed`).toBeCloseTo(during.y, 6);
  });

  it("abandons a drag on Escape with nothing dispatched", () => {
    const { container } = renderCanvas();
    const units = unitsOf(currentModel);
    const element = elementOn(container, "aaa");
    const y = units.y(0) + 18;

    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 200, clientY: y }));
    fireEvent(element, pointer("pointermove", { clientX: 400, clientY: y + 100 }));
    fireEvent.keyDown(surfaceOf(container), { key: "Escape" });
    fireEvent(element, pointer("pointerup", { clientX: 400, clientY: y + 100 }));

    expect(moves).toHaveLength(0);
  });

  it("shows the landing placement while a drag is in progress", () => {
    const { container } = renderCanvas();
    const units = unitsOf(currentModel);
    const element = elementOn(container, "aaa");
    const y = units.y(0) + 18;

    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 200, clientY: y }));
    fireEvent(element, pointer("pointermove", { clientX: 300, clientY: y + ROW_HEIGHT }));

    // The times and the row it would land on, visible before the user commits.
    const hint = container.querySelector(".timeline-hint");
    expect(hint).not.toBeNull();
    expect(hint!.textContent).toContain("row 1");
  });

  it("shows the landing row as a WHOLE row, not the fraction it is dragged to", () => {
    // THE ASSERTION `toContain("row 1")` CANNOT MAKE, and a sabotage found the gap: an
    // unrounded row reads "row 1.03333" and contains "row 1" as a substring, so the test above
    // stays green against a hint that shows a fraction of a row. Only an exact tail
    // discriminates - the same shape of vacuity as a membership check against a uniform shift.
    const { container } = renderCanvas();
    const units = unitsOf(currentModel);
    const element = elementOn(container, "aaa");
    const y = units.y(0) + 18;

    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 200, clientY: y }));
    fireEvent(element, pointer("pointermove", { clientX: 300, clientY: y + ROW_HEIGHT + 4 }));

    const hint = container.querySelector(".timeline-hint")!;
    expect(hint.textContent!.endsWith("· row 1")).toBe(true);
  });

  it("relates the source to the target in one stateless call, in the gesture's own direction", () => {
    const { container } = renderCanvas();
    const units = unitsOf(currentModel);
    const at = Date.UTC(2026, 0, 5) / 1000;
    const endAnchor = anchorOn(container, "aaa", "end");

    // From aaa's end anchor into bbb's diamond, a real drag.
    drag(endAnchor, units.x(at + 39 * 86400), units.y(0) + 18, units.x(at + 42 * 86400) + 9, units.y(2 * ROW_HEIGHT) + 18);

    const calls = executed.filter((call) => call.actionId === "timeline.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toBe("rel:aaa->bbb");
  });

  it("reverses the relation when the drag lifts from the begin anchor", () => {
    const { container } = renderCanvas();
    const units = unitsOf(currentModel);
    const at = Date.UTC(2026, 0, 5) / 1000;
    const beginAnchor = anchorOn(container, "aaa", "begin");

    drag(beginAnchor, units.x(at), units.y(0) + 18, units.x(at + 42 * 86400) + 9, units.y(2 * ROW_HEIGHT) + 18);

    // What precedes an element points INTO it: the landing becomes the relation's source.
    const calls = executed.filter((call) => call.actionId === "timeline.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toBe("rel:bbb->aaa");
  });

  it("never connects from a plain click on the element body", () => {
    const { container } = renderCanvas();
    const units = unitsOf(currentModel);

    press(elementOn(container, "aaa"), { clientX: 200, clientY: units.y(0) + 18 });

    expect(executed.filter((call) => call.actionId === "timeline.connect")).toHaveLength(0);
  });

  it("lands a toolbox drop as a placement action, wherever it falls", () => {
    const { container } = renderCanvas();
    const data = new Map([["application/x-adp-toolbox-item", "timeline.add-element"]]);

    fireEvent.drop(surfaceOf(container), {
      clientX: 300,
      clientY: 200,
      dataTransfer: { getData: (type: string) => data.get(type) ?? "", types: [...data.keys()] },
    });

    const calls = executed.filter((call) => call.actionId === "timeline.add-element");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^new:/);
  });

  it("pans with a left drag on empty space, and empty space consumes the browser menu", () => {
    // The library's arbiter pans on the primary button; the right button stays the menu's.
    // The one behaviour shift of the migration - right-drag panning - is recorded in the
    // tasks document; the browser menu on empty space stays consumed as before.
    const { container } = renderCanvas();
    const surface = surfaceOf(container);
    const before = surface.getAttribute("viewBox");

    drag(surface, 400, 300, 320, 300);
    const menuPrevented = !fireEvent.contextMenu(surface, { clientX: 320, clientY: 300 });

    expect(surface.getAttribute("viewBox")).not.toBe(before);
    expect(menuPrevented).toBe(true);
    // A moved pan neither selects nor deselects - the press's verdict came at release, and
    // it was a pan. (The old canvas cleared the selection at press time; the library defers.)
    expect(selections).toEqual([]);
  });

  it("forwards Tab against the selection as a shortcut, letting the backend own the table", () => {
    const { container } = renderCanvas();

    fireEvent.keyDown(container.querySelector(".timeline-canvas")!, { key: "Tab" });

    // Nothing selected in this mock, so nothing is forwarded - a Tab with no selection must
    // stay the browser's own focus traversal.
    expect(shortcuts).toHaveLength(0);
  });

  it("completes a relation onto empty space as a create-and-relate placement", () => {
    const { container } = renderCanvas();
    const units = unitsOf(currentModel);
    const at = Date.UTC(2026, 0, 5) / 1000;

    // From aaa's end anchor onto nothing at all.
    drag(anchorOn(container, "aaa", "end"), units.x(at + 39 * 86400), units.y(0) + 18, units.x(at + 50 * 86400), units.y(5 * ROW_HEIGHT));

    const calls = executed.filter((call) => call.actionId === "timeline.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^rel:aaa->new:/);
  });

  it("a begin-anchor drag onto empty space puts the placement at the relation's source", () => {
    const { container } = renderCanvas();
    const units = unitsOf(currentModel);
    const at = Date.UTC(2026, 0, 5) / 1000;

    drag(anchorOn(container, "aaa", "begin"), units.x(at), units.y(0) + 18, units.x(at - 10 * 86400), units.y(5 * ROW_HEIGHT));

    const calls = executed.filter((call) => call.actionId === "timeline.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^rel:new:.+->aaa$/);
  });

  it("a surface release with no gesture in flight fabricates no calls", () => {
    const { container } = renderCanvas();

    fireEvent(surfaceOf(container), pointer("pointerup", { clientX: 100, clientY: 100 }));

    expect(executed).toHaveLength(0);
  });

  it("selects a relation on a press, through its fat hit path", () => {
    const { container } = renderCanvas();

    press(container.querySelector(".timeline-connection-hit")!, { clientX: 1064, clientY: 138 });

    expect(selections).toHaveLength(1);
  });

  it("marks the selected relation, so the selection is visible", () => {
    currentSelectionKey = "element:ccc";
    const { container } = renderCanvas();

    expect(container.querySelector(".timeline-connection")!.classList.contains("canvas-selected")).toBe(true);
  });

  it("resizes a period's edge as one property edit, clamped at the other edge", () => {
    // The resize handles are the library's, earned by sizing: "user" when selected; the
    // commit stays the module's own property channel - one command, one undo.
    currentSelectionKey = "element:aaa";
    const { container } = renderCanvas();
    const units = unitsOf(currentModel);
    const at = Date.UTC(2026, 0, 5) / 1000;
    const handle = container.querySelector('[data-element-id="aaa"] [data-resize="right"]')!;
    expect(handle).not.toBeNull();

    // Carry the end edge one day further.
    const edge = units.x(at + 39 * 86400);
    drag(handle, edge, units.y(0) + 18, units.x(at + 40 * 86400), units.y(0) + 18);

    expect(properties).toHaveLength(1);
    expect(properties[0].propertyId).toBe("timeline.end");
    expect(properties[0].value).toBe("2026-02-14");
  });

  it("loops a relation forward out of the source and back into an overlapping target", () => {
    const at = Date.UTC(2026, 0, 5) / 1000;
    currentModel = {
      elements: new Map([
        ["aaa", period("aaa", "First", at, 39, 0)],
        ["bbb", period("bbb", "Second", at + 5 * 86400, 5, 2)],
      ]),
      connections: new Map([["ccc", { id: "ccc", fromElementId: "aaa", toElementId: "bbb", label: "" }]]),
    };

    const { container } = renderCanvas();
    const d = container.querySelector(".timeline-connection-line")!.getAttribute("d")!;
    const numbers = d.match(/-?[\d.]+/g)!.map(Number);
    const [startX, , control1X, , control2X, , endX] = numbers;

    // The control points push past both endpoints: the curve departs the source rightward,
    // loops around, and arrives at the target from its left - it never reverses out of a side.
    expect(endX).toBeLessThan(startX);
    expect(control1X).toBeGreaterThan(startX);
    expect(control2X).toBeLessThan(endX);
  });

  it("keeps the plain facing bezier when the target starts after the source ends", () => {
    const { container } = renderCanvas();

    const d = container.querySelector(".timeline-connection-line")!.getAttribute("d")!;
    const numbers = d.match(/-?[\d.]+/g)!.map(Number);
    const [, , control1X, , control2X] = numbers;

    expect(control1X).toBe(control2X);
  });

  it("keeps a label centred and trims it with an ellipsis when the box cannot hold it", () => {
    const at = Date.UTC(2026, 0, 10) / 1000;
    currentModel.elements.set("nnn", { ...period("nnn", "A label wider than two days", at, 2, 3) });
    const { container } = renderCanvas();

    const labels = [...container.querySelectorAll(".timeline-label")];
    const narrow = labels.find((label) => label.textContent?.endsWith("…"))!;
    const wide = labels.find((label) => label.textContent === "Discovery")!;

    expect(narrow).toBeDefined();
    expect(narrow.textContent!.length).toBeLessThan("A label wider than two days".length);
    expect(narrow.getAttribute("text-anchor")).toBeNull();
    expect(wide.getAttribute("text-anchor")).toBeNull();
  });

  it("zooms the rows along with the time axis", () => {
    // One uniform viewBox zoom scales both axes by the same step - which is exactly "the
    // rows spread and squeeze along with the time axis", now by construction.
    const { container } = renderCanvas();
    const surface = surfaceOf(container);
    const before = surface.getAttribute("viewBox")!.split(" ").map(Number);

    fireEvent.wheel(surface, { deltaY: -100 });

    const after = surface.getAttribute("viewBox")!.split(" ").map(Number);
    expect(after[2]).toBeCloseTo(before[2] / 1.25, 5);
    expect(after[3]).toBeCloseTo(before[3] / 1.25, 5);
  });

  it("reports the settled viewport in seconds and row units", async () => {
    const reportView = vi.fn();
    currentReportView = reportView;

    renderCanvas();

    await waitFor(() => expect(reportView).toHaveBeenCalled(), { timeout: 2000 });
    const viewport = reportView.mock.calls.at(-1)![0] as { minX: number; minY: number; maxX: number; maxY: number };
    expect(viewport.maxX).toBeGreaterThan(viewport.minX);
    expect(viewport.maxY).toBeGreaterThan(viewport.minY);

    // The unit claim, asserted rather than assumed: x is seconds since the epoch, so the left
    // edge is a number in the billions and the span is measured in days.
    expect(viewport.minX).toBeGreaterThan(1_000_000_000);
    const spanDays = (viewport.maxX - viewport.minX) / 86400;
    expect(spanDays).toBeGreaterThan(1);
    expect(spanDays).toBeLessThan(365 * 100);
  });

  it("reports again when the view changes, carrying the new rectangle", async () => {
    const reportView = vi.fn();
    currentReportView = reportView;
    const { container } = renderCanvas();
    await waitFor(() => expect(reportView).toHaveBeenCalled(), { timeout: 2000 });
    const first = reportView.mock.calls.at(-1)![0] as { minX: number; maxX: number };

    fireEvent.wheel(surfaceOf(container), { deltaY: 120 });

    await waitFor(
      () => {
        const latest = reportView.mock.calls.at(-1)![0] as { minX: number; maxX: number };
        expect(latest.maxX - latest.minX).not.toBeCloseTo(first.maxX - first.minX, 0);
      },
      { timeout: 2000 },
    );
  });

  it("reports nothing while the timeline is loading", async () => {
    const reportView = vi.fn();
    currentReportView = reportView;
    currentLoading = true;

    renderCanvas();
    await new Promise((resolve) => setTimeout(resolve, 400));

    expect(reportView).not.toHaveBeenCalled();
  });

  function labelPromptFor(elementId: string, initialValue: string): unknown {
    return {
      interactionId: { value: new Uint8Array(16).fill(9) },
      prompt: {
        case: "inputDialog",
        value: {
          title: "Rename",
          icon: "mdi-pencil-outline",
          fieldLabel: "Name",
          initialValue,
          confirmLabel: "Rename",
          inlineLabelEdit: { elementId: { value: elementId } },
        },
      },
    };
  }

  function labelField(container: HTMLElement): HTMLInputElement {
    return container.querySelector("input.inline-label-editor-field") as HTMLInputElement;
  }

  function editorBox(container: HTMLElement): SVGForeignObjectElement {
    return container.querySelector("foreignObject") as SVGForeignObjectElement;
  }

  it("opens an editor over a span, and submits what is typed into it", async () => {
    currentPrompt = labelPromptFor("aaa", "Discovery");

    const { container } = renderCanvas();

    expect(editorBox(container)).not.toBeNull();
    expect(labelField(container).value).toBe("Discovery");

    fireEvent.change(labelField(container), { target: { value: "Renamed" } });
    fireEvent.keyDown(labelField(container), { key: "Enter" });
    await waitFor(() => expect(submitLabel).toHaveBeenCalledWith("Renamed"));
  });

  it("opens an instant's editor beside its diamond, not over it", () => {
    currentPrompt = labelPromptFor("bbb", "Go");

    const { container } = renderCanvas();

    const diamond = container.querySelector("path.timeline-moment") as SVGPathElement;
    expect(diamond).not.toBeNull();
    const box = editorBox(container);
    expect(box).not.toBeNull();
    const centre = Number(diamond.getAttribute("d")?.match(/M\s*(-?[\d.]+)/)?.[1] ?? "0");
    expect(Number(box.getAttribute("x"))).toBeGreaterThan(centre);
  });

  it("places a connection's editor at the midpoint of the line, where its label is drawn", () => {
    currentPrompt = labelPromptFor("ccc", "gates");

    const { container } = renderCanvas();

    const drawn = container.querySelector('[data-connection-id="ccc"] text') as SVGTextElement;
    expect(drawn).not.toBeNull();
    const box = editorBox(container);
    expect(box).not.toBeNull();
    const centreOfEditor = Number(box.getAttribute("x")) + Number(box.getAttribute("width")) / 2;
    expect(centreOfEditor).toBeCloseTo(Number(drawn.getAttribute("x")), 5);
  });

  it("leaves the selection alone when an inline edit commits", async () => {
    currentSelectionKey = "aaa";
    currentPrompt = labelPromptFor("aaa", "Discovery");
    const { container } = renderCanvas();
    selections = [];

    fireEvent.change(labelField(container), { target: { value: "Renamed" } });
    fireEvent.keyDown(labelField(container), { key: "Enter" });

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

describe("nearestRow - the library's rule, not a copy of it", () => {
  // The halves and the zero side are where the five earlier copies went wrong: Math.round sends
  // -0.5 to -0, a drop half a row above the origin landing on row 0 while the same drop below
  // lands correctly. Architect 1's fixture pins the rule itself; this pins that the timeline uses it.
  it("rounds halves away from zero on both sides of the origin", () => {
    expect(nearestRow(30)).toBe(1);
    expect(nearestRow(-30)).toBe(-1);
    expect(nearestRow(-90)).toBe(-2);
  });

  it("never answers negative zero", () => {
    expect(Object.is(nearestRow(-1), 0)).toBe(true);
  });
});
