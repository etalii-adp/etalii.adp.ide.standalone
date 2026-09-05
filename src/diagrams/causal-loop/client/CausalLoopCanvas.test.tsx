import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, waitFor } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import {
  CausalLoopLinkPayloadSchema,
  CausalLoopLoopPayloadSchema,
  CausalLoopPolarityProto,
  CausalLoopVariablePayloadSchema,
  LoopPolarityProto,
} from "@client/generated/causal-loop_pb";
import { VIEW_REPORT_DEBOUNCE_MS } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type CausalLoopModel } from "./causalLoopModel";

/**
 * A pointer event jsdom can actually carry: jsdom implements no PointerEvent, and
 * `fireEvent.pointerDown` builds a bare Event whose `button` is undefined -
 * usePointerGesture.test.tsx's idiom, for the same reason.
 */
function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
}

// jsdom implements no pointer capture on SVG elements; the arbiter uses it.
SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

const select = vi.fn();
const moveElementTo = vi.fn(() => Promise.resolve(""));
const reportView = vi.fn();
const executeAction = vi.fn(() => Promise.resolve());
let currentActions: unknown[] = [];
let currentModel: CausalLoopModel = emptyModel;
let currentLoading = false;
let currentFailed = false;
let currentSelection: unknown = null;

vi.mock("./useCausalLoopStream", () => ({
  useCausalLoopStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    moveElementTo,
    reportView,
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({ watchId: new Uint8Array(16), select, executeAction }),
    // The canvas reads `actions` to decide whether a context menu has anything to show, so the
    // mock has to carry them: a shape that is missing here is a crash there, which is what this
    // suite caught the moment the menu was wired.
    useContextSelection: () => ({ selection: currentSelection, actions: currentActions }),
  };
});

const emptyPalette: never[] = [];

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: () => emptyPalette,
}));

const { CausalLoopCanvas } = await import("./CausalLoopCanvas");

const VARIABLE_URL = "type.googleapis.com/etalii.adp.causalloop.CausalLoopVariablePayload";
const LINK_URL = "type.googleapis.com/etalii.adp.causalloop.CausalLoopLinkPayload";
const LOOP_URL = "type.googleapis.com/etalii.adp.causalloop.CausalLoopLoopPayload";

function variable(id: string, x: number, y: number, display = id) {
  const payload = create(CausalLoopVariablePayloadSchema, { display, id, width: 120, height: 40 });
  return create(ElementSchema, {
    id: { value: `variable:${id}` },
    position: { x, y },
    type: "systems/causal-loop+variable",
    payload: { typeUrl: VARIABLE_URL, value: toBinary(CausalLoopVariablePayloadSchema, payload) },
  });
}

function link(from: string, to: string, overrides: Record<string, unknown> = {}) {
  const payload = create(CausalLoopLinkPayloadSchema, {
    fromElementId: `variable:${from}`,
    toElementId: `variable:${to}`,
    polarity: CausalLoopPolarityProto.POSITIVE,
    ...overrides,
  });
  return create(ElementSchema, {
    id: { value: `link:${from}|${to}` },
    position: { x: 0, y: 0 },
    type: "systems/causal-loop+link",
    payload: { typeUrl: LINK_URL, value: toBinary(CausalLoopLinkPayloadSchema, payload) },
  });
}

function loop(identifier: string, members: string[], overrides: Record<string, unknown> = {}) {
  const payload = create(CausalLoopLoopPayloadSchema, {
    identifier,
    name: "the ring",
    computed: LoopPolarityProto.REINFORCING,
    memberElementIds: members.map((id) => `variable:${id}`),
    ...overrides,
  });
  return create(ElementSchema, {
    id: { value: `loop:${identifier}` },
    position: { x: 50, y: 50 },
    type: "systems/causal-loop+loop",
    payload: { typeUrl: LOOP_URL, value: toBinary(CausalLoopLoopPayloadSchema, payload) },
  });
}

function modelOf(...elements: ReturnType<typeof variable>[]): CausalLoopModel {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return applyDelta(emptyModel, { action: { case: "add", value: { elements } } } as any);
}

function renderCanvas() {
  return render(
    <CausalLoopCanvas projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={["feedback.adp"]} />,
  );
}

beforeEach(() => {
  select.mockClear();
  moveElementTo.mockClear();
  reportView.mockClear();
  currentLoading = false;
  currentFailed = false;
  currentSelection = null;
  currentActions = [];
  executeAction.mockClear();
  currentModel = modelOf(
    variable("a", 0, 0, "Alpha"),
    variable("b", 200, 0, "Beta"),
    link("a", "b"),
    link("b", "a", { polarity: CausalLoopPolarityProto.NEGATIVE }),
    loop("R1", ["a", "b"]),
  );
});

