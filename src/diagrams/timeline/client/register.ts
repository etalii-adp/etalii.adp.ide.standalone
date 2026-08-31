import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { TimelineCanvas } from "./TimelineCanvas";
import "./timeline.css";

/**
 * What this module contributes to the client: one canvas for `generic/timeline`.
 *
 * The shell finds this by scanning `src/diagrams/*​/client/register.ts` - it holds no list of
 * diagram types, exactly as the backend holds none and discovers `Diagram.Definitions` by
 * scanning its own assemblies. Adding this type meant adding a module, not editing the shell.
 */
export const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === "generic/timeline",
    Canvas: TimelineCanvas,
  },
];
