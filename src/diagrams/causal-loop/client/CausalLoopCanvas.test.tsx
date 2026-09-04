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

const select = vi.fn();
const moveElementTo = vi.fn(() => Promise.resolve(""));
const reportView = vi.fn();
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
    useContextConnection: () => ({ watchId: new Uint8Array(16), select }),
    useContextSelection: () => ({ selection: currentSelection }),
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

  it("selects on click", () => {
    const { container } = renderCanvas();

    fireEvent.click(container.querySelector('[data-element-id="variable:a"]')!);

    expect(select).toHaveBeenCalled();
  });

  it("dispatches a move when a variable is dragged, and only then", () => {
    const { container } = renderCanvas();
    const target = container.querySelector('[data-element-id="variable:a"]')!;

    fireEvent.mouseDown(target, { button: 0, clientX: 10, clientY: 10 });
    fireEvent.mouseMove(container.querySelector("svg")!, { clientX: 80, clientY: 60 });
    fireEvent.mouseUp(container.querySelector("svg")!);

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
