import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import { ProblemSchema, ProblemSeverity, type Problem } from "@client/generated/problems_pb";
import { PipelineElementKindProto, PipelineElementPayloadSchema } from "@client/generated/azure-pipeline_pb";
import disText from "../definition/azure-devops-pipeline.dis?raw";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { applyDelta, emptyModel, EDGE_TYPE, JOB_TYPE, STAGE_TYPE, STEP_TYPE, TEMPLATE_TYPE, type PipelineModel } from "./pipelineModel";
import { correctedDefinition, PIPELINE_BINDINGS, PIPELINE_CORRECTIONS } from "./pipelineBindings";
import { HAND_WRITTEN_DEFINITION, handWrittenModel } from "./handWrittenDefinition";

/**
 * Requirement 7.1: the canvas definition is compiled from the bundled `azure-devops-pipeline.dis`, and
 * the hand-written definition it replaced (`handWrittenDefinition.ts`) is kept here as the oracle. Both
 * are drawn by the real `DiagramCanvas` over one corpus, and their markup must be the same.
 *
 * The definition is compiled after `PIPELINE_CORRECTIONS` (`pipelineBindings.ts`), each of which states
 * what the definition says today, what it should say for the drawing to stay as it was, and why; each
 * refuses to apply once the definition no longer says it, so a correction fails until it is removed.
 *
 * What the compiled definition draws differently on purpose, because the definition says so, is stated
 * below as a replacement that must match an exact number of times rather than tolerated:
 *  - every box carries its tooltip (`<title>`), where the oracle carried one on a template only, and the
 *    template's names its reference instead of why it is not followed - the reason stays in its
 *    property grid's "Why it is not shown" row;
 *  - a stage's role is `group`, a box holding boxes, where it was `button`;
 *  - a box's accessible name is DISL's default (6.15), its type's label and its name;
 *  - the unknown count ("×?") is shown on a job only, as the definition states it for jobs only.
 */
let currentModel: PipelineModel = emptyModel;
let currentProblems: Problem[] = [];

vi.mock("./usePipelineStream", () => ({
  usePipelineStream: () => ({ model: currentModel, loading: false, failed: false, reportView: vi.fn() }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
    useContextConnection: () => connection,
    useContextSelection: () => ({ selection: null }),
    useContextProblems: () => ({ problems: currentProblems }),
  };
});

const emptyPalette: never[] = [];

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: () => emptyPalette,
}));

const connection = fakeContextConnection({ select: vi.fn() });

const { DiagramCanvas } = await import("@client/canvas/library/DiagramCanvas");
const { PipelineCanvas, PIPELINE_DEFINITION } = await import("./PipelineCanvas");

const PATH = ["azure-pipelines.yml"];

function element(id: string, type: string, x: number, y: number, width: number, height: number, extra: Record<string, unknown>) {
  const payload = create(PipelineElementPayloadSchema, { enabled: true, multiplicity: 1, width, height, ...extra });
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type,
    payload: { typeUrl: `type.googleapis.com/${type}`, value: toBinary(PipelineElementPayloadSchema, payload) },
  });
}

const stage = (id: string, x: number, y: number, width: number, height: number, extra: Record<string, unknown> = {}) =>
  element(id, STAGE_TYPE, x, y, width, height, { name: id, displayName: id, kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_STAGE, ...extra });
const job = (id: string, parentId: string, x: number, y: number, extra: Record<string, unknown> = {}) =>
  element(id, JOB_TYPE, x, y, 180, 56, { name: id, displayName: id.split("/").pop(), parentId, kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_JOB, ...extra });
const step = (id: string, parentId: string, x: number, y: number, extra: Record<string, unknown> = {}) =>
  element(id, STEP_TYPE, x, y, 180, 56, { displayName: id.split("/").pop(), parentId, kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_STEP, ...extra });
const edge = (id: string, sourceId: string, targetId: string, extra: Record<string, unknown> = {}) =>
  element(id, EDGE_TYPE, 0, 0, 0, 0, { kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_EDGE, sourceId, targetId, ...extra });

function add(...elements: ReturnType<typeof element>[]) {
  return { action: { case: "add", value: { elements } } } as never;
}

/**
 * Every case the drawing distinguishes: an open and a closed stage, each of the seven indicators on
 * the kinds that carry it - alone, together, and with an earlier one hidden - a deployment job, a step,
 * an element from a template, an unfollowed template, both problem marks, and the arrow a dependsOn
 * writes, the implicit one of the written order, a broken one and one whose end is not drawn.
 */
