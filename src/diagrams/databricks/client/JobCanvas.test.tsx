import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render, waitFor } from "@testing-library/react";
import { emptyModel, type DatabricksModel } from "./databricksModel";
import { fakeContextConnection, idsPushed, pointer } from "@client/canvas/library/testing/canvasHarness";

let currentModel: DatabricksModel = emptyModel;
let currentLoading = false;
let currentFailed = false;
let currentSelectionKey: string | null = null;
let currentActions: unknown[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];
let selections: unknown[] = [];
let currentPrompt: unknown = null;
const proposeLabel = vi.fn(async () => ({ accepted: true, error: "" }));
const submitLabel = vi.fn(async () => ({ accepted: true, error: "" }));
const cancelLabel = vi.fn();
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
    reportView: () => undefined,
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
});

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

const { JobCanvas } = await import("./JobCanvas");
const { expectLibrarySelection } = await import("@client/canvas/library/testing/expectLibrarySelection");

function task(id: string, label: string, x: number, y: number, badges: string[] = [], unresolved = false, kind = "notebook") {
  return { id, x, y, kind, label, badges, unresolved, runIf: "" };
}

function modelWith(): DatabricksModel {
  return {
    nodes: new Map([
      ["task:ingest", task("task:ingest", "ingest", 0, 0, ["ingest_cluster"])],
      ["task:publish", task("task:publish", "publish", 520, 0, ["serverless"])],
      ["task:gone", task("task:gone", "gone", 260, 200, [], true)],
      ["cluster:ingest_cluster", { id: "cluster:ingest_cluster", x: 0, y: 400, kind: "compute", label: "ingest_cluster", badges: ["2 workers"], unresolved: false, runIf: "" }],
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
  currentPrompt = null;
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
    expect(container.querySelector("marker#library-arrow")).not.toBeNull();
    expect(container.textContent).toContain("ingest");
    expect(container.textContent).toContain("2 workers");
  });

  it("wears the outcome on the edge, in class and label (Requirement 4.3)", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".databricks-edge-outcome-true")).not.toBeNull();
    expect(container.querySelector(".databricks-edge text.library-connection-label")!.textContent).toBe("true");
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
    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(element, pointer("pointerup", { clientX: 100, clientY: 100 }));

    // Assert.
    expect(moves).toHaveLength(0);
    expect(selections).toHaveLength(1);
  });

  it("selects an edge on a press - this family's edges were pending, not exempt", () => {
    // This used to assert that an edge press sent nothing. The label library's readme records
    // databricks' edges as "pending, not exempt", and centralized-selection Requirement 2.1 makes
    // a connection selectable wherever Requirement 2.4 does not exempt it - so the Assert changes,
    // and the Arrange and Act are as they were. The backend has always resolved an edge id.
    const { container } = renderCanvas();
    const edge = container.querySelector('[data-connection-id="edge:ingest->publish"]')!;

    // Act.
    fireEvent(edge, pointer("pointerdown", { button: 0, clientX: 300, clientY: 28 }));
    fireEvent(edge, pointer("pointerup", { clientX: 300, clientY: 28 }));

    // Assert: the edge's own id travels, as a connection's does on every canvas.
    expect(idsPushed(selections)).toEqual(["edge:ingest->publish"]);
  });

  it("commits a drag as one move in raw module coordinates - the layout path, never a grid", () => {
    // Arrange.
    const { container } = renderCanvas();
    const element = container.querySelector('[data-element-id="task:ingest"]')!;
    const pixelsPerUnit = Number(container.querySelector(".databricks-node-box")!.getAttribute("width")) / 200;

    // Act: an odd, fractional distance.
    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(element, pointer("pointermove", { clientX: 137.5, clientY: 112 }));
    fireEvent(element, pointer("pointerup", { clientX: 137.5, clientY: 112 }));

    // Assert.
    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe("task:ingest");
    expect(moves[0].x).toBeCloseTo(37.5 / pixelsPerUnit, 10);
    expect(moves[0].y).toBeCloseTo(12 / pixelsPerUnit, 10);
  });

  it("abandons a drag on Escape with nothing dispatched", () => {
    // Arrange.
    const { container } = renderCanvas();
    const element = container.querySelector('[data-element-id="task:ingest"]')!;

    // Act.
    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(element, pointer("pointermove", { clientX: 300, clientY: 300 }));
    fireEvent.keyDown(container.querySelector("svg.library-canvas-surface")!, { key: "Escape" });
    fireEvent(element, pointer("pointerup", { clientX: 300, clientY: 300 }));

    // Assert.
    expect(moves).toHaveLength(0);
  });

  it("relates two tasks in one stateless rel: call from the anchor drag", () => {
    // Arrange: the library renders the named anchors always, shown by the stylesheet
    // when they matter.
    const { container } = renderCanvas();
    const anchor = container.querySelector('[data-element-id="task:ingest"] [data-anchor="right"]')!;

    // Act: drag to publish's centre - jsdom's zero-size rect makes one pixel one unit.
    const overPublish = { clientX: 620, clientY: 28 };
    fireEvent(anchor, pointer("pointerdown", { button: 0, clientX: 200, clientY: 28 }));
    fireEvent(anchor, pointer("pointermove", { ...overPublish }));
    fireEvent(anchor, pointer("pointerup", { ...overPublish }));

    // Assert.
    const calls = executed.filter((call) => call.actionId === "databricks.connect");
    expect(calls).toHaveLength(1);
    const source = calls[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toBe("rel:task:ingest->task:publish");
  });

  it("a release on empty canvas is a never-mind, not a placement gesture", () => {
    // Arrange.
    // This family creates tasks by drop, not by relation-to-empty-space.
    const { container } = renderCanvas();
    const anchor = container.querySelector('[data-element-id="task:ingest"] [data-anchor="left"]')!;

    // Act.
    fireEvent(anchor, pointer("pointerdown", { button: 0, clientX: 0, clientY: 28 }));
    fireEvent(anchor, pointer("pointermove", { clientX: 900, clientY: 300 }));
    fireEvent(anchor, pointer("pointerup", { clientX: 900, clientY: 300 }));

    // Assert.
    expect(executed.filter((call) => call.actionId === "databricks.connect")).toHaveLength(0);
  });

  it("lands a toolbox drop as a new: placement action, with nothing asked", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const box = surface.getAttribute("viewBox")!.split(" ").map(Number);
    Object.defineProperty(surface, "getBoundingClientRect", {
      value: () => ({ x: 0, y: 0, top: 0, left: 0, right: box[2], bottom: box[3], width: box[2], height: box[3], toJSON: () => ({}) }),
    });

    // Act.
    const event = new MouseEvent("drop", { bubbles: true, cancelable: true, clientX: 100, clientY: 100 });
    Object.defineProperty(event, "dataTransfer", {
      value: { getData: (type: string) => (type === "application/x-adp-toolbox-item" ? "databricks.add-task:notebook" : ""), types: ["application/x-adp-toolbox-item"] },
    });
    fireEvent(surface, event);

    // Assert.
    expect(executed).toHaveLength(1);
    expect(executed[0].actionId).toBe("databricks.add-task:notebook");
    const source = executed[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^new:/);
  });

  it("leaves the unavailable state to the library's frame rather than saying it itself", () => {
    // Arrange: client-centralization Requirement 2.3 - one appearance, drawn by the library.
    currentFailed = true;

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.textContent).not.toContain("could not be opened");
  });

  it("wears the shared canvas classes, so the central stylesheet is what dresses it", () => {
    // Arrange & act.
    // The appearance lives in @client/canvas/canvas.css (loaded by register.ts); what a canvas
    // owns is composing the shared class names. A node missing canvas-node here would render
    // unstyled however correct the stylesheet is.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".databricks-canvas")!.classList.contains("canvas-host")).toBe(true);
    expect(container.querySelector(".databricks-surface")!.classList.contains("library-canvas")).toBe(true);
    expect(container.querySelector("svg.library-canvas-surface")).not.toBeNull();
    expect(container.querySelector(".databricks-node-box")!.classList.contains("canvas-node")).toBe(true);
    expect(container.querySelector(".databricks-label")!.classList.contains("canvas-node-label")).toBe(true);
    expect(container.querySelector(".databricks-edge .canvas-connection-line")).not.toBeNull();
    expect(container.querySelector("marker#library-arrow path")!.classList.contains("canvas-arrowhead")).toBe(true);
  });

  it("starts a task's name at the box's left inset, as before the migration", () => {
    // The job's tasks are the `task` element type, a separate declaration from the pipeline's
    // `node` one, so each canvas guards its own - PipelineCanvas.test.tsx holds the other.
    // Act.
    const { container } = renderCanvas();

    // Assert.
    const name = container.querySelector<SVGTextElement>('[data-element-id="task:publish"] text.databricks-label')!;
    expect(name.style.getPropertyValue("text-anchor")).toBe("start");
    // The publish box's left edge is at 520 in modelWith(); centred, the name would sit at 620.
    expect(name.getAttribute("x")).toBe(String(520 + 8));
  });

  it("draws the shared scrollbars, and dragging the horizontal thumb pans", () => {
    // Arrange.
    const { container } = renderCanvas();
    const bar = container.querySelector(".databricks-scrollbars.canvas-scrollbar-horizontal")!;
    Object.defineProperty(bar, "getBoundingClientRect", {
      value: () => ({ x: 0, y: 0, top: 0, left: 0, right: 200, bottom: 10, width: 200, height: 10, toJSON: () => ({}) }),
    });
    const thumb = bar.querySelector(".canvas-scrollbar-thumb")!;
    const svg = container.querySelector("svg.library-canvas-surface")!;
    const before = svg.getAttribute("viewBox");

    // Act.
    fireEvent.mouseDown(thumb, { clientX: 100, clientY: 300 });
    fireEvent.mouseMove(window, { clientX: 180, clientY: 300 });
    fireEvent.mouseUp(window);

    // Assert: panning moves the viewBox now, not each node's own transform.
    expect(container.querySelector(".databricks-scrollbars.canvas-scrollbar-vertical")).not.toBeNull();
    expect(svg.getAttribute("viewBox")).not.toBe(before);
  });

  it("intercepts a simulated action id: the show plays locally and executeAction is never called", () => {
    // Arrange.
    // The Requirement 8.6 seam: the id is exactly what the backend discovers, the marker is
    // what the canvas intercepts on - and nothing may reach the history (Requirement 11.6).
    const { container } = renderCanvas();
    const surface = container.querySelector("svg.library-canvas-surface")!;

    // Act.
    const event = new MouseEvent("drop", { bubbles: true, cancelable: true, clientX: 100, clientY: 100 });
    Object.defineProperty(event, "dataTransfer", {
      value: { getData: (type: string) => (type === "application/x-adp-toolbox-item" ? "databricks.simulated.run-job" : ""), types: ["application/x-adp-toolbox-item"] },
    });
    fireEvent(surface, event);

    // Assert.
    expect(executed).toHaveLength(0);
    expect(container.querySelector(".databricks-simulation-banner")!.textContent).toContain("Simulated");
    // The show marks the tasks; the unmarked drop test above proves ordinary ids still travel.
    expect(container.querySelector('[class*="databricks-sim-"]')).not.toBeNull();
  });

  // ---- inline renaming -----------------------------------------------------------------

  function labelPromptFor(elementId: string, text: string): unknown {
    return {
      prompt: {
        case: "inputDialog",
        value: {
          title: "Rename task",
          icon: "mdi-pencil-outline",
          fieldLabel: "Key",
          initialValue: text,
          confirmLabel: "Rename",
          inlineLabelEdit: { elementId: { value: elementId } },
        },
      },
    };
  }

  function editorBox(container: HTMLElement): SVGForeignObjectElement {
    return container.querySelector("foreignObject.inline-label-editor") as SVGForeignObjectElement;
  }

  function labelField(container: HTMLElement): HTMLInputElement {
    return container.querySelector("input.inline-label-editor-field") as HTMLInputElement;
  }

  it("opens an editor over a task, and submits what is typed into it", async () => {
    // Arrange. The task's key IS its drawn label, which is why renaming it qualifies.
    currentPrompt = labelPromptFor("task:publish", "publish");

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(editorBox(container)).not.toBeNull();
    expect(labelField(container).value).toBe("publish");

    fireEvent.change(labelField(container), { target: { value: "published" } });
    fireEvent.keyDown(labelField(container), { key: "Enter" });
    await waitFor(() => expect(submitLabel).toHaveBeenCalledWith("published"));
  });

  it("covers the task's own box, so the editor is where the label is drawn", () => {
    // Arrange.
    currentPrompt = labelPromptFor("task:publish", "publish");

    // Act.
    const { container } = renderCanvas();

    // Assert.
    const rect = container.querySelector('[data-element-id="task:publish"] rect') as SVGRectElement;
    expect(rect).not.toBeNull();
    const box = editorBox(container);
    expect(Number(box.getAttribute("width"))).toBeCloseTo(Number(rect.getAttribute("width")), 5);
    expect(Number(box.getAttribute("height"))).toBeCloseTo(Number(rect.getAttribute("height")), 5);
  });

  it("places no editor for an element the canvas cannot draw", () => {
    // Arrange. An edge cannot be selected on this canvas, so nothing can reach one - and a
    // resolver that answered for one would put an editor where there is no label.
    currentPrompt = labelPromptFor("edge:ingest->publish", "depends");

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(editorBox(container)).toBeNull();
  });

  it("leaves the selection alone when an inline edit commits", async () => {
    // Arrange.
    currentSelectionKey = "task:publish";
    currentPrompt = labelPromptFor("task:publish", "publish");
    const { container } = renderCanvas();
    selections = [];

    // Act.
    fireEvent.change(labelField(container), { target: { value: "published" } });
    fireEvent.keyDown(labelField(container), { key: "Enter" });

    // Assert.
    await waitFor(() => expect(submitLabel).toHaveBeenCalled());
    expect(selections).toEqual([]);
  });
});

