import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, fireEvent, waitFor, act } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import { ProblemSchema, ProblemSeverity } from "@client/generated/problems_pb";
import {
  PipelineElementKindProto,
  PipelineElementPayloadSchema,
} from "@client/generated/azure-pipeline_pb";
import {
  applyDelta,
  emptyModel,
  EDGE_TYPE,
  JOB_TYPE,
  STAGE_TYPE,
  TEMPLATE_TYPE,
  type PipelineModel,
} from "./pipelineModel";
import { ToolboxItemSchema, type ToolboxItem } from "@client/generated/diagrams_pb";
import { DiagramToolboxProvider, useDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";
import { fakeContextConnection, idsPushed, pointer } from "@client/canvas/library/testing/canvasHarness";

const select = vi.fn();
const executeShortcut = vi.fn(async () => ({ accepted: true, error: "" }));
const submitLabel = vi.fn(async () => ({ accepted: true, error: "" }));
let currentSelectionKey: string | null = null;
let currentPrompt: unknown = null;
let currentModel: PipelineModel = emptyModel;
let currentLoading = false;
let currentFailed = false;

vi.mock("./usePipelineStream", () => ({
  usePipelineStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    reportView: () => undefined,
  }),
}));

let currentProblems: { problems: unknown[] } | null = null;

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => connection,
    useContextSelection: () => ({ selection: currentSelectionKey, actions: [] }),
    innermostKey: () => currentSelectionKey,
    useContextPrompt: () => ({ prompt: currentPrompt, onPropose: vi.fn(async () => ({ accepted: true, error: "" })), onSubmit: submitLabel, onCancel: vi.fn() }),
    useContextProblems: () => currentProblems,
  };
});

const connection = fakeContextConnection({ select, executeShortcut });

let currentToolboxItems: ToolboxItem[] = [];
let toolboxRequests: (readonly string[])[] = [];

vi.mock("@client/shell/panels/InlineLabelPlacementContext", () => ({
  useRegisterInlineLabelPlacement: () => undefined,
}));

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: (_projectId: Uint8Array, path: readonly string[]) => {
    toolboxRequests.push(path);
    return currentToolboxItems;
  },
}));

const { PipelineCanvas } = await import("./PipelineCanvas");

function element(id: string, type: string, payload: Uint8Array, x = 0, y = 0) {
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type,
    payload: { typeUrl: `type.googleapis.com/${type}`, value: payload },
  });
}

function payload(extra: Record<string, unknown>) {
  return toBinary(
    PipelineElementPayloadSchema,
    create(PipelineElementPayloadSchema, { enabled: true, multiplicity: 1, width: 200, height: 80, ...extra }),
  );
}

function stage(id: string, displayName: string, extra: Record<string, unknown> = {}, x = 0, y = 0) {
  return element(
    id,
    STAGE_TYPE,
    payload({ name: id, displayName, kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_STAGE, ...extra }),
    x,
    y,
  );
}

function job(id: string, parentId: string, displayName: string, extra: Record<string, unknown> = {}, x = 0, y = 0) {
  return element(
    id,
    JOB_TYPE,
    payload({
      name: displayName,
      displayName,
      parentId,
      kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_JOB,
      ...extra,
    }),
    x,
    y,
  );
}

function edge(id: string, sourceId: string, targetId: string, extra: Record<string, unknown> = {}) {
  return element(
    id,
    EDGE_TYPE,
    payload({ kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_EDGE, sourceId, targetId, ...extra }),
  );
}

function add(...elements: ReturnType<typeof element>[]) {
  return { action: { case: "add", value: { elements } } } as never;
}

function group(groupId: string, ...sourceIds: string[]) {
  return {
    action: {
      case: "group",
      value: {
        sourceElementIds: sourceIds.map((value) => ({ value })),
        groupElement: create(ElementSchema, { id: { value: groupId } }),
      },
    },
  } as never;
}

function draw() {
  return render(
    <PipelineCanvas projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={["azure-pipelines.yml"]} />,
  );
}

