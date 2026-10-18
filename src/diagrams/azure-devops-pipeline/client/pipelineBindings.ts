import type { ClassDeclaration, CustomRouteRef, DataAttributes, DecorationDeclaration } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the pipeline canvas needs beside its bundled definition, because the library cannot read it
 * from there. `compileNotation` derives everything else from `definition/azure-devops-pipeline.dis`:
 * the five node types and their shapes, labels, tooltips and accessible names, the seven indicators
 * packed into the definition's `badgeLayout` in its order, the one relation with its marker, the
 * rename, the step moves and their keys, the manual layout and the refused drag.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL paths and conditions.** The library has no CEL, so what the definition derives from
 *   the file - a label that falls back to the kind's name, whether an element is disabled, how many
 *   of it run - is read from the payload the backend sends (`PipelineElementMapper`), under the name
 *   that payload gives it, or from a flag the canvas sets beside it (`pipelineModel`'s `drawnPayload`).
 * - **The class names.** They tie the drawing to `azure-pipeline.css`, which paints the theme's
 *   colours; the definition's styles say the same in tokens.
 * - **The route.** DISL's `bezier` with a curvature of 30 leaving right and entering left says the
 *   curve; its exact shape is the column layout's fixed horizontal reach.
 * - **The problem mark and its classes**: where the problems service's findings are drawn, which is
 *   the host's and not the definition's (the definition states only the `invalid` and `warning`
 *   strokes, which the classes paint).
 * - **The test ids** the canvas's tests and the browser pass find each element and indicator by.
 * - **F2.** The backend resolves the rename by its key rather than its id, as it always has.
 */

/** The fixed control reach the "waits for" arrows have always had: the definition's `curvature`. */
const EDGE_REACH = 30;

/**
 * One "waits for" arrow: a horizontal cubic from the source's right edge to the target's left edge
 * with a fixed control reach - the column-layout case, where a midpoint-based curve would flatten out.
 */
export const waitsForRoute: CustomRouteRef = {
  customRoute: "pipeline-waits-for",
  path: (from, to, _waypoints, ends) => {
    const a = ends ? { x: ends.source.x + ends.source.width, y: ends.source.y + ends.source.height / 2 } : from;
    const b = ends ? { x: ends.target.x, y: ends.target.y + ends.target.height / 2 } : to;
    return `M ${a.x} ${a.y} C ${a.x + EDGE_REACH} ${a.y}, ${b.x - EDGE_REACH} ${b.y}, ${b.x} ${b.y}`;
  },
};

/** Which class each node type's box carries in `azure-pipeline.css`. */
const BOX_CLASSES: Readonly<Record<string, readonly ClassDeclaration[]>> = {
  Stage: [
    { className: "pipeline-stage", on: "element" },
    { className: "pipeline-stage-expanded", on: "element", when: { path: "payload.expanded", is: "true" } },
    { className: "pipeline-stage-collapsed", on: "element", when: { path: "payload.expanded", is: "false" } },
  ],
  Job: [{ className: "pipeline-job", on: "element" }],
  DeploymentJob: [{ className: "pipeline-job", on: "element" }, { className: "pipeline-deployment", on: "element" }],
  Step: [{ className: "pipeline-step", on: "element" }],
  TemplateReference: [{ className: "pipeline-template", on: "element" }],
};

/** What every kind wears for what it is told: decided by an expression, from a template, carrying a problem. */
const STATE_CLASSES: readonly ClassDeclaration[] = [
  { className: "pipeline-indeterminate", on: "element", when: { path: "payload.indeterminate", is: "true" } },
  { className: "pipeline-from-template", on: "element", when: { path: "payload.fromTemplate", is: "true" } },
  { className: "pipeline-problem", on: "element", when: { path: "payload.problemSeverity", is: "present" } },
  { className: { template: "pipeline-problem-{payload.problemSeverity}" }, on: "element", when: { path: "payload.problemSeverity", is: "present" } },
];

function classNamesOf(type: string): readonly ClassDeclaration[] {
  const kind = BOX_CLASSES[type];
  if (kind === undefined) {
    throw new Error(`pipelineBindings: the node type "${type}" has no class in azure-pipeline.css.`);
  }

  return [...kind, ...STATE_CLASSES, { className: type === "Stage" ? "pipeline-stage-box" : "pipeline-box", on: "shape" }];
}

/**
 * The problem mark: WHERE a problem is shown and what colours it, while the problems service says
 * what a problem IS. A stage's sits further in from its corner than a box's, as it always has.
 */
function problemMark(type: string): DecorationDeclaration {
  const inset = type === "Stage" ? { x: -12, y: -12 } : { x: -8, y: -6 };
  return {
    glyph: "marker",
    anchor: "canvas",
    from: { x: { path: "bounds.right", number: { plus: inset.x } }, y: { path: "bounds.bottom", number: { plus: inset.y } } },
    text: { path: "payload.problemGlyph" },
    tooltip: { path: "payload.problemTitle" },
    textAnchor: "end",
    className: { template: "pipeline-problem-mark pipeline-problem-mark-{payload.problemSeverity}" },
    accessibility: { role: "img", label: { path: "payload.problemTitle" } },
    data: { testid: { template: "problem-{element.id}" } },
    when: { path: "payload.problemSeverity", is: "present" },
  };
}

/** The rename's wire id (`x-azure-pipeline.actions.editLabel`). */
const RENAME = "azure-pipeline.rename";

/** What each label shows: the display name the backend measured the box for. */
const NAME = "payload.displayName";

export const PIPELINE_BINDINGS: NotationBindings = {
  wireIds: "x-azure-pipeline",
  celPaths: {
    // What a sentence calls an element, its display name else its name: the display name the backend
    // already falls back for.
    "self.label": NAME,
    "self.reference": NAME,
    // Each kind's name line, which falls back to the kind's own identifying value: the display name
    // the backend sends already does (`PipelineStage.Label`, `PipelineJob.Label`, `PipelineStep.Label`).
    "self.label != '' ? self.label : 'stage'": NAME,
    "self.label != '' ? self.label : 'job'": NAME,
    "self.displayName != '' ? self.displayName : (has(self.identifier) && self.identifier != '' ? self.identifier.split('\\n')[0] : 'step')": NAME,
    // Why a template is not followed, the sentence the backend sends (`PipelineTemplateUnresolved.Explanation`).
    "self.explanation": "payload.unresolvedReason",
    "self.template": "payload.templatePath",
    "string(self.multiplicity)": "payload.multiplicity",
    "(has(self.environment) ? self.environment : '')": "payload.environment",
  },
  celConditions: {
    "self.view.collapsed": { path: "payload.expanded", is: "false" },
    "self.isDisabled": { path: "payload.enabled", is: "false" },
    "self.manualTrigger": { path: "payload.triggerIsManual", is: "true" },
    "has(self.condition) && self.condition != ''": { path: "payload.condition", is: "non-empty" },
    "has(self.multiplicity) && self.multiplicity > 1": { path: "payload.multiplied", is: "true" },
    "!(has(self.multiplicity) && self.multiplicity > 1) && has(self.multiplicityExpression)": { path: "payload.multiplicityUnknown", is: "true" },
  },
  classNames: (type) => classNamesOf(type),
  labelClassName: (type, label) => (type === "Stage" ? (label.id === "jobs" ? "pipeline-stage-count" : "pipeline-stage-name") : "pipeline-box-name"),
  labelEditorBox: (type, label) => (type === "Stage" && label.id === "name" ? { top: 6, height: 24 } : undefined),
  badgeClassName: () => "pipeline-indicator",
  // The indicator's id as the canvas has always written it: `continueOnError` is `continue-on-error`.
  badgeData: (_type, badge) => ({ testid: `indicator-${badge.id.replace(/[A-Z]/g, (letter) => `-${letter.toLowerCase()}`)}` }),
  elementData: (type): DataAttributes =>
    type === "Stage" ? { testid: { template: "stage-{element.id}" }, expanded: { path: "payload.expanded" } } : { testid: { template: "node-{element.id}" } },
  elementDecorations: (type) => [problemMark(type)],
  relationClassName: () => "pipeline-edge",
  // Every box is a thing to pick, as it always was; a stage says it is a group of jobs.
  accessibleRole: "button",
  // A dependency is never drawn by hand: no end starts one, and a target attaches by its edge.
  endpointAnchors: (_relation, end) => (end === "source" ? [] : "edge"),
  builtInShapes: { roundedRect: "rounded-rectangle" },
  customRoutes: { DependsOn: waitsForRoute, DefaultOrder: waitsForRoute },
  // The backend resolves the rename by its key, F2, as it always has; every other action is sent by its id.
  action: (action) => (action.id === RENAME ? { ...action, backendKey: "F2" } : action),
};
