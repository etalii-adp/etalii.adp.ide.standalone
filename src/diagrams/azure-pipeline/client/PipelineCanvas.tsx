import { useMemo, useState } from "react";

import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  CustomRouteRef,
  DiagramDefinition,
  ElementTypeDefinition,
  ShapeBounds,
} from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { useContextConnection, useContextPrompt, useContextProblems } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import { PipelineElementKindProto } from "@client/generated/azure-pipeline_pb";
import { indicatorsOf, problemMarkOf, problemsOn } from "./pipelineIndicators";
import {
  endpointsOf,
  JOB_TYPE,
  STAGE_TYPE,
  TEMPLATE_TYPE,
  type PipelineNode,
} from "./pipelineModel";
import { usePipelineStream } from "./usePipelineStream";


/** The fixed control reach the "waits for" arrows have always had. */
const EDGE_REACH = 30;

export interface PipelineCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}






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

const PIPELINE_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
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
  ],
  layout: { modes: ["manual"] },
  dragging: "disabled",
});

/** The definition id a wire node type draws as. */
function elementTypeOf(node: PipelineNode): string {
  return node.type === STAGE_TYPE ? "stage" : node.type === JOB_TYPE ? "job" : node.type === TEMPLATE_TYPE ? "template" : "step";
}

/**
 * Renders one Azure pipeline: its stages left to right, the jobs inside whichever stages are
 * open, and the arrows saying what waits for what - drawn through the central canvas library.
 *
 * Everything drawn here was decided by the backend - where each box goes, how big it is,
 * whether an edge is implicit or broken. The canvas is a renderer rather than a second
 * opinion about what a pipeline is. What this deliberately does not draw is any particular
 * *run*: which stage passed, which failed, how long it took (Requirement 8.8).
 */
export function PipelineCanvas({ projectId, entryId, path }: PipelineCanvasProps) {
  const { model, loading, failed, reportView } = usePipelineStream(projectId, path);
  const {  } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  // Problems arrive for the whole project, so an element only wears the ones that name it and
  // this file - two pipelines may each have a stage called Build (Requirement 8.7).
  const problems = useContextProblems()?.problems ?? [];

  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const { prompt, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel } = useContextPrompt();
  const editingId = inlineLabelElementIdOf(prompt);

  const diagramModel = useMemo<DiagramModel>(() => {
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
  }, [model, problems, path]);

  // The library sends the declared keystroke and shows its refusal on the one line it draws around
  // every canvas, clearing it when the next gesture is sent (client-centralization Requirement 2).
  const events: DiagramEventHandlers = {
    // Selection is the library's (centralized-selection), and so is its highlight: this canvas
    // used to draw a private `focusedId` it set on a press, beside the backend's selection -
    // two answers to "what is selected", with the drawing following the wrong one.
    onViewChanged: ({ viewport: next }) => setViewport(next),
  };

  useViewReport({
    view: { x: viewport?.x ?? 0, y: viewport?.y ?? 0, w: viewport?.width ?? 0, h: viewport?.height ?? 0 },
    report: reportView,
    convert: () => ({
      minX: viewport?.x ?? 0,
      minY: viewport?.y ?? 0,
      maxX: (viewport?.x ?? 0) + (viewport?.width ?? 0),
      maxY: (viewport?.y ?? 0) + (viewport?.height ?? 0),
    }),
    ready: !loading && !failed && viewport !== null,
  });


  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas.
  return (
    <div className="pipeline-canvas" data-testid="pipeline-canvas">
      <DiagramCanvas
        definition={PIPELINE_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        editing={{ editingId, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel }}
        ariaLabel={`Pipeline ${path.join("/")}`}
        className="pipeline-canvas-surface"
        scrollbarsClassName="pipeline-scrollbars"
      />
    </div>
  );
}
