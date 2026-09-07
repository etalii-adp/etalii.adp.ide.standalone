import { describe, expect, it } from "vitest";
import { create } from "@bufbuild/protobuf";
import {
  PipelineElementKindProto,
  PipelineElementPayloadSchema,
} from "@client/generated/azure-pipeline_pb";
import { ProblemSchema, ProblemSeverity } from "@client/generated/problems_pb";
import { indicatorsOf, problemMarkOf, problemsOn } from "./pipelineIndicators";

function payload(extra: Record<string, unknown> = {}) {
  return create(PipelineElementPayloadSchema, {
    kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_JOB,
    enabled: true,
    multiplicity: 1,
    ...extra,
  });
}

function keys(extra: Record<string, unknown> = {}) {
  return indicatorsOf(payload(extra)).map((indicator) => indicator.key);
}

function problem(elementId: string, segments: string[], extra: Record<string, unknown> = {}) {
  return create(ProblemSchema, {
    severity: ProblemSeverity.WARNING,
    message: "Something is off.",
    path: { segments },
    location: { location: { case: "elementId", value: { value: elementId } } },
    ...extra,
  });
}

describe("pipelineIndicators", () => {
  it("says nothing about an ordinary element", () => {
    // Arrange & act & assert.
    // Every element carrying a badge would be every element carrying no information.
    expect(keys()).toEqual([]);
  });

  it("marks an element the file switched off", () => {
    // Assert.
    expect(keys({ enabled: false })).toContain("disabled");
  });

  it("marks a stage that waits for a person", () => {
    // Assert.
    // Requirement 8.4 - a reader must not have to open the file to find this out.
    expect(keys({ triggerIsManual: true })).toContain("manual");
  });

  it("carries the condition verbatim rather than paraphrasing it", () => {
    // Arrange & act.
    // A reader who knows Azure's functions can read it; one who does not is no worse off than
    // with a paraphrase this module invented.
    const indicators = indicatorsOf(payload({ condition: "and(succeeded(), eq(variables.x, 'y'))" }));

    // Assert.
    const condition = indicators.find((indicator) => indicator.key === "condition");
    expect(condition?.title).toContain("and(succeeded(), eq(variables.x, 'y'))");
  });

  it("marks an element whose failure does not fail the run", () => {
    // Assert.
    expect(keys({ continuesOnError: true })).toContain("continue-on-error");
  });

  it("shows how many of a matrix job will run", () => {
    // Arrange & act.
    const indicators = indicatorsOf(payload({ multiplicity: 3 }));

    // Assert.
    // Requirement 4.6 - a job that becomes three at run time must not look like one.
    const multiplicity = indicators.find((indicator) => indicator.key === "multiplicity");
    expect(multiplicity?.glyph).toBe("×3");
  });

  it("admits when the count is not known yet rather than showing a number", () => {
    // Arrange & act.
    // `parallel: $(slices)` - putting a number here that nobody knows would be worse than saying
    // it depends.
    const indicators = indicatorsOf(payload({ multiplicity: 1, indeterminate: true }));

    // Assert.
    expect(indicators.map((indicator) => indicator.key)).toContain("multiplicity-unknown");
    expect(indicators.find((indicator) => indicator.key === "multiplicity-unknown")?.glyph).toBe("×?");
  });

  it("does not show both a count and an unknown count", () => {
    // Arrange & act.
    const indicators = keys({ multiplicity: 4, indeterminate: true });

    // Assert.
    expect(indicators).toContain("multiplicity");
    expect(indicators).not.toContain("multiplicity-unknown");
  });

  it("marks an element from a template and says where to edit it", () => {
    // Arrange & act.
    const indicators = indicatorsOf(payload({ fromTemplate: true, templatePath: "templates/build.yml" }));

    // Assert.
    const fromTemplate = indicators.find((indicator) => indicator.key === "from-template");
    expect(fromTemplate?.title).toContain("templates/build.yml");
  });

  it("puts whether it runs before how it runs", () => {
    // Arrange & act.
    // Fixed rather than incidental order: that is the order a reader asks the questions in, and
    // badges that shuffle between renders are badges nobody learns to read.
    const indicators = keys({
      enabled: false,
      triggerIsManual: true,
      condition: "always()",
      continuesOnError: true,
      multiplicity: 2,
    });

    // Assert.
    expect(indicators).toEqual(["disabled", "manual", "condition", "continue-on-error", "multiplicity"]);
  });

  it("gives every badge a distinct key, so React and a test can both address them", () => {
    // Arrange & act.
    const indicators = keys({ enabled: false, triggerIsManual: true, condition: "always()", multiplicity: 2 });

    // Assert.
    expect(new Set(indicators).size).toBe(indicators.length);
  });
});

describe("problem marks", () => {
  it("finds the problems naming an element in this file", () => {
    // Arrange.
    const problems = [problem("Build", ["azure-pipelines.yml"])];

    // Act & assert.
    expect(problemsOn(problems, ["azure-pipelines.yml"], "Build")).toHaveLength(1);
  });

  it("ignores a problem on the same element name in a different pipeline", () => {
    // Arrange: two pipelines in one project may each have a stage called Build, and marking the
    // wrong one is worse than marking neither.
    const problems = [problem("Build", ["other", "azure-pipelines.yml"])];

    // Act & assert.
    expect(problemsOn(problems, ["azure-pipelines.yml"], "Build")).toHaveLength(0);
  });

  it("ignores a problem located on a line rather than an element", () => {
    // Arrange: a rule that judged the text has nothing to mark on the canvas.
    const onALine = create(ProblemSchema, {
      severity: ProblemSeverity.ERROR,
      message: "Bad indentation.",
      path: { segments: ["azure-pipelines.yml"] },
      location: { location: { case: "line", value: 12 } },
    });

    // Act & assert.
    expect(problemsOn([onALine], ["azure-pipelines.yml"], "Build")).toHaveLength(0);
  });

  it("marks nothing when nothing is wrong", () => {
    // Act & assert.
    expect(problemMarkOf([])).toBeNull();
  });

  it("lets an error outrank a warning", () => {
    // Arrange: a stage with both is a stage that will not run, and that is the thing to say.
    const both = [
      problem("Build", ["p.yml"]),
      problem("Build", ["p.yml"], { severity: ProblemSeverity.ERROR, message: "Depends on nothing that exists." }),
    ];

    // Act.
    const mark = problemMarkOf(both);

    // Assert.
    expect(mark?.severity).toBe("error");
  });

  it("says everything that is wrong, not only the first thing", () => {
    // Arrange.
    const both = [
      problem("Build", ["p.yml"], { message: "First." }),
      problem("Build", ["p.yml"], { message: "Second." }),
    ];

    // Act.
    const mark = problemMarkOf(both);

    // Assert.
    expect(mark?.title).toContain("First.");
    expect(mark?.title).toContain("Second.");
  });
});
