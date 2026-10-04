import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { SankeyFlowPayloadSchema, SankeyNodePayloadSchema } from "@client/generated/sankey_pb";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";
import { fakeContextConnection, idsPushed, pointer } from "@client/canvas/library/testing/canvasHarness";
import { SankeyActions } from "./sankeyIds";
import { emptyModel, NODE_GAP, type SankeyModel } from "./sankeyModel";

/**
 * The REAL canvas, mounted: what the library draws from this module's definition and model - bars
 * in their colours, bands between them, labels on the right side - and what a drag sends.
 */

let currentModel: SankeyModel = emptyModel;
let currentSelectionKey: string | null = null;
let selections: unknown[] = [];
let executed: { actionId: string; targetId: string }[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];
let answer: Promise<string> = Promise.resolve("");

type Source = { source: { value: { value: string } } };
const idOf = (source: unknown) => (source as Source).source.value.value;

vi.mock("@client/diagrams/useDiagramStream", () => ({
  useDiagramStream: () => ({
    model: currentModel,
    loading: false,
    failed: false,
    client: { moveElement: () => Promise.resolve({ error: "" }) },
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return answer;
    },
  }),
}));

vi.mock("@client/diagrams/viewReport", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@client/diagrams/viewReport")>()),
  viewReportOf: () => () => {},
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  innermostKey: () => currentSelectionKey,
  useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  useContextSelection: () => ({ selection: currentSelectionKey, levels: [], actions: [] }),
  useContextConnection: () => connection,
}));

const connection = fakeContextConnection({
  watchId: new Uint8Array([9]),
  select: (selection: unknown) => selections.push(selection),
  executeAction: (actionId: string, source?: unknown) => {
    executed.push({ actionId, targetId: idOf(source) });
    return Promise.resolve({ accepted: true, error: "" });
  },
});

vi.mock("@client/shell/panels/DiagramViewContext", () => ({ useRegisterDiagramView: () => undefined }));
vi.mock("@client/shell/panels/InlineLabelPlacementContext", () => ({ useRegisterInlineLabelPlacement: () => undefined }));
vi.mock("@client/shell/panels/DiagramToolboxContext", () => ({
  TOOLBOX_DRAG_TYPE: "application/x-adp-toolbox-item",
  useRegisterDiagramToolbox: () => undefined,
}));
vi.mock("@client/shell/panels/useToolboxItems", () => ({ useToolboxItems: () => [] }));

const { SankeyCanvas } = await import("./SankeyCanvas");

/** Two sources stacked in the first column, 100 tall, both flowing into a middle 200 tall - centres, as the backend sends them. */
function modelWith(): SankeyModel {
  const node = (name: string, height: number, column: number, color: string, customColor = "", note = "") =>
    create(SankeyNodePayloadSchema, { name, displayValue: `€${height}M`, note, width: 24, height, column, color, customColor, side: column === 0 ? "left" : "right" });
  const flow = (fromElementId: string, toElementId: string, targetAt: number) =>
    create(SankeyFlowPayloadSchema, { fromElementId, toElementId, displayValue: "€100M", thickness: 100, sourceAt: 0.5, targetAt, color: "grey" });
  return {
    nodes: new Map([
      ["a", { id: "a", x: 12, y: 50, payload: node("Alpha", 100, 0, "blue") }],
      ["b", { id: "b", x: 12, y: 50 + 100 + NODE_GAP, payload: node("Beta", 100, 0, "", "#336699") }],
      ["m", { id: "m", x: 332, y: 118, payload: node("Middle", 200, 1, "green", "", "+6% Y/Y") }],
    ]),
    flows: new Map([
      ["a->m", { id: "a->m", payload: flow("a", "m", 0.25) }],
      ["b->m", { id: "b->m", payload: flow("b", "m", 0.75) }],
    ]),
  };
}

function renderCanvas() {
  return render(<SankeyCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["company.skv"]} />);
}

const elementOn = (container: HTMLElement, id: string) => container.querySelector(`[data-element-id="${id}"]`);
const connectionOn = (container: HTMLElement, id: string) => container.querySelector(`[data-connection-id="${id}"]`);

