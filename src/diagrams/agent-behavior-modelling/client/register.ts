import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { AbmCanvas } from "./AbmCanvas";
import { ABM_MIME } from "./abmIds";
import "@client/canvas/canvas.css";
import "./abm.css";

/**
 * What this module contributes to the client: one canvas for `etalii/agent-behavior-modelling`,
 * found by the shell's scan of every module's `client/register.ts`.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === ABM_MIME,
    Panel: AbmCanvas,
  },
];
