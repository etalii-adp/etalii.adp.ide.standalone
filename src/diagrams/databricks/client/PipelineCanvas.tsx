import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { DatabricksCanvas } from "./DatabricksCanvas";

/**
 * The pipeline diagram: source libraries flowing into the pipeline and on to its catalog
 * target, satellites beneath (databricks-diagrams Requirement 5). The flow edges are the
 * file's structure, so there is no dependency gesture.
 */
export function PipelineCanvas(props: ToolContentProps) {
  return <DatabricksCanvas {...props} ariaLabel="Databricks pipeline" connectable={false} />;
}
