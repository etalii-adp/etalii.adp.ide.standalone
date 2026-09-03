import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { emptyModel, type DatabricksModel } from "./databricksModel";

let currentModel: DatabricksModel = emptyModel;
let currentLoading = false;
let currentFailed = false;
let currentSelectionKey: string | null = null;
let currentActions: unknown[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];
let selections: unknown[] = [];
let executed: { actionId: string; source: unknown }[] = [];

vi.mock("./useDatabricksStream", () => ({
  useDatabricksStream: () => ({
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
    executeShortcut: () => Promise.resolve({ accepted: true, error: "" }),
    setProperty: () => Promise.resolve({ accepted: true, error: "" }),
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

const { JobCanvas } = await import("./JobCanvas");

function task(id: string, label: string, x: number, y: number, badges: string[] = [], unresolved = false, kind = "notebook") {
  return { id, x, y, kind, label, badges, unresolved };
}

function modelWith(): DatabricksModel {
  return {
    nodes: new Map([
      ["task:ingest", task("task:ingest", "ingest", 0, 0, ["ingest_cluster"])],
      ["task:publish", task("task:publish", "publish", 520, 0, ["serverless"])],
      ["task:gone", task("task:gone", "gone", 260, 200, [], true)],
      ["cluster:ingest_cluster", { id: "cluster:ingest_cluster", x: 0, y: 400, kind: "compute", label: "ingest_cluster", badges: ["2 workers"], unresolved: false }],
    ]),
    frames: new Map(),
    edges: new Map([
      ["edge:ingest->publish", { id: "edge:ingest->publish", fromElementId: "task:ingest", toElementId: "task:publish", outcome: "true", kind: "depends" as const }],
    ]),
  };
}

function renderCanvas() {
  return render(<JobCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["job.adp"]} />);
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
});

describe("the job canvas", () => {
  it("draws the tasks, the cluster and the directed dependency between them", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelectorAll(".databricks-node")).toHaveLength(4);
    expect(container.querySelectorAll(".databricks-edge")).toHaveLength(1);
    expect(container.querySelector("marker#databricks-arrowhead")).not.toBeNull();
    expect(container.textContent).toContain("ingest");
    expect(container.textContent).toContain("2 workers");
  });

  it("wears the outcome on the edge, in class and label (Requirement 4.3)", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".databricks-edge-outcome-true")).not.toBeNull();
    expect(container.querySelector(".databricks-outcome-label")!.textContent).toBe("true");
  });

  it("marks a depends_on stub as missing rather than dropping it (Requirement 4.5)", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    const stub = container.querySelector(".databricks-node-missing");
    expect(stub).not.toBeNull();
    expect(stub!.textContent).toContain("gone");
  });

  it("treats a motionless press as a selection, never an edit", () => {
    // Arrange.
    const { container } = renderCanvas();
    const element = container.querySelector('[data-element-id="task:ingest"]')!;

    // Act.
    fireEvent.mouseDown(element, { clientX: 100, clientY: 100 });
    fireEvent.mouseUp(container.querySelector(".databricks-surface")!);

    // Assert.
    expect(moves).toHaveLength(0);
    expect(selections).toHaveLength(1);
  });

  it("commits a drag as one move in raw module coordinates - the layout path, never a grid", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".databricks-surface")!;
    const element = container.querySelector('[data-element-id="task:ingest"]')!;
    const pixelsPerUnit = Number(container.querySelector(".databricks-node-box")!.getAttribute("width")) / 200;

    // Act: an odd, fractional distance.
    fireEvent.mouseDown(element, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 137.5, clientY: 112 });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe("task:ingest");
    expect(moves[0].x).toBeCloseTo(37.5 / pixelsPerUnit, 10);
    expect(moves[0].y).toBeCloseTo(12 / pixelsPerUnit, 10);
  });

  it("abandons a drag on Escape with nothing dispatched", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".databricks-surface")!;
    const element = container.querySelector('[data-element-id="task:ingest"]')!;

    // Act.
    fireEvent.mouseDown(element, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 300, clientY: 300 });
    fireEvent.keyDown(window, { key: "Escape" });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(moves).toHaveLength(0);
  });

  it("relates two tasks in one stateless rel: call from the anchor drag", () => {
    // Arrange: anchors render on the selected task only.
    currentSelectionKey = "element:task:ingest";
    const { container } = renderCanvas();
    const anchor = container.querySelectorAll(".databricks-anchor-hit")[1];
    const target = container.querySelector('[data-element-id="task:publish"]')!;

    // Act.
    fireEvent.mouseDown(anchor, { clientX: 100, clientY: 30 });
    fireEvent.mouseUp(target);

    // Assert.
    const calls = executed.filter((call) => call.actionId === "databricks.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toBe("rel:task:ingest->task:publish");
  });

  it("a release on empty canvas is a never-mind, not a placement gesture", () => {
    // Arrange.
    // This family creates tasks by drop, not by relation-to-empty-space.
    currentSelectionKey = "element:task:ingest";
    const { container } = renderCanvas();
    const anchor = container.querySelectorAll(".databricks-anchor-hit")[0];
    const surface = container.querySelector(".databricks-surface")!;

    // Act.
    fireEvent.mouseDown(anchor, { clientX: 100, clientY: 30 });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(executed.filter((call) => call.actionId === "databricks.connect")).toHaveLength(0);
  });

  it("lands a toolbox drop as a new: placement action, with nothing asked", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".databricks-surface")!;
    const data = new Map([["application/x-adp-toolbox-item", "databricks.add-task:notebook"]]);

    // Act.
    fireEvent.drop(surface, {
      dataTransfer: { getData: (type: string) => data.get(type) ?? "", types: [...data.keys()] },
    });

    // Assert.
    expect(executed).toHaveLength(1);
    expect(executed[0].actionId).toBe("databricks.add-task:notebook");
    const source = executed[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^new:/);
  });

  it("shows the unavailable state when the backend answered permanently", () => {
    // Arrange.
    currentFailed = true;

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.textContent).toContain("could not be opened");
  });
});
