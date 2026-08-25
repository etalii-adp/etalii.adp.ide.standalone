import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { MindmapCanvas } from "./MindmapCanvas";
import "./mindmap.css";

/**
 * What this module contributes to the client. The shell discovers this file by scanning
 * every `src/diagrams/<type>/client/register.ts`, so nothing in the shell names Freeplane mindmaps.
 */
export const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === "freeplane/mindmap",
    Canvas: MindmapCanvas,
  },
];
