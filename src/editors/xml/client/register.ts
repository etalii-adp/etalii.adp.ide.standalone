import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { XmlEditorPanel } from "./XmlEditorPanel";

/**
 * What this module contributes to the client. The shell discovers this file by the same glob
 * that finds every diagram module's registrations, so nothing in the shell names XML.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "editor/xml",
    Panel: XmlEditorPanel,
  },
];
