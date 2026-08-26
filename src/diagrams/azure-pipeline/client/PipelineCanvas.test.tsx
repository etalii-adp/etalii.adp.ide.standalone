import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, fireEvent } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import { ProblemSchema, ProblemSeverity } from "@client/generated/context_pb";
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

const select = vi.fn();
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
    useContextConnection: () => ({ watchId: new Uint8Array(16), select }),
    useContextSelection: () => ({ selection: null, actions: [] }),
    useContextProblems: () => currentProblems,
  };
});

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

describe("PipelineCanvas", () => {
  beforeEach(() => {
    currentModel = emptyModel;
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
    expect(view.getByTestId("edge-edge:Build->Test")).toBeTruthy();
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
    expect(view.getByTestId("edge-implicit").getAttribute("class")).toContain("pipeline-edge-implicit");
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
    expect(view.getByTestId("edge-broken").getAttribute("class")).toContain("pipeline-edge-broken");
  });

  it("draws no arrow when one of its ends is not on the canvas", () => {
    // Arrange: a line into empty space says less than no line at all.
    currentModel = applyDelta(emptyModel, add(stage("Test", "Test"), edge("dangling", "Build", "Test")));

    // Act.
    const view = draw();

    // Assert.
    expect(view.queryByTestId("edge-dangling")).toBeNull();
  });

  it("publishes a selection when an element is clicked", () => {
    // Arrange: so the property grid and the explorer follow the canvas.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build")));
    const view = draw();

    // Act.
    fireEvent.click(view.getByTestId("stage-Build"));

    // Assert.
    expect(select).toHaveBeenCalledTimes(1);
    const selection = select.mock.calls[0][0];
    expect(selection.detail.value.id.source.value.value).toBe("Build");
  });

  it("clears the selection when the background is clicked", () => {
    // Arrange.
    currentModel = applyDelta(emptyModel, add(stage("Build", "Build")));
    const view = draw();

    // Act.
    fireEvent.click(view.container.querySelector(".pipeline-canvas-surface")!);

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

  it("says it is loading rather than showing an empty pipeline", () => {
    // Arrange.
    currentLoading = true;

    // Act.
    const view = draw();

    // Assert.
    expect(view.getByRole("status").textContent).toContain("Loading");
  });

  it("says so when the diagram is no longer there", () => {
    // Arrange.
    currentFailed = true;

    // Act.
    const view = draw();

    // Assert.
    expect(view.getByRole("alert").textContent).toContain("no longer available");
  });
});
