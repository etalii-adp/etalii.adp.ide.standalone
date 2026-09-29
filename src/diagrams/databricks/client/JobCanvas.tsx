import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { DatabricksCanvas } from "./DatabricksCanvas";

/**
 * The job diagram: the task DAG with outcome edges, cluster bindings and the full interaction
 * set - drag to reposition through the layout path, anchor drags for dependencies, toolbox
 * drops via placement ids (databricks-diagrams Requirement 4).
 */
export function JobCanvas(props: ToolContentProps) {
  return <DatabricksCanvas {...props} ariaLabel="Databricks job" connectable />;
}
