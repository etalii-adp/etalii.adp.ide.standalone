import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { CausalLoopCanvas } from "./CausalLoopCanvas";
import "@client/canvas/canvas.css";
import "./causal-loop.css";

/**
 * What this module contributes to the client. The shell discovers this file by scanning every
 * `src/diagrams/<type>/client/register.ts`, so nothing in the shell names causal-loop - adding a
 * diagram type means adding a module, not editing the shell.
 */
export const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === "systems/causal-loop-diagram",
    Canvas: CausalLoopCanvas,
  },
];
