import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { PipelineCanvas } from "./PipelineCanvas";
import "./azure-pipeline.css";

/**
 * What this module contributes to the client: one canvas for the one pipeline type.
 *
 * The shell discovers this by globbing every diagram module's `client/register.ts`, so nothing in
 * the shell names Azure Pipelines - exactly as nothing in the backend names it either. Adding a
 * diagram type is adding a module, not editing the shell (Requirement 14.3).
 */
export const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === "azure-devops/pipeline",
    Canvas: PipelineCanvas,
  },
];
