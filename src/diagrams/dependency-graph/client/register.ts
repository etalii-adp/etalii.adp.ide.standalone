import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { DependencyGraphCanvas } from "./DependencyGraphCanvas";
import "@client/canvas/canvas.css";
import "./dependency-graph.css";

/**
 * What this module contributes to the client: one canvas for `generic/dependencies`.
 *
 * The shell finds this by scanning `src/diagrams/*​/client/register.ts` - it holds no list of
 * diagram types, exactly as the backend holds none and discovers `Diagram.Definitions` by
 * scanning its own assemblies. Adding this type meant adding a module, not editing the shell.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "generic/dependencies",
    Panel: DependencyGraphCanvas,
  },
];
