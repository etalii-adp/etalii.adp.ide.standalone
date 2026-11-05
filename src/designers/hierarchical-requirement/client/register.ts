import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { HierarchicalRequirementPanel } from "./HierarchicalRequirementPanel";

/**
 * What this module contributes to the client. The shell discovers this file by the glob that
 * finds every tool module's registrations, so nothing in the shell names this designer.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    // The origin a requirement document's envelope names, and nothing else.
    matches: (mimeType) => mimeType === "etalii/hierarchical-requirement",
    Panel: HierarchicalRequirementPanel,
  },
];
