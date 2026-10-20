import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import type { DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { DatabricksCanvas } from "./DatabricksCanvas";
import { compiledDefinition } from "./databricksBindings";
import type { DatabricksNode } from "./databricksModel";
import disText from "../definition/databricks-pipeline.dis?raw";

/**
 * What a pipeline diagram is, compiled from the bundled definition (`definition/databricks-pipeline.dis`):
 * source libraries flowing into the pipeline and on to its catalog destination, compute and notifications
 * beneath (databricks-diagrams Requirement 5). The flow edges are the file's structure, so no end of one
 * is connectable.
 */
export const PIPELINE_DEFINITION: DiagramDefinition = compiledDefinition(disText);

/** The definition's type of each role the pipeline reading sends a box in. */
const TYPE_OF_ROLE: Readonly<Record<string, string>> = {
  source: "library",
  pipeline: "pipeline",
  target: "destination",
  compute: "compute",
  notifications: "notifications",
};

export function pipelineTypeOf(node: DatabricksNode): string {
  return TYPE_OF_ROLE[node.kind] ?? node.kind;
}

/** The pipeline diagram (databricks-diagrams Requirement 5). */
export function PipelineCanvas(props: ToolContentProps) {
  return <DatabricksCanvas {...props} ariaLabel="Databricks pipeline" definition={PIPELINE_DEFINITION} typeOf={pipelineTypeOf} />;
}
