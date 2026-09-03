import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { DatabricksCanvas } from "./DatabricksCanvas";

/**
 * The job diagram: the task DAG with outcome edges, cluster bindings and the full interaction
 * set - drag to reposition through the layout path, anchor drags for dependencies, toolbox
 * drops via placement ids (databricks-diagrams Requirement 4).
 */
export function JobCanvas(props: DiagramCanvasProps) {
  return <DatabricksCanvas {...props} ariaLabel="Databricks job" connectable />;
}
