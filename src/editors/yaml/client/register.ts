import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { YamlEditorPanel } from "./YamlEditorPanel";

/**
 * What this module contributes to the client. The shell discovers this file by the same glob
 * that finds every diagram module's registrations, so nothing in the shell names YAML.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "editor/yaml",
    Panel: YamlEditorPanel,
  },
];
