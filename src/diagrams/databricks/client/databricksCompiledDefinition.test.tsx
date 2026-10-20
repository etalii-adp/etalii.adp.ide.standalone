import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import type { DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import bundleText from "../definition/databricks-bundle.dis?raw";
import jobText from "../definition/databricks-job.dis?raw";
import pipelineText from "../definition/databricks-pipeline.dis?raw";
import { compiledDefinition, DATABRICKS_BINDINGS } from "./databricksBindings";
import { emptyModel, type DatabricksEdge, type DatabricksFrame, type DatabricksModel, type DatabricksNode } from "./databricksModel";
import { HAND_WRITTEN_CONNECTABLE, HAND_WRITTEN_RENDER_ONLY, handWrittenModel } from "./handWrittenDefinition";

/**
 * Requirement 8.1: each reading's canvas definition is compiled from its bundled definition, and the one
 * hand-written definition all three replaced (`handWrittenDefinition.ts`) is kept here as the oracle. Both
 * are drawn by the real `DiagramCanvas` over one corpus per reading - the lakehouse example's job, bundle
 * and pipeline, with a missing upstream, a name too long for its box, a kind the reader does not model,
 * targets with and without a summary, and every simulated state - and their markup must be the same.
 *
 * The definitions are compiled as they are bundled: nothing corrects them on the way in. What the bundle's
 * compiled definition draws differently on purpose, because its definition says so, is stated below as a
 * change to the oracle, or a replacement that must match an exact number of times, rather than tolerated:
 *  - a target is a rectangle on the background layer (DISL 6.16), drawn beneath the connections, where the
 *    oracle drew the library's frame over them: the library's box, its corners rounded 4 rather than 6;
 *  - an override's dash is the definition's line style, which the stylesheet used to say alone, so its line
 *    wears no class of its own to say it by;
 *  - a target with no mode, no default and no override draws its summary line empty, where the oracle's
 *    parts drew no line at all.
 * The job's and the pipeline's compiled drawings are the oracle's exactly.
 */
let currentModel: DatabricksModel = emptyModel;
let currentStates = new Map<string, string>();

vi.mock("./useDatabricksStream", () => ({
  useDatabricksStream: () => ({ model: currentModel, loading: false, failed: false, moveElementTo: vi.fn(), reportView: vi.fn() }),
}));

vi.mock("./useSimulatedRun", async (importOriginal) => {
  const actual = await importOriginal<typeof import("./useSimulatedRun")>();
  return { ...actual, useSimulatedRun: () => ({ states: currentStates, marker: null, intercept: () => false, dismiss: vi.fn() }) };
});

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
    useContextConnection: () => connection,
    useContextSelection: () => ({ selection: null }),
  };
});

const emptyPalette: never[] = [];

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: () => emptyPalette,
}));

const connection = fakeContextConnection({ select: vi.fn() });

const { DiagramCanvas } = await import("@client/canvas/library/DiagramCanvas");
const { BundleCanvas, BUNDLE_DEFINITION } = await import("./BundleCanvas");
const { JobCanvas, JOB_DEFINITION } = await import("./JobCanvas");
const { PipelineCanvas, PIPELINE_DEFINITION } = await import("./PipelineCanvas");

const PROJECT = new Uint8Array(16);
const ENTRY = new Uint8Array(16);

function node(id: string, kind: string, label: string, x: number, y: number, badges: string[] = [], unresolved = false): DatabricksNode {
  return { id, x, y, kind, label, badges, unresolved, runIf: "" };
}

function frame(id: string, x: number, y: number, mode: string, isDefault: boolean, overrideCount: number): DatabricksFrame {
  return { id, x, y, label: id.slice("target:".length), mode, isDefault, overrideCount };
}

function edge(id: string, kind: DatabricksEdge["kind"], fromElementId: string, toElementId: string, outcome = ""): DatabricksEdge {
  return { id, kind, fromElementId, toElementId, outcome };
}

function modelOf(nodes: DatabricksNode[], edges: DatabricksEdge[], frames: DatabricksFrame[] = []): DatabricksModel {
  return {
    nodes: new Map(nodes.map((each) => [each.id, each])),
    frames: new Map(frames.map((each) => [each.id, each])),
    edges: new Map(edges.map((each) => [each.id, each])),
  };
}

