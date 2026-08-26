import { describe, expect, it } from "vitest";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import {
  PipelineElementKindProto,
  PipelineElementPayloadSchema,
} from "@client/generated/azure-pipeline_pb";
import {
  applyDelta,
  boxesOf,
  emptyModel,
  endpointsOf,
  jobCountLabel,
  jobsOf,
  EDGE_TYPE,
  JOB_TYPE,
  STAGE_TYPE,
  TEMPLATE_TYPE,
} from "./pipelineModel";

function element(id: string, type: string, payload: Uint8Array, x = 0, y = 0) {
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type,
    payload: { typeUrl: `type.googleapis.com/${type}`, value: payload },
  });
}

function stage(id: string, displayName: string, x = 0, y = 0, jobCount = 0) {
  return element(
    id,
    STAGE_TYPE,
    toBinary(
      PipelineElementPayloadSchema,
      create(PipelineElementPayloadSchema, {
        name: id,
        displayName,
        kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_STAGE,
        width: 220,
        height: 88,
        jobCount,
        enabled: true,
        multiplicity: 1,
      }),
    ),
    x,
    y,
  );
}

function job(id: string, parentId: string, displayName: string, x = 0, y = 0) {
  return element(
    id,
    JOB_TYPE,
    toBinary(
      PipelineElementPayloadSchema,
      create(PipelineElementPayloadSchema, {
        name: displayName,
        displayName,
        kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_JOB,
        parentId,
        width: 180,
        height: 56,
        enabled: true,
        multiplicity: 1,
      }),
    ),
    x,
    y,
  );
}

function edge(id: string, sourceId: string, targetId: string, extras: Record<string, unknown> = {}) {
  return element(
    id,
    EDGE_TYPE,
    toBinary(
      PipelineElementPayloadSchema,
      create(PipelineElementPayloadSchema, {
        kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_EDGE,
        sourceId,
        targetId,
        enabled: true,
        multiplicity: 1,
        ...extras,
      }),
    ),
  );
}

function add(...elements: ReturnType<typeof element>[]) {
  return { action: { case: "add", value: { elements } } } as never;
}

