import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { BundleCanvas } from "./BundleCanvas";
import { JobCanvas } from "./JobCanvas";
import { PipelineCanvas } from "./PipelineCanvas";
import "@client/canvas/canvas.css";
import "./databricks.css";

/**
 * What this module contributes to the client: three canvases for the family's three MIME types.
 *
 * The shell finds this by scanning `src/diagrams/*​/client/register.ts` - it holds no list of
 * diagram types, exactly as the backend holds none and discovers `Diagram.Definitions` by
 * scanning its own assemblies. Adding this family meant adding a module, not editing the shell.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "databricks/bundle",
    Panel: BundleCanvas,
  },
  {
    matches: (mimeType) => mimeType === "databricks/job",
    Panel: JobCanvas,
  },
  {
    matches: (mimeType) => mimeType === "databricks/pipeline",
    Panel: PipelineCanvas,
  },
];
