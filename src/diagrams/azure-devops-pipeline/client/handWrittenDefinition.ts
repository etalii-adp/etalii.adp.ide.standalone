import type { CustomRouteRef, DiagramDefinition, ElementTypeDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import type { Problem } from "@client/generated/problems_pb";
import { PipelineElementKindProto, type PipelineElementPayload } from "@client/generated/azure-pipeline_pb";
import { endpointsOf, JOB_TYPE, problemMarkOf, problemsOn, STAGE_TYPE, TEMPLATE_TYPE, type PipelineModel, type PipelineNode } from "./pipelineModel";

/**
 * THE ORACLE, AND TEST CODE ONLY: the canvas definition `PipelineCanvas.tsx` stated by hand until it
 * was compiled from the bundled definition, moved here unchanged with the model it built and the
 * indicators `pipelineIndicators.ts` worked out, so `pipelineCompiledDefinition.test.tsx` can hold the
 * compiled drawing to it. Nothing in the product imports this file.
 *
 * It had one `job` type for a job and a deployment job, told apart by a payload class, one
 * `waits-for` relation told apart by a class on each connection, and the indicators as a list the
 * model computed and one decoration drew with `each`.
 */

const MOVE_STEP_UP = "azure-pipeline.move-step-up";
const MOVE_STEP_DOWN = "azure-pipeline.move-step-down";

/** The fixed control reach the "waits for" arrows have always had. */
const EDGE_REACH = 30;

/**
 * One "waits for" arrow, exactly as FixedBezierConnection drew it: a horizontal cubic from
 * the source's right edge to the target's left edge with a fixed control reach - the
 * column-layout case, where a midpoint-based curve would flatten out.
 */
const waitsForRoute: CustomRouteRef = {
  customRoute: "pipeline-waits-for",
  path: (from, to, _waypoints, ends) => {
    const a = ends ? { x: ends.source.x + ends.source.width, y: ends.source.y + ends.source.height / 2 } : from;
    const b = ends ? { x: ends.target.x, y: ends.target.y + ends.target.height / 2 } : to;
    return `M ${a.x} ${a.y} C ${a.x + EDGE_REACH} ${a.y}, ${b.x - EDGE_REACH} ${b.y}, ${b.x} ${b.y}`;
  },
};

/**
 * What a pipeline diagram allows, stated once: nothing moves, nothing connects, nothing
 * deletes - the backend decides every box and every arrow, and this canvas renders them.
 * Stages paint beneath the connections so the arrows between their jobs stay visible; the
 * one relation is render-only, its implicit/broken stylings carried per connection.
 */

/** Which class each box kind carries - what the renderer chose with a nested ternary. */
const BOX_CLASS = { job: "pipeline-job", template: "pipeline-template", step: "pipeline-step" } as const;

export const HAND_WRITTEN_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "stage",
      shape: "rounded-rectangle",
      style: { cornerRadius: 6 },
      classNames: [
        { className: "pipeline-stage", on: "element" },
        { className: "pipeline-stage-expanded", on: "element", when: { path: "payload.expanded", is: "true" } },
        { className: "pipeline-stage-collapsed", on: "element", when: { path: "payload.expanded", is: "false" } },
        { className: "pipeline-indeterminate", on: "element", when: { path: "payload.indeterminate", is: "true" } },
        { className: "pipeline-from-template", on: "element", when: { path: "payload.fromTemplate", is: "true" } },
        { className: "pipeline-problem", on: "element", when: { path: "payload.problemSeverity", is: "present" } },
        { className: { template: "pipeline-problem-{payload.problemSeverity}" }, on: "element", when: { path: "payload.problemSeverity", is: "present" } },
        { className: "pipeline-stage-box", on: "shape" },
      ],
      data: { testid: { template: "stage-{element.id}" }, expanded: { path: "payload.expanded" } },
      accessibility: { role: "button", label: { path: "payload.displayName" } },
      labels: [
        {
          text: { path: "payload.displayName" },
          anchorTo: "top",
          offset: { x: 0, y: 24 },
          align: "start",
          insetX: 12,
          editable: true,
          editorBox: { top: 6, height: 24 },
          className: "pipeline-stage-name",
        },
        {
          // The job count a closed stage shows in place of the jobs themselves - a count that
          // declines its noun, which is the whole of `plural`'s justification.
          text: { path: "payload.jobCount", plural: { one: "job", other: "jobs" } },
          anchorTo: "top",
          offset: { x: 0, y: 44 },
          align: "start",
          insetX: 12,
          when: { path: "payload.expanded", is: "false" },
          className: "pipeline-stage-count",
        },
      ],
      decorations: [
        {
          // ONE GLYPH PER THING THE MODEL SAYS, which is why decorations take an `each`: a
          // stage may be disabled, manual, conditional, tolerant of failure and multiplied, and
          // a fixed list of declarations cannot say "as many as there are".
          glyph: "marker",
          each: { path: "payload.indicators" },
          // Canvas units: these read `bounds`, which resolves to a position rather than an offset.
          anchor: "canvas",
          from: { x: { path: "bounds.right", number: { plus: -12 } }, y: { path: "bounds.top", number: { plus: 20 } } },
          step: { x: -16, y: 0 },
          text: { path: "glyph" },
          tooltip: { path: "title" },
          textAnchor: "end",
          className: "pipeline-indicator",
          data: { testid: { template: "indicator-{key}" } },
        },
        {
          // The problem mark: WHERE a problem is shown and what colours it, while the module's
          // model still says what a problem IS. That boundary is deliberate - the problems
          // service is not this canvas's to own.
          glyph: "marker",
          // Canvas units: these read `bounds`, which resolves to a position rather than an offset.
          anchor: "canvas",
          from: { x: { path: "bounds.right", number: { plus: -12 } }, y: { path: "bounds.bottom", number: { plus: -12 } } },
          text: { path: "payload.problemGlyph" },
          tooltip: { path: "payload.problemTitle" },
          textAnchor: "end",
          className: { template: "pipeline-problem-mark pipeline-problem-mark-{payload.problemSeverity}" },
          accessibility: { role: "img", label: { path: "payload.problemTitle" } },
          data: { testid: { template: "problem-{element.id}" } },
          when: { path: "payload.problemSeverity", is: "present" },
        },
      ],
      anchors: { kind: "edge" },
      sizing: "model",
      deletable: false,
      beneathConnections: true,
    },
    ...(["job", "template", "step"] as const).map((id): ElementTypeDefinition => ({
      id,
      shape: "box" as const,
      classNames: [
        { className: BOX_CLASS[id], on: "element" },
        { className: "pipeline-deployment", on: "element", when: { path: "payload.deployment", is: "true" } },
        { className: "pipeline-indeterminate", on: "element", when: { path: "payload.indeterminate", is: "true" } },
        { className: "pipeline-from-template", on: "element", when: { path: "payload.fromTemplate", is: "true" } },
        { className: "pipeline-problem", on: "element", when: { path: "payload.problemSeverity", is: "present" } },
        { className: { template: "pipeline-problem-{payload.problemSeverity}" }, on: "element", when: { path: "payload.problemSeverity", is: "present" } },
        { className: "pipeline-box", on: "shape" as const },
      ],
      data: { testid: { template: "node-{element.id}" } },
      accessibility: { role: "button", label: { path: "payload.displayName" } },
      tooltip: { path: "payload.unresolvedReason", when: { path: "payload.unresolvedReason", is: "non-empty" } },
      labels: [
        {
          text: { path: "payload.displayName" },
          align: "start" as const,
          insetX: 10,
          editable: true,
          className: "pipeline-box-name",
        },
      ],
      decorations: [
        {
          // ONE GLYPH PER THING THE MODEL SAYS, which is why decorations take an `each`: a
          // stage may be disabled, manual, conditional, tolerant of failure and multiplied, and
          // a fixed list of declarations cannot say "as many as there are".
          glyph: "marker",
          each: { path: "payload.indicators" },
          // Canvas units: these read `bounds`, which resolves to a position rather than an offset.
          anchor: "canvas",
          from: { x: { path: "bounds.right", number: { plus: -8 } }, y: { path: "bounds.top", number: { plus: 16 } } },
          step: { x: -16, y: 0 },
          text: { path: "glyph" },
          tooltip: { path: "title" },
          textAnchor: "end",
          className: "pipeline-indicator",
          data: { testid: { template: "indicator-{key}" } },
        },
        {
          // The problem mark: WHERE a problem is shown and what colours it, while the module's
          // model still says what a problem IS. That boundary is deliberate - the problems
          // service is not this canvas's to own.
          glyph: "marker",
          // Canvas units: these read `bounds`, which resolves to a position rather than an offset.
          anchor: "canvas",
          from: { x: { path: "bounds.right", number: { plus: -8 } }, y: { path: "bounds.bottom", number: { plus: -6 } } },
          text: { path: "payload.problemGlyph" },
          tooltip: { path: "payload.problemTitle" },
          textAnchor: "end",
          className: { template: "pipeline-problem-mark pipeline-problem-mark-{payload.problemSeverity}" },
          accessibility: { role: "img", label: { path: "payload.problemTitle" } },
          data: { testid: { template: "problem-{element.id}" } },
          when: { path: "payload.problemSeverity", is: "present" },
        },
      ],
      anchors: { kind: "edge" as const },
      sizing: "model" as const,
      deletable: false,
    })),
  ],
  relationTypes: [
    {
      id: "waits-for",
      route: waitsForRoute,
      style: { endMarker: "arrow" },
      className: "pipeline-edge",
      endpoints: {
        source: { elementTypes: ["stage", "job", "template", "step"], anchors: [] },
        target: { elementTypes: ["stage", "job", "template", "step"], anchors: "edge" },
        allowSelf: false,
      },
    },
  ],
  // WHAT THIS TYPE OFFERS, AND WHAT INVOKES IT. The key list was hand-written in this canvas
  // and the delete was a keystroke it built to describe a gesture the library had already
  // handed it. Declared, the library derives the key set and dispatches an action id.
  actions: [
    { id: "rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
    // A move is sent by its id rather than a backendKey, which carries a key without its modifier.
    { id: MOVE_STEP_UP, invokedBy: [{ kind: "shortcut", key: "ArrowUp", alt: true }], appliesTo: [{ kind: "element", elementTypes: ["step"] }] },
    { id: MOVE_STEP_DOWN, invokedBy: [{ kind: "shortcut", key: "ArrowDown", alt: true }], appliesTo: [{ kind: "element", elementTypes: ["step"] }] },
  ],
  layout: { modes: ["manual"] },
  dragging: "disabled",
});