const CORPUS: PipelineModel = applyDelta(
  applyDelta(
    emptyModel,
    add(
      stage("Build", 0, 0, 476, 400, { jobCount: 3, condition: "succeeded()", triggerIsManual: true }),
      job("Build/Compile", "Build", 24, 64, { multiplicity: 3, continuesOnError: true }),
      job("Build/Matrix", "Build", 24, 152, { indeterminate: true }),
      element("Build/Deploy", JOB_TYPE, 24, 240, 180, 56, { name: "Deploy", displayName: "Deploy", parentId: "Build", kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_DEPLOYMENT_JOB, environment: "prod", enabled: false, fromTemplate: true, templatePath: "templates/deploy.yml" }),
      step("Build/Compile/dotnet build", "Build/Compile", 260, 64, { condition: "always()", enabled: false }),
      step("Build/Compile/dotnet test", "Build/Compile", 260, 152),
      stage("Test", 556, 0, 220, 88, { jobCount: 1, enabled: false, continuesOnError: true }),
      stage("Release", 856, 0, 220, 88, { jobCount: 2, fromTemplate: true, templatePath: "templates/release.yml", indeterminate: true }),
      element("template:t1", TEMPLATE_TYPE, 556, 480, 220, 88, { name: "templates/missing.yml", displayName: "templates/missing.yml", kind: PipelineElementKindProto.PIPELINE_ELEMENT_KIND_TEMPLATE, reference: "templates/missing.yml", unresolvedReason: "There is no file at 'templates/missing.yml'.", indeterminate: true, parentId: "Release" }),
      edge("edge:Build->Test", "Build", "Test"),
      edge("edge:Test->Release", "Test", "Release", { implicitDependency: true }),
      edge("edge:Ghost->Release", "Release", "Release", { broken: true }),
      edge("edge:Compile->Matrix", "Build/Compile", "Build/Matrix"),
      edge("edge:Hidden->Test", "Test/Hidden", "Test"),
    ),
  ),
  { action: { case: "group", value: { sourceElementIds: [], groupElement: create(ElementSchema, { id: { value: "Test" } }) } } } as never,
);

const problem = (elementId: string, severity: ProblemSeverity, message: string) =>
  create(ProblemSchema, { severity, message, path: { segments: PATH }, location: { location: { case: "elementId", value: { value: elementId } } } });

const PROBLEMS = [
  problem("Release", ProblemSeverity.ERROR, "Release depends on Ghost, which is not a stage in this pipeline."),
  problem("Build/Matrix", ProblemSeverity.WARNING, "How many of these run is decided when the pipeline runs."),
];

const ids = (markup: string) => markup.replace(/«r[0-9a-z]+»|:r[0-9a-z]+:/g, "«id»");

/** Replaces every match, failing where there is none: a stated difference that no longer occurs is not one. */
function replaced(markup: string, pattern: RegExp, replacement: string | ((...match: string[]) => string), expected: number): string {
  const found = markup.match(pattern)?.length ?? 0;
  expect(found, String(pattern)).toBe(expected);
  return markup.replace(pattern, replacement as never);
}

/** The oracle's model, without the unknown count it showed on a kind the definition never multiplies. */
function oracleModel() {
  const model = handWrittenModel(CORPUS, PROBLEMS, PATH);
  return {
    ...model,
    elements: model.elements.map((element) => {
      const payload = element.payload as { indicators: { key: string }[] };
      return element.type === "job" ? element : { ...element, payload: { ...payload, indicators: payload.indicators.filter((indicator) => indicator.key !== "multiplicity-unknown") } };
    }),
  };
}

function oracleDrawing() {
  return render(
    <div className="pipeline-canvas" data-testid="pipeline-canvas">
      <DiagramCanvas
        definition={HAND_WRITTEN_DEFINITION}
        model={oracleModel()}
        events={{}}
        source={{ entryId: new Uint8Array(16), path: PATH }}
        toolboxItems={emptyPalette}
        ariaLabel={`Pipeline ${PATH.join("/")}`}
        className="pipeline-canvas-surface"
        scrollbarsClassName="pipeline-scrollbars"
      />
    </div>,
  );
}

function compiledDrawing() {
  currentModel = CORPUS;
  currentProblems = PROBLEMS;
  return render(<PipelineCanvas projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={PATH} />);
}

