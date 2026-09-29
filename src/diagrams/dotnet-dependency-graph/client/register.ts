import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { DotNetDependencyGraphCanvas } from "./DotNetDependencyGraphCanvas";
import "@client/canvas/canvas.css";
import "./dotnet-dependency-graph.css";

/**
 * What this module contributes to the client. The shell discovers this file by scanning every
 * `src/diagrams/<type>/client/register.ts`, so nothing in the shell names this type - adding a
 * diagram type means adding a module, not editing the shell.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "dotnet/dependency-graph",
    Panel: DotNetDependencyGraphCanvas,
  },
];