/** The definition id a wire node type draws as. */
function elementTypeOf(node: PipelineNode): string {
  return node.type === STAGE_TYPE ? "stage" : node.type === JOB_TYPE ? "job" : node.type === TEMPLATE_TYPE ? "template" : "step";
}

/** One thing worth saying about an element, as a badge on it. */
export interface PipelineIndicator {
  /** Stable within an element, so React has a key and a test has something to ask for. */
  key: string;
  /** The glyph drawn on the canvas. */
  glyph: string;
  /** What it means, in words, for the tooltip and for a screen reader. */
  title: string;
}

/**
 * The badges an element carries.
 *
 * Order is fixed rather than incidental: the ones that change *whether* an element runs come
 * before the ones that change *how*, because that is the order a reader asks the questions in.
 */
export function indicatorsOf(payload: PipelineElementPayload): PipelineIndicator[] {
  const indicators: PipelineIndicator[] = [];

  if (!payload.enabled) {
    indicators.push({ key: "disabled", glyph: "⊘", title: "Disabled: this will not run." });
  }

  if (payload.triggerIsManual) {
    indicators.push({
      key: "manual",
      glyph: "▶",
      title: "Manual trigger: this waits to be started by a person.",
    });
  }

  if (payload.condition.length > 0) {
    indicators.push({
      key: "condition",
      glyph: "?",
      // The condition itself, verbatim - a reader who knows Azure's functions can read it, and
      // one who does not is no worse off than with a paraphrase this module invented.
      title: `Runs only when: ${payload.condition}`,
    });
  }

  if (payload.continuesOnError) {
    indicators.push({
      key: "continue-on-error",
      glyph: "!",
      title: "Continues on error: a failure here does not fail the run.",
    });
  }

  if (payload.multiplicity > 1) {
    indicators.push({
      key: "multiplicity",
      glyph: `×${payload.multiplicity}`,
      title: `Runs ${payload.multiplicity} times, once per matrix entry.`,
    });
  } else if (payload.indeterminate && payload.multiplicity === 1 && payload.kind !== 0) {
    // A count decided at run time. Shown as a question rather than a number, because putting a
    // number here that nobody knows yet would be worse than admitting it depends.
    indicators.push({
      key: "multiplicity-unknown",
      glyph: "×?",
      title: "How many of these run is decided when the pipeline runs.",
    });
  }

  if (payload.fromTemplate) {
    indicators.push({
      key: "from-template",
      glyph: "⇱",
      title: `From ${payload.templatePath}: edit it there.`,
    });
  }

  return indicators;
}

