import { useMemo, useState } from "react";

import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { useContextConnection, useContextProblems } from "@client/shell/context/ContextConnectionProvider";
import { elementSourceOf } from "@client/canvas/selection";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import { PipelineElementKindProto } from "@client/generated/azure-pipeline_pb";
import disText from "../definition/azure-devops-pipeline.dis?raw";
import { PIPELINE_BINDINGS } from "./pipelineBindings";
import {
  endpointsOf,
  JOB_TYPE,
  problemMarkOf,
  problemsOn,
  STAGE_TYPE,
  STEP_TYPE,
  type PipelineNode,
} from "./pipelineModel";
import { usePipelineStream } from "./usePipelineStream";

export interface PipelineCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/**
 * What a pipeline diagram is, compiled from the bundled definition (`definition/azure-devops-pipeline.dis`)
 * as it is, with what the library cannot read there bound in `pipelineBindings.ts`: five kinds of box the backend places and sizes,
 * none moved, connected or deleted by hand; the name and job-count lines; the indicators packed into
 * the definition's badge layout; the "waits for" arrow, one relation per way of waiting; the rename,
 * the folding and the step moves and their keys; a manual layout with dragging refused.
 */
export const PIPELINE_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(parseDisl(disText), PIPELINE_BINDINGS));

/**
 * The declared actions this canvas sends by id. One declared with a backend key - the rename, which
 * the backend knows as F2 - is sent by the library as that keystroke, so it is not sent here too.
 */
const SENT_BY_ID: ReadonlySet<string> = new Set((PIPELINE_DEFINITION.actions ?? []).filter((action) => action.backendKey === undefined).map((action) => action.id));

/** The definition's node type a wire node draws as. */
function elementTypeOf(node: PipelineNode): string {
  switch (node.type) {
    case STAGE_TYPE:
      return "stage";
    case JOB_TYPE:
      return node.payload.kind === PipelineElementKindProto.PIPELINE_ELEMENT_KIND_DEPLOYMENT_JOB ? "deploymentjob" : "job";
    case STEP_TYPE:
      return "step";
    default:
      return "templatereference";
  }
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
  const { executeAction } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  // Problems arrive for the whole project, so an element only wears the ones that name it and
  // this file - two pipelines may each have a stage called Build (Requirement 8.7).
  const problems = useContextProblems()?.problems ?? [];

  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const diagramModel = useMemo<DiagramModel>(() => {
    const nodes = [...model.nodes.values()];
    // Stages first so the jobs inside them draw on top; the stage type's beneathConnections
    // keeps the arrows above the cards as well.
    const ordered = [...nodes.filter((node) => node.type === STAGE_TYPE), ...nodes.filter((node) => node.type !== STAGE_TYPE)];
    const elements = ordered.map((node): DiagramModelElement => {
      const problem = problemMarkOf(problemsOn(problems, path, node.id));
      const multiplicity = node.payload.multiplicity;
      // What the definition reads, under the names the bindings give it. What a problem is stays the
      // problems service's; which indicators show, and where, is the definition's.
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
          templatePath: node.payload.templatePath,
          unresolvedReason: node.payload.unresolvedReason,
          enabled: node.payload.enabled,
          triggerIsManual: node.payload.triggerIsManual,
          condition: node.payload.condition,
          continuesOnError: node.payload.continuesOnError,
          multiplicity,
          // The definition's `self.multiplicity > 1`, and its `has(self.multiplicityExpression)`: the
          // payload carries no expression, only that the count is decided when the pipeline runs.
          multiplied: multiplicity > 1,
          multiplicityUnknown: multiplicity === 1 && node.payload.indeterminate,
          environment: node.payload.environment,
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
        // The definition's two ways of waiting: the order a stage is written in, or a dependsOn.
        type: edge.payload.implicitDependency ? "defaultorder" : "dependson",
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
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId !== undefined && SENT_BY_ID.has(actionId)) {
        void executeAction(actionId, elementSourceOf(targetId, entryId));
      }
    },
    // A toolbox entry runs its operation on the box it is dropped on; the library has found the box
    // the definition's drop targets name, and refused a drop on anything else before it got here.
    onElementDropped: ({ onto }) => {
      if (onto !== undefined) {
        void executeAction(onto.actionId, elementSourceOf(onto.elementId, entryId));
      }
    },
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
        ariaLabel={`Pipeline ${path.join("/")}`}
        className="pipeline-canvas-surface"
        scrollbarsClassName="pipeline-scrollbars"
      />
    </div>
  );
}
