import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { FdgCanvas } from "./FdgCanvas";
import { FDG_MIME } from "./fdgIds";
import "@client/canvas/canvas.css";
import "./fdg.css";

/**
 * What this module contributes to the client: one canvas for `etalii/functional-decomposition-graph`.
 *
 * The shell finds this by scanning `src/diagrams/*​/client/register.ts` - it holds no list of
 * diagram types, so adding this one meant adding a module, not editing the shell.
 */
export const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === FDG_MIME,
    Canvas: FdgCanvas,
  },
];