/**
 * The model the canvas built for the hand-written definition: stages first, each element's payload
 * as the declaration read it, and an edge only where both its ends are drawn.
 */
export function handWrittenModel(model: PipelineModel, problems: readonly Problem[], path: readonly string[]): DiagramModel {
  const nodes = [...model.nodes.values()];
  // Stages first so the jobs inside them draw on top; the stage type's beneathConnections
  // keeps the arrows above the cards as well.
  const ordered = [...nodes.filter((node) => node.type === STAGE_TYPE), ...nodes.filter((node) => node.type !== STAGE_TYPE)];
  const elements = ordered.map((node): DiagramModelElement => {
    const problem = problemMarkOf(problemsOn(problems, path, node.id));
    // What the declaration reads. Which indicators a node shows, and what a problem is, stay
    // this module's knowledge; where they are drawn is the library's.
    return {
      id: node.id,
      type: elementTypeOf(node),
      x: node.x + node.payload.width / 2,
      y: node.y + node.payload.height / 2,
      width: node.payload.width,
      height: node.payload.height,
      label: node.payload.displayName,
      payload: {
        displayName: node.payload.displayName,
        jobCount: node.payload.jobCount,
        indeterminate: node.payload.indeterminate,
        fromTemplate: node.payload.fromTemplate,
        unresolvedReason: node.payload.unresolvedReason,
        deployment: node.payload.kind === PipelineElementKindProto.PIPELINE_ELEMENT_KIND_DEPLOYMENT_JOB,
        indicators: indicatorsOf(node.payload),
        expanded: !model.collapsed.has(node.id),
        ...(problem ? { problemSeverity: problem.severity, problemTitle: problem.title, problemGlyph: problem.severity === "error" ? "✖" : "⚠" } : {}),
      },
    };
  });
  // An edge whose ends are not both on the canvas is not drawn: a job's dependency inside a
  // collapsed stage has nowhere to start, and a line into empty space says less than no line.
  const connections = [...model.edges.values()].flatMap((edge) => {
    const ends = endpointsOf(model, edge);
    if (ends === null) {
      return [];
    }
    return [{
      id: edge.id,
      type: "waits-for",
      sourceId: ends.from.id,
      targetId: ends.to.id,
      className: [
        edge.payload.implicitDependency ? "pipeline-edge-implicit" : "",
        edge.payload.broken ? "pipeline-edge-broken" : "",
      ].filter(Boolean).join(" ") || undefined,
    }];
  });
  return { elements, connections };
}