/** The lakehouse example's job, with a missing upstream and a task with a long name. */
const JOB = modelOf(
  [
    node("task:land_raw", "notebook", "land_raw", 0, 0, ["ingest_cluster"]),
    node("task:quality_gate", "condition", "quality_gate", 260, 0, ["serverless"]),
    node("task:publish", "notebook", "publish", 520, 0, ["serverless"]),
    node("task:alert_on_empty", "python", "alert_on_empty", 520, 120, ["AT_LEAST_ONE_FAILED", "serverless"]),
    node("task:refresh_dashboard_with_a_name_too_long_for_its_box", "sql", "refresh_dashboard_with_a_name_too_long_for_its_box", 780, 0, ["serverless"]),
    node("task:gone", "other", "gone", 260, 240, ["serverless"], true),
    node("cluster:ingest_cluster", "compute", "ingest_cluster", 0, 400, ["15.4.x-scala2.12", "Standard_DS3_v2", "2 workers"]),
  ],
  [
    edge("edge:land_raw->quality_gate", "depends", "task:land_raw", "task:quality_gate"),
    edge("edge:quality_gate->publish", "depends", "task:quality_gate", "task:publish", "true"),
    edge("edge:quality_gate->alert_on_empty", "depends", "task:quality_gate", "task:alert_on_empty", "false"),
    edge("edge:publish->refresh_dashboard_with_a_name_too_long_for_its_box", "depends", "task:publish", "task:refresh_dashboard_with_a_name_too_long_for_its_box"),
    edge("edge:gone->publish", "depends", "task:gone", "task:publish"),
  ],
);

/** Each simulated state a task can be drawn in. */
const JOB_STATES = new Map([
  ["task:land_raw", "succeeded"],
  ["task:quality_gate", "running"],
  ["task:publish", "pending"],
  ["task:alert_on_empty", "skipped"],
  ["task:gone", "failed"],
]);

/** The lakehouse example's bundle, with a kind the reader does not model and a target with nothing to say. */
const BUNDLE = modelOf(
  [
    node("bundle", "bundle", "lakehouse", 0, 0),
    node("resource:jobs/nightly_ingest", "jobs", "nightly_ingest", 0, 180, ["jobs"]),
    node("resource:pipelines/bronze_to_gold", "pipelines", "bronze_to_gold", 260, 180, ["pipelines"]),
    node("unknown:resources.dashboards", "resources.dashboards", "dashboards", 520, 180, ["resources.dashboards"]),
  ],
  [
    edge("override:prod/pipelines/bronze_to_gold", "override", "target:prod", "resource:pipelines/bronze_to_gold"),
    edge("override:prod/jobs/nightly_ingest", "override", "target:prod", "resource:jobs/nightly_ingest"),
    edge("override:staging/jobs/nightly_ingest", "override", "target:staging", "resource:jobs/nightly_ingest"),
  ],
  [
    frame("target:dev", 0, 400, "development", true, 0),
    frame("target:prod", 260, 400, "production", false, 2),
    frame("target:staging", 520, 400, "", false, 1),
    frame("target:bare", 780, 400, "", false, 0),
  ],
);

const BUNDLE_STATES = new Map([["resource:jobs/nightly_ingest", "running"], ["resource:pipelines/bronze_to_gold", "succeeded"], ["unknown:resources.dashboards", "pending"]]);

/** The lakehouse example's pipeline. */
const PIPELINE = modelOf(
  [
    node("library:transformations/bronze", "source", "transformations/bronze", 0, 0, ["notebook"]),
    node("library:transformations/silver", "source", "transformations/silver", 0, 80, ["notebook"]),
    node("library:transformations/gold/**", "source", "transformations/gold/**", 0, 160, ["glob"]),
    node("pipeline", "pipeline", "Bronze to gold", 260, 80, ["serverless", "CURRENT"]),
    node("target", "target", "lakehouse_dev.gold", 520, 80),
    node("compute", "compute", "serverless", 260, 240),
    node("notifications", "notifications", "data-eng@vrenken.eu", 520, 240),
  ],
  [
    edge("flow:transformations/bronze->pipeline", "flow", "library:transformations/bronze", "pipeline"),
    edge("flow:transformations/silver->pipeline", "flow", "library:transformations/silver", "pipeline"),
    edge("flow:transformations/gold/**->pipeline", "flow", "library:transformations/gold/**", "pipeline"),
    edge("flow:pipeline->target", "flow", "pipeline", "target"),
  ],
);

const PIPELINE_STATES = new Map([["library:transformations/bronze", "succeeded"], ["pipeline", "running"], ["target", "pending"]]);

const ids = (markup: string) => markup.replace(/«r[0-9a-z]+»|:r[0-9a-z]+:/g, "«id»");

function oracleDrawing(definition: DiagramDefinition, model: DatabricksModel, states: ReadonlyMap<string, string>, ariaLabel: string): string {
  const view = render(
    <div className="databricks-canvas canvas-host" role="application" aria-label={ariaLabel}>
      <DiagramCanvas
        definition={definition}
        model={handWrittenModel(model, states)}
        events={{}}
        source={{ entryId: ENTRY, path: ["databricks.yml"] }}
        toolboxItems={emptyPalette}
        ariaLabel={ariaLabel}
        className="databricks-surface"
        scrollbarsClassName="databricks-scrollbars"
      />
    </div>,
  );
  const markup = ids(view.container.innerHTML);
  view.unmount();
  return markup;
}

function compiledDrawing(Canvas: typeof JobCanvas, model: DatabricksModel, states: Map<string, string>): string {
  currentModel = model;
  currentStates = states;
  const view = render(<Canvas projectId={PROJECT} entryId={ENTRY} path={["databricks.yml"]} />);
  const markup = ids(view.container.innerHTML);
  view.unmount();
  return markup;
}