function remove(...ids: string[]) {
  return { action: { case: "remove", value: { elementIds: ids.map((value) => ({ value })) } } } as never;
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

function ungroup(groupId: string, ...elements: ReturnType<typeof element>[]) {
  return {
    action: { case: "ungroup", value: { groupElementId: { value: groupId }, elements } },
  } as never;
}

describe("pipelineModel", () => {
  it("sorts boxes and arrows into their own collections", () => {
    // Arrange and act.
    const model = applyDelta(emptyModel, add(stage("Build", "Build"), stage("Test", "Test"), edge("e", "Build", "Test")));

    // Assert.
    expect([...model.nodes.keys()]).toEqual(["Build", "Test"]);
    expect([...model.edges.keys()]).toEqual(["e"]);
  });

  it("treats an add as an upsert, so an edit replaces an element in place", () => {
    // Arrange: this is what makes "an edit is an Add" work on the client side - the backend
    // re-sends the element in its new state and it lands on top of the old one.
    const first = applyDelta(emptyModel, add(stage("Build", "Build the solution")));

    // Act.
    const second = applyDelta(first, add(stage("Build", "Build everything")));

    // Assert.
    expect(second.nodes.size).toBe(1);
    expect(second.nodes.get("Build")?.payload.displayName).toBe("Build everything");
  });

  it("never mutates the model it was given", () => {
    // Arrange.
    const before = applyDelta(emptyModel, add(stage("Build", "Build")));

    // Act.
    applyDelta(before, add(stage("Test", "Test")));

    // Assert.
    expect(before.nodes.size).toBe(1);
  });

  it("removes what the backend says has gone", () => {
    // Arrange.
    const model = applyDelta(emptyModel, add(stage("Build", "Build"), edge("e", "Build", "Test")));

    // Act.
    const after = applyDelta(model, remove("Build", "e"));

    // Assert.
    expect(after.nodes.size).toBe(0);
    expect(after.edges.size).toBe(0);
  });

  it("keeps a node's position as the top-left corner it arrived as", () => {
    // Arrange: this module puts corners on the wire where C4 puts centres, so a canvas that
    // assumed the other convention would draw every box half a box off.
    const model = applyDelta(emptyModel, add(stage("Build", "Build", 100, 200)));

    // Assert.
    expect(model.nodes.get("Build")).toMatchObject({ x: 100, y: 200 });
    expect(boxesOf(model)).toEqual([{ x: 100, y: 200, width: 220, height: 88 }]);
  });

  describe("folding", () => {
    it("takes a collapsed stage's jobs off the canvas", () => {
      // Arrange: the third independent use of Group/Ungroup, after the mindmap's.
      const model = applyDelta(
        emptyModel,
        add(stage("Build", "Build"), job("Build/Compile", "Build", "Compile"), job("Build/Lint", "Build", "Lint")),
      );

      // Act.
      const folded = applyDelta(model, group("Build", "Build/Compile", "Build/Lint"));

      // Assert.
      expect(folded.collapsed.has("Build")).toBe(true);
      expect(folded.nodes.has("Build/Compile")).toBe(false);
      expect(folded.nodes.has("Build")).toBe(true);
    });

    it("brings them back when the stage is opened again", () => {
      // Arrange.
      const folded = applyDelta(
        applyDelta(emptyModel, add(stage("Build", "Build"))),
        group("Build"),
      );

      // Act.
      const opened = applyDelta(folded, ungroup("Build", job("Build/Compile", "Build", "Compile")));

      // Assert.
      expect(opened.collapsed.has("Build")).toBe(false);
      expect(opened.nodes.get("Build/Compile")?.payload.displayName).toBe("Compile");
    });

    it("ignores a fold that names nothing", () => {
      // Arrange: a malformed delta must not put an empty string in the collapsed set, where it
      // would sit forever matching no stage.
      const model = applyDelta(emptyModel, add(stage("Build", "Build")));

      // Act.
      const after = applyDelta(model, group(""));

      // Assert.
      expect(after.collapsed.size).toBe(0);
    });
  });

  describe("job counts", () => {
    it("reads the count off the stage, because a collapsed one sends no jobs", () => {
      // Arrange & act.
      const model = applyDelta(emptyModel, add(stage("Build", "Build", 0, 0, 3)));

      // Assert.
      expect(model.nodes.get("Build")?.payload.jobCount).toBe(3);
    });

    it("says 'job' for one and 'jobs' for any other number", () => {
      // Assert.
      expect(jobCountLabel(1)).toBe("1 job");
      expect(jobCountLabel(0)).toBe("0 jobs");
      expect(jobCountLabel(4)).toBe("4 jobs");
    });
  });

  it("finds the jobs belonging to one stage", () => {
    // Arrange.
    const model = applyDelta(
      emptyModel,
      add(
        stage("Build", "Build"),
        stage("Test", "Test"),
        job("Build/Compile", "Build", "Compile"),
        job("Test/Verify", "Test", "Verify"),
      ),
    );

    // Assert.
    expect(jobsOf(model, "Build").map((node) => node.id)).toEqual(["Build/Compile"]);
  });

  describe("edges", () => {
    it("resolves both ends to boxes the model holds", () => {
      // Arrange.
      const model = applyDelta(emptyModel, add(stage("Build", "Build"), stage("Test", "Test"), edge("e", "Build", "Test")));

      // Act.
      const ends = endpointsOf(model, model.edges.get("e")!);

      // Assert.
      expect(ends?.from.id).toBe("Build");
      expect(ends?.to.id).toBe("Test");
    });

    it("gives up on an edge whose far end is not on the canvas", () => {
      // Arrange: a job's dependency inside a collapsed stage has nowhere to start, and a line
      // into empty space says less than no line at all.
      const model = applyDelta(emptyModel, add(stage("Test", "Test"), edge("e", "Build", "Test")));

      // Act & assert.
      expect(endpointsOf(model, model.edges.get("e")!)).toBeNull();
    });

    it("carries whether the file said so and whether it names anything", () => {
      // Arrange: the two things a reader most needs telling about an arrow.
      const model = applyDelta(
        emptyModel,
        add(edge("implicit", "A", "B", { implicitDependency: true }), edge("broken", "", "C", { broken: true })),
      );

      // Assert.
      expect(model.edges.get("implicit")?.payload.implicitDependency).toBe(true);
      expect(model.edges.get("broken")?.payload.broken).toBe(true);
    });
  });

  it("ignores an element of a type this module does not put on the wire", () => {
    // Arrange: the stream is shared, and a module must not decode something that is not its own.
    const model = applyDelta(
      emptyModel,
      add(element("stranger", "c4/model+node", new Uint8Array([1, 2, 3]))),
    );

    // Assert.
    expect(model.nodes.size).toBe(0);
    expect(model.edges.size).toBe(0);
  });

  it("keeps an unfollowed template as a box of its own", () => {
    // Arrange: a diagram that quietly drops a template is worse than one that admits the gap.
    const model = applyDelta(
      emptyModel,
      add(
        element(
          "template:0",
          TEMPLATE_TYPE,
          toBinary(
            PipelineElementPayloadSchema,
            create(PipelineElementPayloadSchema, {
              kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_TEMPLATE,
              displayName: "shared.yml@other",
              unresolvedReason: "It comes from the 'other' repository resource.",
              width: 220,
              height: 88,
            }),
          ),
        ),
      ),
    );

    // Assert.
    expect(model.nodes.get("template:0")?.payload.unresolvedReason).toContain("repository resource");
  });
});