/** A click in the pointer vocabulary the surface listens to: press and release, unmoved. */
function press(target: Element, init: MouseEventInit = {}) {
  fireEvent(target, pointer("pointerdown", { button: 0, ...init }));
  fireEvent(target, pointer("pointerup", { ...init }));
}

describe("PipelineCanvas", () => {
  beforeEach(() => {
    currentModel = emptyModel;
    currentSelectionKey = null;
    currentPrompt = null;
    executeShortcut.mockClear();
    currentLoading = false;
    currentFailed = false;
    currentProblems = null;
    select.mockClear();
  });

  it("draws the stages the backend sent", () => {
    // Arrange.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build the solution"), stage("Test", "Test")));

    // Act.
    const view = draw();

    // Assert.
    expect(view.getByTestId("stage-Build")).toBeTruthy();
    expect(view.getByText("Build the solution")).toBeTruthy();
  });

  it("shows a collapsed stage's job count rather than its jobs", () => {
    // Arrange: Requirement 8.2 - a pipeline read at three levels at once is unreadable, so a
    // closed stage stands for what it holds.
    currentModel = applyDelta(
      applyDelta(emptyModel, add(stage("Build", "Build", { jobCount: 3 }), job("Build/Compile", "Build", "Compile"))),
      group("Build", "Build/Compile"),
    );

    // Act.
    const view = draw();

    // Assert.
    expect(view.getByText("3 jobs")).toBeTruthy();
    expect(view.queryByTestId("node-Build/Compile")).toBeNull();
    expect(view.getByTestId("stage-Build").getAttribute("data-expanded")).toBe("false");
  });

  it("shows the jobs of an open stage", () => {
    // Arrange.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build"), job("Build/Compile", "Build", "Compile")));

    // Act.
    const view = draw();

    // Assert.
    expect(view.getByTestId("node-Build/Compile")).toBeTruthy();
    expect(view.getByTestId("stage-Build").getAttribute("data-expanded")).toBe("true");
  });

  it("draws a deployment job unlike a plain one", () => {
    // Arrange: Requirement 8.3 - it targets an environment and runs lifecycle hooks, which is a
    // different thing to reason about and so a different thing to look at.
    currentModel = applyDelta(
      emptyModel,
      add(
        stage("Deploy", "Deploy"),
        job("Deploy/Staging", "Deploy", "Staging", {
          kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_DEPLOYMENT_JOB,
          environment: "staging",
        }),
        job("Deploy/Notify", "Deploy", "Notify"),
      ),
    );

    // Act.
    const view = draw();

    // Assert.
    expect(view.getByTestId("node-Deploy/Staging").getAttribute("class")).toContain("pipeline-deployment");
    expect(view.getByTestId("node-Deploy/Notify").getAttribute("class")).not.toContain("pipeline-deployment");
  });

  it("marks an element that came from a template", () => {
    // Arrange: Requirement 8.5 - authored-here has to be tellable from included-from-there,
    // because only one of them can be edited on this canvas.
    currentModel = applyDelta(
      emptyModel,
      add(
        stage("Build", "Build"),
        job("Build/Compile", "Build", "Compile", { fromTemplate: true, templatePath: "templates/build.yml" }),
      ),
    );

    // Act.
    const view = draw();

    // Assert.
    expect(view.getByTestId("node-Build/Compile").getAttribute("class")).toContain("pipeline-from-template");
  });

  it("marks a template it could not follow, and says why on hover", () => {
    // Arrange.
    currentModel = applyDelta(
      emptyModel,
      add(
        element(
          "template:0",
          TEMPLATE_TYPE,
          payload({
            kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_TEMPLATE,
            displayName: "shared.yml@other",
            unresolvedReason: "It comes from the 'other' repository resource, which is not checked out here.",
          }),
        ),
      ),
    );

    // Act.
    const view = draw();

    // Assert.
    expect(view.getByTestId("node-template:0").getAttribute("class")).toContain("pipeline-template");
    expect(view.getByText(/not checked out here/)).toBeTruthy();
  });

  it("draws an arrow between two stages", () => {
    // Arrange.
    currentModel = applyDelta(
      emptyModel,
      add(stage("Build", "Build", {}, 0, 0), stage("Test", "Test", {}, 300, 0), edge("edge:Build->Test", "Build", "Test")),
    );

    // Act.
    const view = draw();

    // Assert.
    expect(view.container.querySelector('[data-connection-id="edge:Build->Test"]')).not.toBeNull();
  });

  it("draws an implicit arrow differently from one somebody wrote down", () => {
    // Arrange: the single most easily missed thing about an Azure pipeline is the arrow nobody
    // wrote, so it must not look identical to one that was written.
    currentModel = applyDelta(
      emptyModel,
      add(
        stage("Build", "Build", {}, 0, 0),
        stage("Test", "Test", {}, 300, 0),
        edge("implicit", "Build", "Test", { implicitDependency: true }),
      ),
    );

    // Act.
    const view = draw();

    // Assert.
    expect(view.container.querySelector('[data-connection-id="implicit"]')!.getAttribute("class")).toContain("pipeline-edge-implicit");
  });

  it("draws a broken arrow as broken", () => {
    // Arrange: a dangling dependsOn is the mistake this diagram exists to catch.
    currentModel = applyDelta(
      emptyModel,
      add(
        stage("Ghost", "Ghost", {}, 300, 0),
        stage("Build", "Build", {}, 0, 0),
        edge("broken", "Build", "Ghost", { broken: true }),
      ),
    );

    // Act.
    const view = draw();

    // Assert.
    expect(view.container.querySelector('[data-connection-id="broken"]')!.getAttribute("class")).toContain("pipeline-edge-broken");
  });

  it("draws no arrow when one of its ends is not on the canvas", () => {
    // Arrange: a line into empty space says less than no line at all.
    currentModel = applyDelta(emptyModel, add(stage("Test", "Test"), edge("dangling", "Build", "Test")));

    // Act.
    const view = draw();

    // Assert.
    expect(view.container.querySelector('[data-connection-id="dangling"]')).toBeNull();
  });

  it("paints a stage under the arrows, and the jobs over them", () => {
    // The old canvas drew stages, then edges, then jobs, so an arrow between two jobs stayed
    // visible over the opaque stage card that holds them. The stage type's beneathConnections
    // keeps that order through the library.
    currentModel = applyDelta(
      emptyModel,
      add(
        stage("Build", "Build"),
        job("Build/A", "Build", "A", {}, 10, 20),
        job("Build/B", "Build", "B", {}, 250, 20),
        edge("j-edge", "Build/A", "Build/B"),
      ),
    );

    const view = draw();

    const drawn = [...view.container.querySelectorAll("[data-element-id], [data-connection-id]")].map(
      (node) => node.getAttribute("data-element-id") ?? node.getAttribute("data-connection-id"),
    );
    expect(drawn.indexOf("Build")).toBeLessThan(drawn.indexOf("j-edge"));
    expect(drawn.indexOf("j-edge")).toBeLessThan(drawn.indexOf("Build/A"));
  });

  it("publishes a selection when an element is clicked", () => {
    // Arrange: so the property grid and the explorer follow the canvas.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build")));
    const view = draw();

    // Act.
    press(view.getByTestId("stage-Build"));

    // Assert.
    expect(select).toHaveBeenCalledTimes(1);
    const selection = select.mock.calls[0][0];
    expect(selection.detail.value.id.source.value.value).toBe("Build");
  });

  // ---- scrollbars: each test is named for the defect it catches -------------------------

  const viewBoxOf = (container: HTMLElement) =>
    (container.querySelector("svg.library-canvas-surface")!.getAttribute("viewBox") ?? "").split(" ").map(Number);

  const thumbOf = (container: HTMLElement, axis: "horizontal" | "vertical") =>
    container.querySelector(`.canvas-scrollbar-${axis} .canvas-scrollbar-thumb`) as HTMLElement;

  it("pans the view when a thumb is dragged - catches an unwired onPan", () => {
    // Arrange.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build")));
    const { container } = draw();
    const [xBefore] = viewBoxOf(container);

    // Act.
    fireEvent.mouseDown(thumbOf(container, "horizontal"), { button: 0, clientX: 10, clientY: 0 });
    fireEvent.mouseMove(window, { clientX: 40, clientY: 0 });
    fireEvent.mouseUp(window);

    // Assert.
    expect(viewBoxOf(container)[0]).toBeGreaterThan(xBefore);
  });

  it("moves the thumb when the view is panned by other means - catches a stale copy of the view", () => {
    // Arrange.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build")));
    const { container } = draw();
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const before = thumbOf(container, "horizontal").style.left;

    // Act.
    fireEvent(surface, pointer("pointerdown", { button: 0, clientX: 200, clientY: 100 }));
    fireEvent(surface, pointer("pointermove", { clientX: 60, clientY: 100 }));
    fireEvent(surface, pointer("pointerup", { clientX: 60, clientY: 100 }));

    // Assert.
    expect(thumbOf(container, "horizontal").style.left).not.toBe(before);
  });

  it("pans with a background drag, and the trailing click does not deselect", () => {
    // Arrange.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build")));
    const { container } = draw();
    const surface = container.querySelector("svg.library-canvas-surface")!;

    // Act.
    fireEvent(surface, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(surface, pointer("pointermove", { clientX: 60, clientY: 100 }));
    fireEvent(surface, pointer("pointerup", { clientX: 60, clientY: 100 }));
    fireEvent.click(surface); // the click that trails the pan - inert, nothing listens

    // Assert: letting go of a pan neither selects nor deselects.
    expect(select).not.toHaveBeenCalled();
  });

  it("resizes the thumb when the view is zoomed - catches a hard-coded viewSpan", () => {
    // Arrange.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build")));
    const { container } = draw();
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const before = thumbOf(container, "horizontal").style.width;

    // Act.
    fireEvent.wheel(surface, { deltaY: -100 });

    // Assert.
    expect(thumbOf(container, "horizontal").style.width).not.toBe(before);
  });

  it("clears the selection when the background is clicked", () => {
    // Arrange.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build")));
    const view = draw();

    // Act.
    press(view.container.querySelector("svg.library-canvas-surface")!);

    // Assert.
    expect(select).toHaveBeenCalledWith(null);
  });

  it("shows what an element is without it being opened", () => {
    // Arrange: Requirement 8.4 - a manual trigger and a condition are things a reader has to be
    // able to see, and opening an edit mode to find them out is not seeing them.
    currentModel = applyDelta(
      emptyModel,
      add(stage("Deploy", "Deploy", { triggerIsManual: true, condition: "succeeded()" })),
    );

    // Act.
    const view = draw();

    // Assert.
    expect(view.getByTestId("indicator-manual")).toBeTruthy();
    expect(view.getByTestId("indicator-condition")).toBeTruthy();
  });

  it("shows how many of a matrix job will run", () => {
    // Arrange.
    currentModel = applyDelta(
      emptyModel,
      add(stage("Test", "Test"), job("Test/Verify", "Test", "Verify", { multiplicity: 3 })),
    );

    // Act.
    const view = draw();

    // Assert.
    expect(view.getByTestId("indicator-multiplicity").textContent).toContain("×3");
  });

  it("puts no badges on an ordinary element", () => {
    // Arrange: a badge on everything is a badge that says nothing.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build")));

    // Act.
    const view = draw();

    // Assert.
    expect(view.container.querySelectorAll(".pipeline-indicator")).toHaveLength(0);
  });

  it("marks an element a problem was reported on", () => {
    // Arrange: Requirement 8.7 - a dangling dependsOn should be visible where it is, not only in
    // a list somewhere else.
    currentModel = applyDelta(emptyModel, add(stage("Ghost", "Ghost")));
    currentProblems = {
      problems: [
        create(ProblemSchema, {
          severity: ProblemSeverity.ERROR,
          message: "Ghost depends on DoesNotExist, which is not a stage in this pipeline.",
          path: { segments: ["azure-pipelines.yml"] },
          location: { location: { case: "elementId", value: { value: "Ghost" } } },
        }),
      ],
    };

    // Act.
    const view = draw();

    // Assert.
    const mark = view.getByTestId("problem-Ghost");
    expect(mark.getAttribute("aria-label")).toContain("DoesNotExist");
    expect(view.getByTestId("stage-Ghost").getAttribute("class")).toContain("pipeline-problem-error");
  });

  it("does not mark an element a problem in another file happens to name", () => {
    // Arrange: two pipelines in one project may each have a stage called Build.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build")));
    currentProblems = {
      problems: [
        create(ProblemSchema, {
          severity: ProblemSeverity.ERROR,
          message: "Something wrong over there.",
          path: { segments: ["other", "azure-pipelines.yml"] },
          location: { location: { case: "elementId", value: { value: "Build" } } },
        }),
      ],
    };

    // Act.
    const view = draw();

    // Assert.
    expect(view.queryByTestId("problem-Build")).toBeNull();
  });

  it("marks nothing when nothing is wrong", () => {
    // Arrange.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build")));
    currentProblems = { problems: [] };

    // Act.
    const view = draw();

    // Assert.
    expect(view.container.querySelectorAll(".pipeline-problem-mark")).toHaveLength(0);
  });

  it.each([
    ["loading", () => (currentLoading = true)],
    ["unavailable", () => (currentFailed = true)],
  ])("while %s, draws no status of its own: the library's frame says it", (_state, arrange) => {
    // Arrange: client-centralization Requirement 2.3 - one appearance, drawn by the library around
    // every canvas. This canvas had its own loading and unavailable blocks.
    arrange();

    // Act.
    const view = draw();

    // Assert.
    expect(view.queryByRole("status")).toBeNull();
    expect(view.queryByRole("alert")).toBeNull();
    expect(view.container.querySelector(".pipeline-canvas-loading, .pipeline-canvas-unavailable")).toBeNull();
  });

  // ---- inline renaming and the F2 it gained ------------------------------------------------

  function labelPromptFor(elementId: string, text: string): unknown {
    return {
      prompt: {
        case: "inputDialog",
        value: {
          title: "Rename",
          icon: "mdi-pencil-outline",
          fieldLabel: "Display name",
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

  it("forwards F2 on the selected element to the backend, which owns the key-to-action map", () => {
    // Arrange. The reach this task added: the provider has declared F2 for its rename all
    // along, and no keyboard path on this canvas could deliver it.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build it")));
    currentSelectionKey = "element:Build";
    const { container } = draw();

    // Act.
    fireEvent.keyDown(container.querySelector("svg.library-canvas-surface") as SVGSVGElement, { key: "F2" });

    // Assert: the key went over as data, addressed to the selected element.
    expect(executeShortcut).toHaveBeenCalledTimes(1);
    const [shortcut, source] = executeShortcut.mock.calls[0] as unknown as [unknown, unknown];
    expect((shortcut as { key: string }).key).toBe("F2");
    expect((source as { source: { value: { value: string } } }).source.value.value).toBe("Build");
  });

  it("draws no refusal line of its own: the library shows a refused shortcut", async () => {
    // Arrange: the backend refuses the rename. The library sends the declared key and its call
    // reports the refusal to the one line drawn around every canvas (client-centralization
    // Requirement 2; contextConnectionReportsToCanvas.test.tsx holds the report of a keystroke).
    // Here: none of its own.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build it")));
    currentSelectionKey = "element:Build";
    executeShortcut.mockResolvedValueOnce({ accepted: false, error: "A template's stage is renamed in its template." });
    const { container } = draw();

    // Act.
    fireEvent.keyDown(container.querySelector("svg.library-canvas-surface") as SVGSVGElement, { key: "F2" });
    await act(async () => {});

    // Assert.
    expect(executeShortcut).toHaveBeenCalledTimes(1);
    expect(container.textContent).not.toContain("A template's stage is renamed in its template.");
    expect(container.querySelector(".canvas-rejection")).toBeNull();
  });

  it("forwards nothing while no element is selected", () => {
    // Arrange.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build it")));
    const { container } = draw();

    // Act.
    fireEvent.keyDown(container.querySelector("svg.library-canvas-surface") as SVGSVGElement, { key: "F2" });

    // Assert.
    expect(executeShortcut).not.toHaveBeenCalled();
  });

  it("opens a stage's editor over its name line, not over the whole card", () => {
    // Arrange. The card draws its name with the job count under it; a card-sized editor
    // would sit over both lines to edit one of them.
    // The stage sits away from the origin on purpose: BoxElement is corner-anchored while
    // the shared helpers speak centred boxes, and at (0, 0) a missed conversion is
    // invisible - the wrong editor lands in the right place.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build it", {}, 40, 30)));
    currentPrompt = labelPromptFor("Build", "Build it");

    // Act.
    const { container } = draw();

    // Assert: on the card, at its top - the name line - not centred on it or beside it.
    const card = container.querySelector('[data-testid="stage-Build"] rect') as SVGRectElement;
    const box = editorBox(container);
    expect(box).not.toBeNull();
    expect(Number(box.getAttribute("x"))).toBe(40);
    expect(Number(box.getAttribute("y"))).toBe(30 + 6);
    expect(Number(box.getAttribute("height"))).toBeLessThan(Number(card.getAttribute("height")));

    const field = container.querySelector("input.inline-label-editor-field") as HTMLInputElement;
    expect(field.value).toBe("Build it");
  });

  it("submits what is typed, and an edit commits through the prompt rather than re-selecting", async () => {
    // Arrange.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build it")));
    currentPrompt = labelPromptFor("Build", "Build it");
    const { container } = draw();
    select.mockClear();

    // Act.
    const field = container.querySelector("input.inline-label-editor-field") as HTMLInputElement;
    fireEvent.change(field, { target: { value: "Build everything" } });
    fireEvent.keyDown(field, { key: "Enter" });

    // Assert.
    await waitFor(() => expect(submitLabel).toHaveBeenCalledWith("Build everything"));
    expect(select).not.toHaveBeenCalled();
  });
});

/** Reads what the shell's Toolbox panel reads: null is what renders the "Open a diagram" placeholder. */
function ToolboxProbe() {
  const items = useDiagramToolbox();
  return <div data-testid="toolbox-probe">{items === null ? "placeholder" : "palette:" + items.map((item) => item.label).join(",")}</div>;
}

describe("PipelineCanvas toolbox", () => {
  it("registers the backend-described palette with the shell while mounted", () => {
    // Arrange. The Toolbox panel shows its placeholder until a mounted canvas registers -
    // which an open pipeline must therefore do (tests.md, documentation task 9: this palette
    // stayed on the placeholder while c4 and mindmap filled theirs on the same flow).
    currentModel = emptyModel;
    currentToolboxItems = [
      create(ToolboxItemSchema, { id: "azure-pipeline.toolbox.stage", label: "Stage", dropActionId: "azure-pipeline.add-stage" }),
      create(ToolboxItemSchema, { id: "azure-pipeline.toolbox.job", label: "Job", dropActionId: "azure-pipeline.add-job" }),
    ];
    toolboxRequests = [];
    const path = ["diagrams", "azure-devops-pipeline", "example 1", "multi-stage.adp"];

    // Act.
    const { getByTestId } = render(
      <DiagramToolboxProvider>
        <PipelineCanvas projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={path} />
        <ToolboxProbe />
      </DiagramToolboxProvider>,
    );

    // Assert: the shell sees this canvas's palette, asked for this diagram's own path.
    expect(getByTestId("toolbox-probe").textContent).toBe("palette:Stage,Job");
    expect(toolboxRequests[0]).toEqual(path);
  });
});

describe("selection, as every canvas has it", () => {
  it("highlights a pushed stage and arrow, and clears on a background press (centralized-selection 9.2)", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentModel = applyDelta(emptyModel, add(stage("Build", "Build"), stage("Deploy", "Deploy", {}, 300, 0), edge("s-edge", "Build", "Deploy")));
        currentSelectionKey = id === null ? null : `element:${id}`;
        return draw();
      },
      pushedIds: () => idsPushed(select),
      element: "Build",
      connection: "s-edge",
    });
  });
});
