import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { GhgCanvas } from "./GhgCanvas";
import { GHG_MIME } from "./ghgIds";
import "@client/canvas/canvas.css";
import "./ghg.css";

/**
 * What this module contributes to the client: one canvas for `gartner/hypecycle-graph`.
 *
 * The shell finds this by scanning `src/diagrams/*​/client/register.ts` - it holds no list of
 * diagram types, so adding this one meant adding a module, not editing the shell.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === GHG_MIME,
    Panel: GhgCanvas,
  },
];
