import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { DotNetDependencyGraphCanvas } from "./DotNetDependencyGraphCanvas";
import "@client/canvas/canvas.css";
import "./dotnet-dependency-graph.css";

/**
 * What this module contributes to the client. The shell discovers this file by scanning every
 * `src/diagrams/<type>/client/register.ts`, so nothing in the shell names this type - adding a
 * diagram type means adding a module, not editing the shell.
 */
export const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === "dotnet/dependency-graph",
    Canvas: DotNetDependencyGraphCanvas,
  },
];
