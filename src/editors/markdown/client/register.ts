import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { MarkdownEditorPanel } from "./MarkdownEditorPanel";

/**
 * What this module contributes to the client. The shell discovers this file by the same glob
 * that finds every diagram module's registrations, so nothing in the shell names markdown.
 */
export const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === "editor/markdown",
    Canvas: MarkdownEditorPanel,
  },
];
