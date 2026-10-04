import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import {
  SupplyChainFlowPayloadSchema,
  SupplyChainGroupPayloadSchema,
  SupplyChainNodePayloadSchema,
} from "@client/generated/supply-chain_pb";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";
import { fakeContextConnection, idsPushed, pointer } from "@client/canvas/library/testing/canvasHarness";
import { SupplyChainActions } from "./supplyChainIds";
import { emptyModel, type SupplyChainModel } from "./supplyChainModel";

/**
 * The REAL canvas, mounted: what the library draws from this module's definition and model, the
 * trace the backend marks arriving as classes, the steppers of whatever is selected, and the
 * shared selection assertion every canvas's own test runs.
 */

let currentModel: SupplyChainModel = emptyModel;
let currentSelectionKey: string | null = null;
let selections: unknown[] = [];
let executed: { actionId: string; targetId: string }[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];

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
      return Promise.resolve("");
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

const { SupplyChainCanvas } = await import("./SupplyChainCanvas");

/** A mine feeding a plant inside one region, and the plant feeding a shop - centres, as the backend sends them. */
function modelWith(trace: Partial<Record<"mine" | "plant" | "shop" | "ore" | "goods" | "north", string>> = {}, plantQuantity = 4): SupplyChainModel {
  const node = (name: string, stage: string, quantity: number, unit: string, groupId: string, mark = "") =>
    create(SupplyChainNodePayloadSchema, { name, stage, quantity, hasQuantity: true, unit, groupId, width: 192, height: 96, trace: mark });
  const flow = (fromElementId: string, toElementId: string, product: string, volume: number, weight: number, mark = "") =>
    create(SupplyChainFlowPayloadSchema, { fromElementId, toElementId, product, volume, hasVolume: true, unit: "t", weight, trace: mark });
  return {
    groups: new Map([["north", { id: "north", x: 116, y: 270, payload: create(SupplyChainGroupPayloadSchema, { name: "North", width: 232, height: 300, members: 2, trace: trace.north ?? "" }) }]]),
    nodes: new Map([
      ["mine", { id: "mine", stage: "raw-material" as const, x: 116, y: 188, payload: node("Mine", "Raw material", 10, "t", "north", trace.mine) }],
      ["plant", { id: "plant", stage: "manufacturer" as const, x: 116, y: 352, payload: node("Plant", "Manufacturer", plantQuantity, "t", "north", trace.plant) }],
      ["shop", { id: "shop", stage: "retailer" as const, x: 600, y: 352, payload: node("Shop", "Retailer", 2, "boxes", "", trace.shop) }],
    ]),
    flows: new Map([
      ["ore", { id: "ore", payload: flow("mine", "plant", "Ore", 6, 1, trace.ore) }],
      ["goods", { id: "goods", payload: flow("plant", "shop", "Goods", 2, 1 / 3, trace.goods) }],
    ]),
  };
}

function renderCanvas() {
  return render(<SupplyChainCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["automotive.supply"]} />);
}

const elementOn = (container: HTMLElement, id: string) => container.querySelector(`[data-element-id="${id}"]`);
const connectionOn = (container: HTMLElement, id: string) => container.querySelector(`[data-connection-id="${id}"]`);