/** Replaces every match, failing where there is none: a stated difference that no longer occurs is not one. */
function replaced(markup: string, pattern: RegExp, replacement: string, expected: number): string {
  const found = markup.match(pattern)?.length ?? 0;
  expect(found, String(pattern)).toBe(expected);
  return markup.replace(pattern, replacement);
}

/** The oracle with the bundle definition's decisions taken in: a target on the background layer, an override dashed by its style rather than by its line's class. */
const BUNDLE_ORACLE: DiagramDefinition = assertValidDiagramDefinition({
  ...HAND_WRITTEN_RENDER_ONLY,
  elementTypes: HAND_WRITTEN_RENDER_ONLY.elementTypes.map((type) => (type.id === "frame" ? { ...type, shape: "box", beneathConnections: true } : type)),
  relationTypes: HAND_WRITTEN_RENDER_ONLY.relationTypes.map((relation) => (relation.id === "override" ? { ...relation, style: { ...relation.style, dash: [3, 3] }, lineClassName: undefined } : relation)),
});

describe("the databricks compiled definitions", () => {
  it("draw the job as the hand-written definition drew it, element for element", () => {
    const actual = compiledDrawing(JobCanvas, JOB, JOB_STATES);

    // The cases the comparison would pass vacuously on: a canvas that drew none of them.
    for (const drawn of ["databricks-node-condition", "databricks-node-missing", "databricks-edge-outcome-true", "databricks-edge-outcome-false", "AT_LEAST_ONE_FAILED · serverless", "15.4.x-scala2.12 · Standard_DS3_v2 · 2 workers", "library-arrow"]) {
      expect(actual, drawn).toContain(drawn);
    }
    for (const state of ["pending", "running", "succeeded", "failed", "skipped"]) {
      expect(actual, state).toContain(`databricks-sim-${state}`);
    }
    expect(actual).toBe(oracleDrawing(HAND_WRITTEN_CONNECTABLE, JOB, JOB_STATES, "Databricks job"));
  });

  it("draw the bundle as the hand-written definition drew it, but for the decisions its definition takes", () => {
    let actual = compiledDrawing(BundleCanvas, BUNDLE, BUNDLE_STATES);

    for (const drawn of ["development · default", "production · 2 overrides", "1 override", "databricks-frame-outline", "databricks-override", "stroke-dasharray: 3 3", "resources.dashboards", "databricks-sim-running"]) {
      expect(actual, drawn).toContain(drawn);
    }
    // The bare target's summary, drawn empty.
    actual = replaced(actual, /<text class="library-element-label databricks-badges" x="792" y="418" style="text-anchor: start;"><\/text>/g, "", 1);
    expect(actual).toBe(oracleDrawing(BUNDLE_ORACLE, BUNDLE, BUNDLE_STATES, "Databricks bundle"));
  });

  it("draw the pipeline as the hand-written definition drew it, element for element", () => {
    const actual = compiledDrawing(PipelineCanvas, PIPELINE, PIPELINE_STATES);

    for (const drawn of ["Bronze to gold", "serverless · CURRENT", "lakehouse_dev.gold", "data-eng@vrenken.eu", "glob", "databricks-edge-flow"]) {
      expect(actual, drawn).toContain(drawn);
    }
    expect(actual).toBe(oracleDrawing(HAND_WRITTEN_RENDER_ONLY, PIPELINE, PIPELINE_STATES, "Databricks pipeline"));
  });

  it("declare each reading's simulated action as a menu entry the canvas runs, on the types its operation is for", () => {
    const simulated = (definition: DiagramDefinition) => (definition.actions ?? []).filter((action) => action.id.includes(".simulated."));
    expect(simulated(JOB_DEFINITION)).toEqual([{ id: "databricks.simulated.run-job", invokedBy: [{ kind: "menu" }], appliesTo: [{ kind: "element", elementTypes: ["task"] }] }]);
    expect(simulated(BUNDLE_DEFINITION)).toEqual([{ id: "databricks.simulated.deploy", invokedBy: [{ kind: "menu" }], appliesTo: [{ kind: "element", elementTypes: ["target"] }] }]);
    expect(simulated(PIPELINE_DEFINITION)).toEqual([{ id: "databricks.simulated.pipeline-update", invokedBy: [{ kind: "menu" }], appliesTo: [{ kind: "element", elementTypes: ["pipeline"] }] }]);
  });

  it("are what the canvases draw: each definition is compiled from its bundled definition", () => {
    // The canary for the tests above: were a canvas still holding its own definition, they would
    // compare the oracle with a copy of itself.
    for (const [definition, text] of [[JOB_DEFINITION, jobText], [BUNDLE_DEFINITION, bundleText], [PIPELINE_DEFINITION, pipelineText]] as const) {
      expect(definition).toEqual(compiledDefinition(text));
      const compiled = compileNotation(parseDisl(text), DATABRICKS_BINDINGS);
      expect(definition.elementTypes).toEqual(compiled.elementTypes);
      expect(definition.relationTypes).toEqual(compiled.relationTypes);
    }
  });
});
