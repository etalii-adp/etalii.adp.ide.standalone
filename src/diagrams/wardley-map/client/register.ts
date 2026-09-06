import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { WardleyCanvas } from "./WardleyCanvas";
import "@client/canvas/canvas.css";
import "./wardley.css";

/**
 * What this module contributes to the client: one canvas for `wardley/map`.
 *
 * The shell finds this by scanning `src/diagrams/*​/client/register.ts` - it holds no list of
 * diagram types, exactly as the backend holds none and discovers `Diagram.Definitions` by
 * scanning its own assemblies. Adding this type meant adding a module, not editing the shell
 * (Requirement 8.7).
 */
export const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === "wardley/map",
    Canvas: WardleyCanvas,
  },
];
