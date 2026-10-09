import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { KnowledgePanel } from "./KnowledgePanel";

/**
 * What this module contributes to the client. The shell discovers this file by the glob that
 * finds every tool module's registrations, so nothing in the shell names the Knowledge designer.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    // The origin a knowledge file's registration names, and nothing else.
    matches: (mimeType) => mimeType === "etalii/knowledge",
    Panel: KnowledgePanel,
  },
];
