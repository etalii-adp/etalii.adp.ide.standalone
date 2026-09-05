import { describe, expect, it, vi, beforeEach } from "vitest";
import { render } from "@testing-library/react";
import { emptyModel, type DatabricksModel } from "./databricksModel";

let currentModel: DatabricksModel = emptyModel;

vi.mock("./useDatabricksStream", () => ({
  useDatabricksStream: () => ({
    model: currentModel,
    loading: false,
    failed: false,
    moveElementTo: () => Promise.resolve(""),
    reportView: () => undefined,
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  innermostKey: () => null,
  useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  useContextSelection: () => ({ selection: null, levels: [], actions: [] }),
  useContextConnection: () => ({
    select: () => undefined,
    executeAction: () => Promise.resolve({ accepted: true, error: "" }),
    executeShortcut: () => Promise.resolve({ accepted: true, error: "" }),
    setProperty: () => Promise.resolve({ accepted: true, error: "" }),
  }),
}));

vi.mock("@client/shell/panels/InlineLabelPlacementContext", () => ({
  useRegisterInlineLabelPlacement: () => undefined,
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

const { PipelineCanvas } = await import("./PipelineCanvas");

function node(id: string, kind: string, label: string, x: number, y: number, badges: string[] = []) {
  return { id, x, y, kind, label, badges, unresolved: false, runIf: "" };
}

function modelWith(): DatabricksModel {
  return {
    nodes: new Map([
      ["library:transformations/bronze", node("library:transformations/bronze", "source", "transformations/bronze", 0, 0, ["notebook"])],
      ["pipeline", node("pipeline", "pipeline", "bronze_to_gold", 320, 0, ["serverless", "CURRENT"])],
      ["target", node("target", "target", "lakehouse_dev.gold", 640, 0)],
      ["compute", node("compute", "compute", "serverless", 0, 200)],
      ["notifications", node("notifications", "notifications", "data-eng@example.com", 320, 200)],
    ]),
    frames: new Map(),
    edges: new Map([
      ["flow:transformations/bronze->pipeline", { id: "flow:transformations/bronze->pipeline", fromElementId: "library:transformations/bronze", toElementId: "pipeline", outcome: "", kind: "flow" as const }],
      ["flow:pipeline->target", { id: "flow:pipeline->target", fromElementId: "pipeline", toElementId: "target", outcome: "", kind: "flow" as const }],
    ]),
  };
}

function renderCanvas() {
  return render(<PipelineCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["pipeline.adp"]} />);
}

beforeEach(() => {
  currentModel = modelWith();
});

describe("the pipeline canvas", () => {
  it("draws the flow: sources into the pipeline into its target, satellites beneath", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelectorAll(".databricks-node")).toHaveLength(5);
    expect(container.querySelectorAll(".databricks-edge-flow")).toHaveLength(2);
    expect(container.textContent).toContain("bronze_to_gold");
    expect(container.textContent).toContain("lakehouse_dev.gold");
    expect(container.textContent).toContain("data-eng@example.com");
  });

  it("wears the pipeline's badges - serverless and the channel", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.textContent).toContain("serverless · CURRENT");
  });

  it("offers no dependency anchors: the flow is the file's structure", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelectorAll(".databricks-anchor-hit")).toHaveLength(0);
  });

  it("wears the shared canvas classes and draws the shared scrollbars", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".databricks-canvas")!.classList.contains("canvas-host")).toBe(true);
    expect(container.querySelector(".databricks-node-box")!.classList.contains("canvas-node")).toBe(true);
    expect(container.querySelector(".databricks-edge-flow")!.classList.contains("canvas-connection-line")).toBe(true);
    expect(container.querySelector(".databricks-scrollbars.canvas-scrollbar-horizontal")).not.toBeNull();
    expect(container.querySelector(".databricks-scrollbars.canvas-scrollbar-vertical")).not.toBeNull();
  });
});
