import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { HelmCanvas } from "./HelmCanvas";
import "@client/canvas/canvas.css";
import "./helm-charts.css";

/**
 * What this module contributes to the client. The shell discovers this file by scanning every
 * `src/diagrams/<type>/client/register.ts`, so nothing in the shell names helm - adding a
 * diagram type means adding a module, not editing the shell.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "helm/chart",
    Panel: HelmCanvas,
  },
];
