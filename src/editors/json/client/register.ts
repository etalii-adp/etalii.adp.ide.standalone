import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { JsonEditorPanel } from "./JsonEditorPanel";

/**
 * What this module contributes to the client. The shell discovers this file by the same glob
 * that finds every diagram module's registrations, so nothing in the shell names JSON.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "editor/json",
    Panel: JsonEditorPanel,
  },
];
