import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { DatabricksCanvas } from "./DatabricksCanvas";

/**
 * The bundle diagram: the bundle, its resources (unmodelled kinds drawn generically) and the
 * target frames with their override edges (databricks-diagrams Requirement 3). Edges here are
 * the file's structure, so there is no dependency gesture - overrides are written in the file,
 * not drawn.
 */
export function BundleCanvas(props: DiagramCanvasProps) {
  return <DatabricksCanvas {...props} ariaLabel="Databricks bundle" connectable={false} />;
}