describe("selection, as every canvas has it", () => {
  it("highlights a pushed task and edge, and clears on a background press (centralized-selection 9.2)", () => {
    // Databricks' three readings wrap one canvas; this is where that canvas meets the assertion.
    expectLibrarySelection({
      mountWith: (id) => {
        currentModel = modelWith();
        currentLoading = false;
        currentFailed = false;
        currentSelectionKey = id === null ? null : `element:${id}`;
        return renderCanvas();
      },
      pushedIds: () => idsPushed(selections),
      element: "task:ingest",
      connection: "edge:ingest->publish",
    });
  });

  it("plays a simulated run chosen from the shared menu, and calls no executeAction (Requirements 8.6, 11.6)", async () => {
    // The backend offers the simulated entry; the canvas declares it a menu entry it runs itself,
    // so the library hands it over and sends nothing. A miss would be silent - the backend
    // no-ops a simulated id that reaches it - which is why this asserts executeAction's absence.
    currentSelectionKey = "element:task:ingest";
    currentActions = [
      { actions: [{ id: "databricks.simulated.run-job", label: "Run job (simulated)", icon: "", available: true, unavailableReason: "", items: [] }] },
    ];
    const { container } = renderCanvas();

    fireEvent.contextMenu(container.querySelector('[data-element-id="task:ingest"]')!, { clientX: 100, clientY: 100 });
    const entry = await waitFor(() => {
      const found = [...document.querySelectorAll("button")].find((candidate) => candidate.textContent?.includes("Run job (simulated)"));
      expect(found).toBeDefined();
      return found!;
    });
    fireEvent.click(entry);

    expect(executed).toHaveLength(0);
    expect(container.querySelector(".databricks-simulation-banner")!.textContent).toContain("Simulated");
  });
});
