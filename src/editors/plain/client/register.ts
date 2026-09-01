import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { PlainEditorPanel } from "./PlainEditorPanel";

/**
 * What this module contributes to the client. The shell discovers this file by the same glob
 * that finds every diagram module's registrations, so nothing in the shell names plain text.
 */
export const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === "editor/plain",
    Canvas: PlainEditorPanel,
  },
];