describe("CausalLoopCanvas", () => {
  it("draws a variable, its links and the loop label", () => {
    const { container } = renderCanvas();

    expect(container.querySelectorAll('[data-element-id^="variable:"]')).toHaveLength(2);
    expect(container.querySelectorAll('[data-element-id^="link:"]')).toHaveLength(2);
    expect(container.textContent).toContain("Alpha");
    expect(container.textContent).toContain("R1");
  });

  /**
   * The defect this section was written for: a two-variable feedback loop drew no loop.
   *
   * Both links took the shared tree connector, which anchors on the sides facing each other, so
   * `a -> b` and `b -> a` produced the same path and rendered as one line with an arrowhead at
   * each end. Every test above still passed while it did - two link elements existed, they had
   * their classes and their polarity marks - because nothing asserted anything about the shape.
   */
  it("draws the two directions of a loop as two different paths", () => {
    const { container } = renderCanvas();

    const paths = [...container.querySelectorAll('[data-element-id^="link:"] .canvas-connection-line')]
      .map((path) => path.getAttribute("d"));

    expect(paths).toHaveLength(2);
    expect(paths[0]).not.toEqual(paths[1]);
  });

  it("curves every link with a single control point, as the notation does", () => {
    const { container } = renderCanvas();

    for (const path of container.querySelectorAll('[data-element-id^="link:"] .canvas-connection-line')) {
      const d = path.getAttribute("d") ?? "";

      // One quadratic segment. A cubic would be the shared tree connector back again, which
      // draws an S rather than an arc and makes a ring read as a concertina.
      expect(d).toContain("Q");
      expect(d).not.toContain("C");
      expect(d).not.toContain("NaN");
    }
  });

  it("bows the two directions to opposite sides, which is what encloses the loop", () => {
    const { container } = renderCanvas();

    // The y of each path's control point - the number after the Q.
    const bows = [...container.querySelectorAll('[data-element-id^="link:"] .canvas-connection-line')]
      .map((path) => {
        const control = /Q\s+(-?[\d.]+)\s+(-?[\d.]+)/.exec(path.getAttribute("d") ?? "");
        return Number(control?.[2] ?? 0);
      });

    expect(bows).toHaveLength(2);
    expect(Math.sign(bows[0]!)).not.toBe(Math.sign(bows[1]!));
    expect(Math.abs(bows[0]!)).toBeGreaterThan(10);
  });

  it("draws the conventional loop marker, and turns it the way the loop turns", () => {
    const { container } = renderCanvas();

    // An identifier alone says a loop exists; the marker shows one. Reinforcing turns one way
    // and balancing the other, which is how the reference tools distinguish them at a glance.
    const reinforcing = container.querySelector(".causal-loop-marker")?.getAttribute("d") ?? "";
    expect(reinforcing).toContain("A");
    expect(reinforcing).not.toContain("NaN");

    currentModel = modelOf(
      variable("a", 0, 0),
      variable("b", 200, 0),
      loop("B1", ["a", "b"], { computed: LoopPolarityProto.BALANCING }),
    );

    const balancing = renderCanvas().container.querySelector(".causal-loop-marker")?.getAttribute("d") ?? "";
    expect(sweepOf(balancing)).not.toEqual(sweepOf(reinforcing));
  });

  it("composes the shared canvas classes rather than private ones", () => {
    const { container } = renderCanvas();

    // The rule five modules broke: compose canvas.css, do not restate it.
    expect(container.querySelector(".canvas-host")).not.toBeNull();
    expect(container.querySelector(".canvas-drawing")).not.toBeNull();
    expect(container.querySelector(".canvas-node")).not.toBeNull();
    expect(container.querySelector(".canvas-connection-line")).not.toBeNull();
    expect(container.querySelector(".canvas-arrowhead")).not.toBeNull();
  });

  it("marks polarity in the notation's own vocabulary", () => {
    const { container } = renderCanvas();
    const marks = [...container.querySelectorAll(".causal-loop-polarity")].map((node) => node.textContent);

    expect(marks).toContain("+");
    expect(marks).toContain("−");
  });

  it("draws no polarity mark where the document states none", () => {
    // An unmarked link is one the author has not decided; drawing a "+" would put a claim on
    // the canvas the document does not make.
    currentModel = modelOf(
      variable("a", 0, 0),
      variable("b", 200, 0),
      link("a", "b", { polarity: CausalLoopPolarityProto.UNSTATED }),
    );

    const { container } = renderCanvas();

    expect(container.querySelectorAll(".causal-loop-polarity")).toHaveLength(0);
    // ...and the link is still drawn: unknown polarity is not a reason to hide the causality.
    expect(container.querySelectorAll('[data-element-id^="link:"]')).toHaveLength(1);
  });

  it("marks a delay with strokes across the link", () => {
    currentModel = modelOf(
      variable("a", 0, 0),
      variable("b", 200, 0),
      link("a", "b", { delayed: true }),
    );

    const { container } = renderCanvas();

    expect(container.querySelectorAll(".causal-loop-delay line")).toHaveLength(2);
  });

  it("draws a reinforcing loop differently from a balancing one, without selecting anything", () => {
    const { container } = renderCanvas();
    expect(container.querySelector(".causal-loop-reinforcing")).not.toBeNull();

    currentModel = modelOf(
      variable("a", 0, 0),
      variable("b", 200, 0),
      loop("B1", ["a", "b"], { computed: LoopPolarityProto.BALANCING }),
    );

    expect(renderCanvas().container.querySelector(".causal-loop-balancing")).not.toBeNull();
  });

  it("marks a loop whose label disagrees with its arrows", () => {
    currentModel = modelOf(
      variable("a", 0, 0),
      variable("b", 200, 0),
      loop("R1", ["a", "b"], { computed: LoopPolarityProto.BALANCING, disagrees: true }),
    );

    const { container } = renderCanvas();

    // Marked rather than corrected: the canvas shows there is a disagreement and does not
    // decide which side is wrong.
    expect(container.querySelector(".causal-loop-disagrees")).not.toBeNull();
    expect(container.textContent).toContain("R1");
  });

  it("draws thicker for a heavier weight, on a short ladder", () => {
    currentModel = modelOf(
      variable("a", 0, 0),
      variable("b", 200, 0),
      link("a", "b", { hasWeight: true, weight: 3 }),
    );

    expect(renderCanvas().container.querySelector(".causal-loop-weight-heavy")).not.toBeNull();
  });

  it("does not draw a link whose far end it does not hold", () => {
    // The backend filters structurally, but a delta can still arrive out of order - and a line
    // to nothing is worse than no line.
    currentModel = modelOf(variable("a", 0, 0), link("a", "gone"));

    const { container } = renderCanvas();

    expect(container.querySelectorAll(".canvas-connection-line")).toHaveLength(0);
  });

  it("selects a variable on a press", () => {
    const { container } = renderCanvas();
    const target = container.querySelector('[data-element-id="variable:a"]')!;

    fireEvent(target, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(target, pointer("pointerup", { clientX: 10, clientY: 10 }));

    expect(select).toHaveBeenCalled();
  });

  it("selects a link on a press, decided at the gesture's end rather than by a click", () => {
    // A link is a relation: its selection goes through the shared arbiter, so a press that
    // turns into a drag is just not a click and a trailing click event selects nothing.
    const { container } = renderCanvas();
    const hit = container.querySelector(".causal-loop-link .canvas-connection-hit")!;

    fireEvent(hit, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(hit, pointer("pointerup", { clientX: 10, clientY: 10 }));

    expect(select).toHaveBeenCalledTimes(1);
  });

  it("dispatches a move when a variable is dragged, and only then", () => {
    const { container } = renderCanvas();
    const target = container.querySelector('[data-element-id="variable:a"]')!;

    fireEvent(target, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(target, pointer("pointermove", { clientX: 80, clientY: 60 }));
    fireEvent(target, pointer("pointerup", { clientX: 80, clientY: 60 }));

    expect(moveElementTo).toHaveBeenCalledTimes(1);
    expect((moveElementTo.mock.calls[0] as unknown as [string])[0]).toBe("variable:a");
  });

  it("reports the view it settles on, and the new one after a zoom", async () => {
    const { container } = renderCanvas();

    await waitFor(() => expect(reportView).toHaveBeenCalled(), { timeout: 2000 });
    const [x, y, w, h] = viewBoxOf(container);
    expect(reportView.mock.calls.at(-1)![0]).toEqual({ minX: x, minY: y, maxX: x + w, maxY: y + h });

    reportView.mockClear();
    fireEvent.wheel(container.querySelector("svg.causal-loop-canvas")!, { deltaY: -100 });

    await waitFor(() => expect(reportView).toHaveBeenCalled(), { timeout: 2000 });
    const [zx, zy, zw, zh] = viewBoxOf(container);
    expect(zw).toBeLessThan(w);
    expect(reportView.mock.calls.at(-1)![0]).toEqual({ minX: zx, minY: zy, maxX: zx + zw, maxY: zy + zh });
  });

  // ---- selection after a variable drag --------------------------------------------------

  /** Every real mouse click fires pointer events, compatibility mouse events AND a click. */
  function realPress(target: Element, x: number, y: number) {
    fireEvent(target, pointer("pointerdown", { button: 0, clientX: x, clientY: y }));
    fireEvent.mouseDown(target, { button: 0, clientX: x, clientY: y });
    fireEvent(target, pointer("pointerup", { clientX: x, clientY: y }));
    fireEvent.mouseUp(target, { clientX: x, clientY: y });
    fireEvent.click(target, { clientX: x, clientY: y });
  }

  /** A real drag: press, move past the threshold, release - and the trailing click the browser
   * then fires on whatever the drop's geometry left under the pointer. */
  function realDrag(pressTarget: Element, surface: Element, fromX: number, fromY: number, toX: number, toY: number, clickTarget: Element) {
    fireEvent(pressTarget, pointer("pointerdown", { button: 0, clientX: fromX, clientY: fromY }));
    fireEvent.mouseDown(pressTarget, { button: 0, clientX: fromX, clientY: fromY });
    fireEvent(pressTarget, pointer("pointermove", { clientX: toX, clientY: toY }));
    fireEvent.mouseMove(surface, { clientX: toX, clientY: toY });
    fireEvent(pressTarget, pointer("pointerup", { clientX: toX, clientY: toY }));
    fireEvent.mouseUp(surface, { clientX: toX, clientY: toY });
    fireEvent.click(clickTarget, { clientX: toX, clientY: toY });
  }

  function lastSelectedId(): string {
    const sel = select.mock.calls.at(-1)?.[0] as { detail?: { value?: { id?: { source?: { value?: { value?: string } } } } } } | null;
    return sel?.detail?.value?.id?.source?.value?.value ?? "(none)";
  }

  it("a completed drag leaves the selection alone - the trailing click selects nothing", () => {
    // The user-reported defect's first face: dropping a variable made the browser's trailing
    // click land on it, and its raw onClick then selected the very thing that was dragged.
    currentModel = modelOf(variable("a", 0, 0), variable("b", 300, 0), link("a", "b"));
    const { container } = renderCanvas();
    const a = container.querySelector('[data-element-id="variable:a"]')!;
    const surface = container.querySelector("svg")!;
    select.mockClear();

    realDrag(a, surface, 0, 0, 80, 60, a);

    expect(select).not.toHaveBeenCalled();
    expect(moveElementTo).toHaveBeenCalledTimes(1);
  });

  it("a drop landing on the loop badge does not select the loop", () => {
    // The second face: the R/B badge sits at the centre of its loop's variables - exactly
    // where drops land - and its raw onClick made the trailing click select the loop.
    currentModel = modelOf(variable("a", 0, 0), variable("b", 300, 0), link("a", "b"), loop("R1", ["a", "b"]));
    const { container } = renderCanvas();
    const a = container.querySelector('[data-element-id="variable:a"]')!;
    const badge = container.querySelector('[data-element-id="loop:R1"]')!;
    const surface = container.querySelector("svg")!;
    select.mockClear();

    realDrag(a, surface, 0, 0, 50, 50, badge);

    expect(select).not.toHaveBeenCalled();
  });

  it("selecting after a drag still selects the next variable pressed", () => {
    currentModel = modelOf(variable("a", 0, 0), variable("b", 300, 0), link("a", "b"));
    const { container } = renderCanvas();
    const a = container.querySelector('[data-element-id="variable:a"]')!;
    const b = container.querySelector('[data-element-id="variable:b"]')!;
    const surface = container.querySelector("svg")!;

    realPress(a, 0, 0);
    realDrag(a, surface, 0, 0, 80, 60, a);
    select.mockClear();

    realPress(b, 300, 0);

    expect(select).toHaveBeenCalledTimes(1);
    expect(lastSelectedId()).toBe("variable:b");
  });

  it("selects a loop on a press of its badge", () => {
    currentModel = modelOf(variable("a", 0, 0), variable("b", 300, 0), link("a", "b"), loop("R1", ["a", "b"]));
    const { container } = renderCanvas();
    const badge = container.querySelector('[data-element-id="loop:R1"]')!;
    select.mockClear();

    realPress(badge, 50, 50);

    expect(lastSelectedId()).toBe("loop:R1");
  });

  it("stays quiet while the diagram is still loading", async () => {
    currentLoading = true;
    renderCanvas();

    await new Promise((resolve) => setTimeout(resolve, VIEW_REPORT_DEBOUNCE_MS * 3));
    expect(reportView).not.toHaveBeenCalled();
  });

  it("says so when the diagram cannot be opened, and when it states nothing", () => {
    currentFailed = true;
    expect(renderCanvas().container.textContent).toContain("could not be opened");

    currentFailed = false;
    currentModel = emptyModel;
    expect(renderCanvas().container.textContent).toContain("states no variables");
  });
});

const viewBoxOf = (container: HTMLElement) =>
  (container.querySelector("svg.causal-loop-canvas")!.getAttribute("viewBox") ?? "").split(" ").map(Number);

/** The sweep flag of the first elliptical arc in a path - which way it turns. */
function sweepOf(path: string): string {
  return /A\s+[\d.]+\s+[\d.]+\s+\d+\s+\d+\s+(\d)/.exec(path)?.[1] ?? "";
}
