import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { MindmapCanvas } from "./MindmapCanvas";
import "@client/canvas/canvas.css";
import "./mindmap.css";

/**
 * What this module contributes to the client. The shell discovers this file by scanning
 * every `src/diagrams/<type>/client/register.ts`, so nothing in the shell names Freeplane mindmaps.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "freeplane/mindmap",
    Panel: MindmapCanvas,
  },
];