describe("the pipeline's compiled definition", () => {
  it("draws what the hand-written definition drew, element for element", () => {
    const oracle = oracleDrawing();
    // The oracle's one tooltip: why a template is not followed (see below).
    const expected = replaced(ids(oracle.container.innerHTML), /(<g class="library-element pipeline-template[^>]*>)<title>[^<]*<\/title>/g, "$1", 1);
    oracle.unmount();

    const compiled = compiledDrawing();
    let actual = ids(compiled.container.innerHTML);
    compiled.unmount();

    // The definition's tooltip on every box, which the oracle drew on none but a template.
    actual = replaced(actual, /(<g class="library-element [^>]*>)<title>[^<]*<\/title>/g, "$1", 9);
    // A stage announces itself as a group of jobs, as the definition says.
    actual = replaced(actual, /(data-testid="stage-[^"]*" data-expanded="[a-z]+") role="group"/g, '$1 role="button"', 3);
    // A box is named by DISL's default (6.15): its type's label and its name.
    actual = replaced(actual, / aria-label="(?:Job|Deployment job|Step) ([^"]*)"/g, ' aria-label="$1"', 5);
    actual = replaced(actual, /(data-testid="node-template:t1" role="button") aria-label="Template"/g, '$1 aria-label="templates/missing.yml"', 1);

    // The cases the comparison would pass vacuously on: a canvas that drew none of them.
    for (const indicator of ["disabled", "manual", "condition", "continue-on-error", "multiplicity", "multiplicity-unknown", "from-template"]) {
      expect(actual, indicator).toContain(`data-testid="indicator-${indicator}"`);
    }
    for (const drawn of ["pipeline-deployment", "pipeline-template", "pipeline-stage-collapsed", "pipeline-problem-mark-error", "pipeline-problem-mark-warning", "pipeline-edge-implicit", "pipeline-edge-broken", "1 job"]) {
      expect(actual, drawn).toContain(drawn);
    }
    expect(actual).not.toContain("edge:Hidden->Test");
    expect(actual).toBe(expected);
  });

  it("draws the seven indicators in today's order: whether it runs before how it runs", () => {
    currentModel = applyDelta(emptyModel, add(
      stage("All", 0, 0, 476, 160, { enabled: false, triggerIsManual: true, condition: "always()", continuesOnError: true, fromTemplate: true, templatePath: "t.yml" }),
      job("All/Many", "All", 24, 64, { enabled: false, condition: "always()", continuesOnError: true, multiplicity: 2, fromTemplate: true, templatePath: "t.yml" }),
      job("All/Unknown", "All", 240, 64, { indeterminate: true }),
    ));
    currentProblems = [];
    const view = render(<PipelineCanvas projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={PATH} />);

    const indicators = (id: string) => [...view.getByTestId(id).querySelectorAll("[data-testid^=indicator-]")].map((group) => [group.getAttribute("data-testid")!.slice("indicator-".length), group.querySelector("text")!.getAttribute("x")]);
    expect(indicators("stage-All")).toEqual([["disabled", "464"], ["manual", "448"], ["condition", "432"], ["continue-on-error", "416"], ["from-template", "400"]]);
    expect(indicators("node-All/Many")).toEqual([["disabled", "196"], ["condition", "180"], ["continue-on-error", "164"], ["multiplicity", "148"], ["from-template", "132"]]);
    expect(indicators("node-All/Unknown")).toEqual([["multiplicity-unknown", "412"]]);
    view.unmount();
  });

  it("is what the canvas draws: the module's definition is compiled from the bundled definition", () => {
    // The canary for the tests above: were the canvas still holding its own definition, they would
    // compare the oracle with a copy of itself.
    expect(PIPELINE_DEFINITION).toEqual(assertValidDiagramDefinition(compileNotation(correctedDefinition(parseDisl(disText)), PIPELINE_BINDINGS)));
    expect(PIPELINE_DEFINITION.layout).toEqual(HAND_WRITTEN_DEFINITION.layout);
    expect(PIPELINE_DEFINITION.dragging).toBe(HAND_WRITTEN_DEFINITION.dragging);
    expect(PIPELINE_DEFINITION.elementTypes.every((type) => type.draggable === false && type.deletable === false && type.sizing === "model")).toBe(true);
  });

  it("refuses a correction the definition no longer needs, so none outlives its reason", () => {
    const spec = parseDisl(disText);
    const [first] = PIPELINE_CORRECTIONS;

    expect(PIPELINE_CORRECTIONS.length).toBeGreaterThan(0);
    expect(() => correctedDefinition(correctedDefinition(spec, [first!]), [first!])).toThrow(/now says .*; remove or update its correction/);
    expect(() => correctedDefinition(spec, [{ ...first!, at: ["notation", "nodes", "Nothing", "labels"] }])).toThrow(/has no notation\.nodes\.Nothing\.labels/);
  });
});
