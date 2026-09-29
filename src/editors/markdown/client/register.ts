import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { MarkdownEditorPanel } from "./MarkdownEditorPanel";

/**
 * What this module contributes to the client. The shell discovers this file by the same glob
 * that finds every diagram module's registrations, so nothing in the shell names markdown.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "editor/markdown",
    Panel: MarkdownEditorPanel,
  },
];
