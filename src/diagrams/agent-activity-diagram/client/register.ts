import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { AadCanvas } from "./AadCanvas";
import { AAD_MIME } from "./aadIds";
import "@client/canvas/canvas.css";
import "./aad.css";

/**
 * What this module contributes to the client: one canvas for `etalii/agent-activity-diagram`.
 *
 * The shell finds this by scanning `src/diagrams/*​/client/register.ts` - it holds no list of
 * diagram types, so adding this one meant adding a module, not editing the shell.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === AAD_MIME,
    Panel: AadCanvas,
  },
];