/** Where an element is drawn: its group's translation, which moves one-for-one with its centre. */
function drawnY(container: HTMLElement, id: string): number {
  const transform = elementOn(container, id)!.querySelector("g[transform]")!.getAttribute("transform")!;
  return Number(/translate\(\s*-?[\d.]+[\s,]+(-?[\d.]+)\s*\)/.exec(transform)![1]);
}

beforeEach(() => {
  currentModel = modelWith();
  currentSelectionKey = null;
  selections = [];
  executed = [];
  moves = [];
  answer = Promise.resolve("");
});

describe("the sankey canvas, mounted", () => {
  it("draws each node as a bar in its colour, its labels before it in the first column and after it elsewhere", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    const alpha = elementOn(container, "a")!;
    expect(alpha.querySelector<SVGElement>(".sankey-bar")!.style.fill).toBe("var(--color-diagram-sankey-blue)");
    expect(elementOn(container, "b")!.querySelector<SVGElement>(".sankey-bar")!.style.fill).toBe("#336699");
    const name = alpha.querySelector<SVGTextElement>(".sankey-name")!;
    expect(name.textContent).toBe("Alpha");
    expect(name.style.textAnchor).toBe("end");
    const middle = elementOn(container, "m")!;
    expect(middle.querySelector<SVGTextElement>(".sankey-name")!.style.textAnchor).toBe("start");
    expect(middle.querySelector(".sankey-value")!.textContent).toBe("€200M");
    expect(middle.querySelector(".sankey-note")!.textContent).toBe("+6% Y/Y");

    // And beside that presence, the absence: a node with no note draws no note line.
    expect(alpha.querySelector(".sankey-note")).toBeNull();
  });

  it("draws each flow as a filled band in its colour, over a centre line nobody sees", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    const band = connectionOn(container, "a->m")!.querySelector<SVGPathElement>(".sankey-band")!;
    expect(band.getAttribute("d")).toMatch(/Z$/);
    expect(band.style.fill).toBe("var(--color-diagram-sankey-grey)");
    expect(connectionOn(container, "a->m")!.querySelector(".sankey-flow-line")).not.toBeNull();
  });

  it("makes room as a node is dragged past its neighbour, and sends the drop", async () => {
    // Arrange.
    let settle: (refusal: string) => void = () => {};
    answer = new Promise((resolve) => (settle = resolve));
    const { container } = renderCanvas();
    const alpha = elementOn(container, "a")!;
    const betaBefore = drawnY(container, "b");

    // Act: Alpha dragged down past Beta's middle.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 0, clientY: 0 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 0, clientY: 150 }));
    const betaDuring = drawnY(container, "b");
    fireEvent(alpha, pointer("pointerup", { clientX: 0, clientY: 150 }));

    // Assert: Beta moved up into the room Alpha left, Alpha was sent where it was let go, and it
    // is drawn in the slot below Beta while the backend answers.
    expect(betaDuring).toBeCloseTo(betaBefore - (100 + NODE_GAP), 5);
    expect(moves).toEqual([{ elementId: "a", x: 0, y: 150 }]);
    expect(drawnY(container, "a")).toBeCloseTo(drawnY(container, "b") + 100 + NODE_GAP, 5);
    settle("");
  });

  it("keeps a dragged node in its column", () => {
    // Arrange.
    const { container } = renderCanvas();
    const alpha = elementOn(container, "a")!;

    // Act: dragged far to the right as well as down.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 0, clientY: 0 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 400, clientY: 10 }));
    fireEvent(alpha, pointer("pointerup", { clientX: 400, clientY: 10 }));

    // Assert: the left edge it was sent at is still the column's.
    expect(moves).toHaveLength(1);
    expect(moves[0].x).toBe(0);
  });

  it("asks for a new name when a node is double-clicked", () => {
    // Arrange.
    const { container } = renderCanvas();

    // Act.
    fireEvent.doubleClick(elementOn(container, "m")!);

    // Assert.
    expect(executed).toContainEqual({ actionId: SankeyActions.rename, targetId: "m" });
  });

  it("selects through the library, exactly as every other canvas does", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentSelectionKey = id === null ? null : `element:${id}`;
        return renderCanvas();
      },
      pushedIds: () => idsPushed(selections),
      element: "m",
      connection: "a->m",
    });
  });
});
