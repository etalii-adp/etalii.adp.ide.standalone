import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import type { DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { DatabricksCanvas } from "./DatabricksCanvas";
import { compiledDefinition } from "./databricksBindings";
import type { DatabricksNode } from "./databricksModel";
import disText from "../definition/databricks-bundle.dis?raw";

/**
 * What a bundle diagram is, compiled from the bundled definition (`definition/databricks-bundle.dis`):
 * the bundle, its resources (unmodelled kinds drawn as the definition's `Unknown`) and the target frames
 * beneath them, with their override edges (databricks-diagrams Requirement 3). No end of an override is
 * connectable - overrides are written in the file, not drawn.
 */
export const BUNDLE_DEFINITION: DiagramDefinition = compiledDefinition(disText);

/** The definition's type of a box the bundle reading sends: the bundle, a resource, or a kind the reader does not model. */
export function bundleTypeOf(node: DatabricksNode): string {
  return node.id === "bundle" ? "bundle" : node.id.startsWith("unknown:") ? "unknown" : "resource";
}

/** The bundle diagram (databricks-diagrams Requirement 3). */
export function BundleCanvas(props: ToolContentProps) {
  return <DatabricksCanvas {...props} ariaLabel="Databricks bundle" definition={BUNDLE_DEFINITION} typeOf={bundleTypeOf} />;
}
