import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { emptyModel, type DatabricksModel } from "./databricksModel";

let currentModel: DatabricksModel = emptyModel;
let moves: { elementId: string; x: number; y: number }[] = [];

vi.mock("./useDatabricksStream", () => ({
  useDatabricksStream: () => ({
    model: currentModel,
    loading: false,
    failed: false,
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve("");
    },
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  innermostKey: () => null,
  useContextSelection: () => ({ selection: null, levels: [], actions: [] }),
  useContextConnection: () => ({
    select: () => undefined,
    executeAction: () => Promise.resolve({ accepted: true, error: "" }),
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

const { BundleCanvas } = await import("./BundleCanvas");

function modelWith(): DatabricksModel {
  return {
    nodes: new Map([
      ["bundle", { id: "bundle", x: 0, y: 0, kind: "bundle", label: "lakehouse-nightly", badges: [], unresolved: false }],
      ["resource:jobs/nightly_ingest", { id: "resource:jobs/nightly_ingest", x: 0, y: 180, kind: "jobs", label: "nightly_ingest", badges: ["jobs"], unresolved: false }],
      ["unknown:sync", { id: "unknown:sync", x: 260, y: 180, kind: "sync", label: "sync", badges: ["sync"], unresolved: false }],
    ]),
    frames: new Map([
      ["target:prod", { id: "target:prod", x: 0, y: 400, label: "prod", mode: "production", isDefault: false, overrideCount: 1 }],
      ["target:dev", { id: "target:dev", x: 260, y: 400, label: "dev", mode: "development", isDefault: true, overrideCount: 0 }],
    ]),
    edges: new Map([
      ["override:prod/jobs/nightly_ingest", { id: "override:prod/jobs/nightly_ingest", fromElementId: "target:prod", toElementId: "resource:jobs/nightly_ingest", outcome: "", kind: "override" as const }],
    ]),
  };
}

function renderCanvas() {
  return render(<BundleCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["bundle.adp"]} />);
}

beforeEach(() => {
  currentModel = modelWith();
  moves = [];
});

describe("the bundle canvas", () => {
  it("draws the bundle, its resources - unknown kinds included - and the target frames", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelectorAll(".databricks-node")).toHaveLength(3);
    expect(container.querySelectorAll(".databricks-frame")).toHaveLength(2);
    expect(container.textContent).toContain("lakehouse-nightly");
    // An unmodelled construct draws generically rather than vanishing (Requirement 2.4).
    expect(container.textContent).toContain("sync");
  });

  it("draws the override as a straight edge from the target frame to the resource (Requirement 3.3)", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelectorAll(".databricks-override-line")).toHaveLength(1);
  });

  it("wears mode, default and the override count on the frame", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.textContent).toContain("production · 1 override");
    expect(container.textContent).toContain("development · default");
  });

  it("repositions a target frame through the layout path, like any node", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".databricks-surface")!;
    const frame = container.querySelector('[data-element-id="target:prod"]')!;

    // Act.
    fireEvent.mouseDown(frame, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 180, clientY: 140 });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe("target:prod");
  });

  it("offers no dependency anchors: overrides are written in the file, not drawn", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelectorAll(".databricks-anchor-hit")).toHaveLength(0);
  });
});
