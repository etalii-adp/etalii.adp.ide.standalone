import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render, waitFor } from "@testing-library/react";
import { emptyModel, type DatabricksModel } from "./databricksModel";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";

let currentModel: DatabricksModel = emptyModel;
let moves: { elementId: string; x: number; y: number }[] = [];

let currentPrompt: unknown = null;
const submitLabel = vi.fn(async () => ({ accepted: true, error: "" }));

vi.mock("./useDatabricksStream", () => ({
  useDatabricksStream: () => ({
    model: currentModel,
    loading: false,
    failed: false,
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve("");
    },
    reportView: () => undefined,
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  innermostKey: () => null,
  useContextPrompt: () => ({ prompt: currentPrompt, onPropose: vi.fn(), onSubmit: submitLabel, onCancel: vi.fn() }),
  useContextSelection: () => ({ selection: null, levels: [], actions: [] }),
  useContextConnection: () => connection,
}));

const connection = fakeContextConnection();

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

const { BundleCanvas } = await import("./BundleCanvas");

function modelWith(): DatabricksModel {
  return {
    nodes: new Map([
      ["bundle", { id: "bundle", x: 0, y: 0, kind: "bundle", label: "lakehouse-nightly", badges: [], unresolved: false, runIf: "" }],
      ["resource:jobs/nightly_ingest", { id: "resource:jobs/nightly_ingest", x: 0, y: 180, kind: "jobs", label: "nightly_ingest", badges: ["jobs"], unresolved: false, runIf: "" }],
      ["unknown:sync", { id: "unknown:sync", x: 260, y: 180, kind: "sync", label: "sync", badges: ["sync"], unresolved: false, runIf: "" }],
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
  currentPrompt = null;
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
    const frame = container.querySelector('[data-element-id="target:prod"]')!;

    // Act.
    fireEvent(frame, new MouseEvent("pointerdown", { bubbles: true, button: 0, clientX: 100, clientY: 100 }));
    fireEvent(frame, new MouseEvent("pointermove", { bubbles: true, clientX: 180, clientY: 140 }));
    fireEvent(frame, new MouseEvent("pointerup", { bubbles: true, clientX: 180, clientY: 140 }));

    // Assert: the authored corner, converted back with the FRAME's own size - jsdom's
    // zero-size rect makes one pixel one unit, so the delta lands verbatim.
    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe("target:prod");
    expect(moves[0].x).toBeCloseTo(80, 5);
    expect(moves[0].y).toBeCloseTo(440, 5);
  });

  it("offers no dependency anchors: overrides are written in the file, not drawn", () => {
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
    expect(container.querySelector(".databricks-override-line")!.classList.contains("canvas-connection-line")).toBe(true);
    expect(container.querySelector(".databricks-scrollbars.canvas-scrollbar-horizontal")).not.toBeNull();
    expect(container.querySelector(".databricks-scrollbars.canvas-scrollbar-vertical")).not.toBeNull();
  });

  // ---- inline renaming -----------------------------------------------------------------

  it("opens an editor over the bundle, whose drawn key IS the name being renamed", async () => {
    // Arrange.
    // This is the case task 4 said to verify rather than assume. The bundle element is packed
    // by the mapper with Key = bundle.Name, so `payload.key || payload.kind` renders the very
    // value rename-bundle asks for; the fallback is an empty-state placeholder, not a second
    // value. Had the name been drawn on a frame, or been a projection, this would not qualify.
    currentPrompt = {
      prompt: {
        case: "inputDialog",
        value: {
          title: "Rename bundle",
          icon: "mdi-pencil-outline",
          fieldLabel: "Name",
          initialValue: "lakehouse-nightly",
          confirmLabel: "Rename",
          inlineLabelEdit: { elementId: { value: "bundle" } },
        },
      },
    };

    // Act.
    const { container } = render(<BundleCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["bundle.adp"]} />);

    // Assert: the editor is drawn, opens on the drawn name, and covers the bundle's own box.
    const field = container.querySelector("input.inline-label-editor-field") as HTMLInputElement;
    expect(field).not.toBeNull();
    expect(field.value).toBe("lakehouse-nightly");

    const rect = container.querySelector('[data-element-id="bundle"] rect') as SVGRectElement;
    const box = container.querySelector("foreignObject.inline-label-editor") as SVGForeignObjectElement;
    expect(Number(box.getAttribute("width"))).toBeCloseTo(Number(rect.getAttribute("width")), 5);

    fireEvent.change(field, { target: { value: "lakehouse-daily" } });
    fireEvent.keyDown(field, { key: "Enter" });
    await waitFor(() => expect(submitLabel).toHaveBeenCalledWith("lakehouse-daily"));
  });

  it("places no editor over a deployment target, which is a frame and not renamed", () => {
    // Arrange.
    currentPrompt = {
      prompt: {
        case: "inputDialog",
        value: { title: "x", icon: "x", fieldLabel: "x", initialValue: "prod", confirmLabel: "x", inlineLabelEdit: { elementId: { value: "target:prod" } } },
      },
    };

    // Act.
    const { container } = render(<BundleCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["bundle.adp"]} />);

    // Assert.
    expect(container.querySelector("foreignObject.inline-label-editor")).toBeNull();
  });
});
