import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import type { DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { DatabricksCanvas } from "./DatabricksCanvas";
import { compiledDefinition } from "./databricksBindings";
import type { DatabricksNode } from "./databricksModel";
import disText from "../definition/databricks-job.dis?raw";

/**
 * What a job diagram is, compiled from the bundled definition (`definition/databricks-job.dis`): the task
 * DAG with outcome edges and its job clusters, and the full interaction set - drag to reposition through
 * the layout path, anchor drags for dependencies, toolbox drops via placement ids (databricks-diagrams
 * Requirement 4).
 */
export const JOB_DEFINITION: DiagramDefinition = compiledDefinition(disText);

/** The definition's type of a box the job reading sends: a task, a missing task's stub included, or a job cluster. */
export function jobTypeOf(node: DatabricksNode): string {
  return node.id.startsWith("task:") ? "task" : "jobcluster";
}

/** The job diagram (databricks-diagrams Requirement 4). */
export function JobCanvas(props: ToolContentProps) {
  return <DatabricksCanvas {...props} ariaLabel="Databricks job" definition={JOB_DEFINITION} typeOf={jobTypeOf} />;
}
