import { useMemo, useState } from "react";

import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  CustomRouteRef,
  CustomShapeRef,
  DiagramDefinition,
  ShapeBounds,
  ShapePoint,
} from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { innermostKey, useContextConnection, useContextPrompt, useContextProblems, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import { PipelineElementKindProto } from "@client/generated/azure-pipeline_pb";
import { indicatorsOf, problemMarkOf, problemsOn, type PipelineIndicator } from "./pipelineIndicators";
import {
  endpointsOf,
  jobCountLabel,
  JOB_TYPE,
  STAGE_TYPE,
  TEMPLATE_TYPE,
  type PipelineNode,
} from "./pipelineModel";
import { usePipelineStream } from "./usePipelineStream";

/** The stage card's name line: baseline 24 with the job count under it, so the editor covers the name alone. */
const STAGE_NAME_TOP = 6;
const STAGE_NAME_HEIGHT = 24;

/** The fixed control reach the "waits for" arrows have always had. */
const EDGE_REACH = 30;

export interface PipelineCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

type ProblemMarkData = { severity: string; title: string } | null;

/** An element as the library carries it here: the model node plus what it draws. */
type PipelineElement = DiagramModelElement & {
  node: PipelineNode;
  expanded: boolean;
  focused: boolean;
  problem: ProblemMarkData;
};

function sideEdgePoint(bounds: ShapeBounds, towards: ShapePoint): ShapePoint {
  const centreX = bounds.x + bounds.width / 2;
  return {
    x: towards.x >= centreX ? bounds.x + bounds.width : bounds.x,
    y: bounds.y + bounds.height / 2,
  };
}

/**
 * The badges along the top-right of an element: what it is telling you without being opened
 * (Requirement 8.4). Laid out right to left so the first one is nearest the corner and adding
 * another does not move the ones already there.
 */
function PipelineIndicators({ indicators, x, y }: { indicators: PipelineIndicator[]; x: number; y: number }) {
  return (
    <>
      {indicators.map((indicator, index) => (
        <text
          key={indicator.key}
          className="pipeline-indicator"
          data-testid={`indicator-${indicator.key}`}
          x={x - index * 16}
          y={y}
          textAnchor="end"
        >
          {indicator.glyph}
          <title>{indicator.title}</title>
        </text>
      ))}
    </>
  );
}

/**
 * The mark on an element something is wrong with, so a dangling dependsOn is visible where it
 * is rather than only in a list (Requirement 8.7).
 */
function ProblemMark({
  elementId,
  problem,
  x,
  y,
}: {
  elementId: string;
  problem: { severity: string; title: string };
  x: number;
  y: number;
}) {
  return (
    <text
      className={`pipeline-problem-mark pipeline-problem-mark-${problem.severity}`}
      data-testid={`problem-${elementId}`}
      x={x}
      y={y}
      textAnchor="end"
      role="img"
      aria-label={problem.title}
    >
      {problem.severity === "error" ? "✖" : "⚠"}
      <title>{problem.title}</title>
    </text>
  );
}

/**
 * A stage: a container holding its jobs when open, a single box with its job count when
 * closed (Requirement 8.2). Painted beneath the connections, so the arrows between the jobs
 * it holds stay visible over its card. A manual-trigger stage is marked, because "this one
 * waits for a person" is not something a reader should have to open the file to discover
 * (Requirement 8.3).
 */
const stageShape: CustomShapeRef = {
  customShape: "pipeline-stage",
  render: (raw) => {
    const element = raw as PipelineElement;
    const { displayName, width, height, jobCount, indeterminate, fromTemplate } = element.node.payload;
    const classes = [
      "pipeline-stage",
      element.expanded ? "pipeline-stage-expanded" : "pipeline-stage-collapsed",
      element.focused ? "pipeline-focused" : "",
      indeterminate ? "pipeline-indeterminate" : "",
      fromTemplate ? "pipeline-from-template" : "",
      element.problem ? `pipeline-problem pipeline-problem-${element.problem.severity}` : "",
    ]
      .filter(Boolean)
      .join(" ");

    return (
      <BoxElement
        className={classes}
        x={element.x - width / 2}
        y={element.y - height / 2}
        width={width}
        height={height}
        rx={6}
        label={displayName}
        boxClassName="pipeline-stage-box"
        labelClassName="pipeline-stage-name"
        labelX={12}
        labelY={24}
        role="button"
        aria-label={displayName}
        data-testid={`stage-${element.id}`}
        data-expanded={element.expanded}
      >
        {!element.expanded && (
          <text className="pipeline-stage-count" x={12} y={44}>
            {jobCountLabel(jobCount)}
          </text>
        )}
        <PipelineIndicators indicators={indicatorsOf(element.node.payload)} x={width - 12} y={20} />
        {element.problem && <ProblemMark elementId={element.id} problem={element.problem} x={width - 12} y={height - 12} />}
      </BoxElement>
    );
  },
  edgePoint: sideEdgePoint,
};

/** A job, a step or an unfollowed template: a plain box, drawn by what kind it says it is. */
const boxShape: CustomShapeRef = {
  customShape: "pipeline-box",
  render: (raw) => {
    const element = raw as PipelineElement;
    const { displayName, width, height, kind, indeterminate, fromTemplate, unresolvedReason } = element.node.payload;
    const deployment = kind === PipelineElementKindProto.PIPELINE_ELEMENT_KIND_DEPLOYMENT_JOB;
    const classes = [
      element.node.type === JOB_TYPE ? "pipeline-job" : element.node.type === TEMPLATE_TYPE ? "pipeline-template" : "pipeline-step",
      deployment ? "pipeline-deployment" : "",
      element.focused ? "pipeline-focused" : "",
      indeterminate ? "pipeline-indeterminate" : "",
      fromTemplate ? "pipeline-from-template" : "",
      element.problem ? `pipeline-problem pipeline-problem-${element.problem.severity}` : "",
    ]
      .filter(Boolean)
      .join(" ");

    return (
      <BoxElement
        className={classes}
        x={element.x - width / 2}
        y={element.y - height / 2}
        width={width}
        height={height}
        label={displayName}
        boxClassName="pipeline-box"
        labelClassName="pipeline-box-name"
        labelX={10}
        role="button"
        aria-label={displayName}
        data-testid={`node-${element.id}`}
      >
        <PipelineIndicators indicators={indicatorsOf(element.node.payload)} x={width - 8} y={16} />
        {element.problem && <ProblemMark elementId={element.id} problem={element.problem} x={width - 8} y={height - 6} />}
        {unresolvedReason.length > 0 && <title>{unresolvedReason}</title>}
      </BoxElement>
    );
  },
  edgePoint: sideEdgePoint,
};

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
const PIPELINE_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "stage",
      shape: stageShape,
      label: { placement: "inset", editable: true, insetTop: STAGE_NAME_TOP, insetHeight: STAGE_NAME_HEIGHT },
      anchors: { kind: "edge" },
      sizing: "model",
      deletable: false,
      beneathConnections: true,
    },
    { id: "job", shape: boxShape, label: { placement: "inside", editable: true }, anchors: { kind: "edge" }, sizing: "model", deletable: false },
    { id: "template", shape: boxShape, label: { placement: "inside", editable: true }, anchors: { kind: "edge" }, sizing: "model", deletable: false },
    { id: "step", shape: boxShape, label: { placement: "inside", editable: true }, anchors: { kind: "edge" }, sizing: "model", deletable: false },
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
  const { select, executeShortcut } = useContextConnection();
  const { selection } = useContextSelection();
  const selectedId = elementIdOfKey(innermostKey(selection ?? null));
  const toolboxItems = useToolboxItems(projectId, path);
  // Problems arrive for the whole project, so an element only wears the ones that name it and
  // this file - two pipelines may each have a stage called Build (Requirement 8.7).
  const problems = useContextProblems()?.problems ?? [];

  const [focusedId, setFocusedId] = useState<string | undefined>(undefined);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const { prompt, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel } = useContextPrompt();
  const editingId = inlineLabelElementIdOf(prompt);

  const diagramModel = useMemo<DiagramModel>(() => {
    const nodes = [...model.nodes.values()];
    // Stages first so the jobs inside them draw on top; the stage type's beneathConnections
    // keeps the arrows above the cards as well.
    const ordered = [...nodes.filter((node) => node.type === STAGE_TYPE), ...nodes.filter((node) => node.type !== STAGE_TYPE)];
    const elements = ordered.map((node): PipelineElement => ({
      id: node.id,
      type: elementTypeOf(node),
      x: node.x + node.payload.width / 2,
      y: node.y + node.payload.height / 2,
      width: node.payload.width,
      height: node.payload.height,
      label: node.payload.displayName,
      node,
      expanded: !model.collapsed.has(node.id),
      focused: node.id === focusedId,
      problem: problemMarkOf(problemsOn(problems, path, node.id)),
    }));
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
  }, [model, focusedId, problems, path]);

  /** The backend's push is the selection; the canvas renders it and never decides. */
  const librarySelection = useMemo<DiagramSelection>(() => {
    if (selectedId === null || !model.nodes.has(selectedId)) {
      return [];
    }
    return [{ kind: "element", id: selectedId }];
  }, [selectedId, model.nodes]);

  const events: DiagramEventHandlers = {
    onSelectionChanged: ({ selection: next }) => {
      // A press on an arrow deselects, as it always did: an edge was never a selectable
      // element here, and the old canvas let such a press fall through to the background.
      const element = next.find((item) => item.kind === "element");
      setFocusedId(element?.id);
      select(element !== undefined ? elementSelectionOf(entryId, path, element.id) : null);
    },
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

  /**
   * F2 on the selected element, forwarded as data through the same seam every other canvas
   * uses: the backend maps the key to its action, so no key-to-action table lives here.
   */
  const onKeyDown = async (event: React.KeyboardEvent) => {
    if (!selectedId || isTextTarget(event.target)) {
      return;
    }

    const shortcut = structuralShortcutFor(event, ["F2"]);
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    await executeShortcut(shortcut, elementSourceOf(selectedId));
  };

  if (failed) {
    return (
      <div className="pipeline-canvas" data-testid="pipeline-canvas">
        <div className="pipeline-canvas-unavailable" role="alert">
          This pipeline is no longer available at {path.join("/")}.
        </div>
      </div>
    );
  }

  return (
    <div className="pipeline-canvas" data-testid="pipeline-canvas" onKeyDown={onKeyDown}>
      {loading ? (
        <div className="pipeline-canvas-loading" role="status">
          Loading…
        </div>
      ) : (
        <DiagramCanvas
          definition={PIPELINE_DEFINITION}
          model={diagramModel}
          events={events}
          selection={librarySelection}
          toolboxItems={toolboxItems}
          editing={{ editingId, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel }}
          ariaLabel={`Pipeline ${path.join("/")}`}
          className="pipeline-canvas-surface"
          scrollbarsClassName="pipeline-scrollbars"
        />
      )}
    </div>
  );
}