function press(target: Element) {
  fireEvent(target, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
  fireEvent(target, pointer("pointerup", { clientX: 10, clientY: 10 }));
}

beforeEach(() => {
  currentModel = modelWith();
  currentSelectionKey = null;
  selections = [];
  executed = [];
  moves = [];
});

describe("the supply chain canvas, mounted", () => {
  it("draws each node as a card in its stage's class, with its name, amount and unit", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    const mine = elementOn(container, "mine")!;
    expect(mine.classList.contains("supply-chain-stage-raw-material")).toBe(true);
    expect(mine.querySelector(".supply-chain-card")).not.toBeNull();
    expect(mine.textContent).toContain("RAW MATERIAL");
    expect(mine.textContent).toContain("Mine");
    expect(mine.querySelector(".supply-chain-amount")?.textContent).toBe("10");
    expect(mine.querySelector(".supply-chain-unit")?.textContent).toBe("t");
  });

  it("draws a group as a frame with its name, and a flow as a band as broad as its weight", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(elementOn(container, "north")!.querySelector(".supply-chain-group-frame")).not.toBeNull();
    expect(elementOn(container, "north")!.textContent).toContain("North");
    const heavy = connectionOn(container, "ore")!.querySelector<SVGPathElement>(".canvas-connection-line")!;
    const light = connectionOn(container, "goods")!.querySelector<SVGPathElement>(".canvas-connection-line")!;
    expect(Number.parseFloat(heavy.style.strokeWidth)).toBeGreaterThan(Number.parseFloat(light.style.strokeWidth));
    expect(connectionOn(container, "ore")!.querySelector(".supply-chain-flow-goods")).not.toBeNull();
    expect(connectionOn(container, "ore")!.querySelector(".supply-chain-flow-volume-text")?.textContent).toBe("6 t");
  });

  it("marks what the backend traced, and draws no stepper while nothing is selected", () => {
    // Arrange.
    currentModel = modelWith({ plant: "selected", mine: "upstream", ore: "upstream", shop: "downstream", goods: "downstream", north: "related" });

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(elementOn(container, "mine")!.classList.contains("supply-chain-trace-upstream")).toBe(true);
    expect(elementOn(container, "shop")!.classList.contains("supply-chain-trace-downstream")).toBe(true);
    expect(connectionOn(container, "ore")!.classList.contains("supply-chain-trace-upstream")).toBe(true);
    expect(elementOn(container, "plant::step-up")).not.toBeNull();

    // And beside that presence, the absence: untraced, nothing is marked and nothing steps.
    currentModel = modelWith();
    const quiet = renderCanvas().container;
    expect(quiet.querySelector('[class*="supply-chain-trace-"]')).toBeNull();
    expect(quiet.querySelector(".supply-chain-stepper")).toBeNull();
  });

  it("steps the selected node's quantity when its + is pressed, and leaves the selection alone", () => {
    // Arrange.
    currentModel = modelWith({ plant: "selected" });
    const { container } = renderCanvas();

    // Act.
    press(elementOn(container, "plant::step-up")!);
    press(elementOn(container, "plant::step-down")!);

    // Assert.
    expect(executed).toEqual([
      { actionId: SupplyChainActions.increase, targetId: "plant" },
      { actionId: SupplyChainActions.decrease, targetId: "plant" },
    ]);
    expect(idsPushed(selections)).toEqual([]);
  });

  it("offers no decrease below zero", () => {
    // Arrange.
    currentModel = modelWith({ plant: "selected" }, 0);
    const { container } = renderCanvas();

    // Act.
    press(elementOn(container, "plant::step-down")!);
    press(elementOn(container, "plant::step-up")!);

    // Assert: the up still works, so the down's silence is its own.
    expect(elementOn(container, "plant::step-down")!.classList.contains("supply-chain-stepper-disabled")).toBe(true);
    expect(executed).toEqual([{ actionId: SupplyChainActions.increase, targetId: "plant" }]);
  });

  it("steps a selected flow's volume the same way", () => {
    // Arrange.
    currentModel = modelWith({ goods: "selected" });
    const { container } = renderCanvas();

    // Act.
    press(elementOn(container, "goods::step-up")!);

    // Assert.
    expect(executed).toEqual([{ actionId: SupplyChainActions.increase, targetId: "goods" }]);
  });

  it("moves a group by its frame's top-left, which the backend turns into moving its members", () => {
    // Arrange.
    const { container } = renderCanvas();
    const frame = elementOn(container, "north")!;

    // Act.
    fireEvent(frame, pointer("pointerdown", { button: 0, clientX: 0, clientY: 0 }));
    fireEvent(frame, pointer("pointermove", { clientX: 40, clientY: 20 }));
    fireEvent(frame, pointer("pointerup", { clientX: 40, clientY: 20 }));

    // Assert.
    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe("north");
  });

  it("selects through the library, exactly as every other canvas does", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentSelectionKey = id === null ? null : `element:${id}`;
        return renderCanvas();
      },
      pushedIds: () => idsPushed(selections),
      element: "plant",
      connection: "ore",
    });
  });
});
