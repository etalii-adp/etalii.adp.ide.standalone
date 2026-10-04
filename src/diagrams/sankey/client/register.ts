import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { SankeyCanvas } from "./SankeyCanvas";
import { SANKEY_MIME } from "./sankeyIds";
import "@client/canvas/canvas.css";
import "./sankey.css";

/**
 * What this module contributes to the client: one canvas for `etalii/sankey`.
 *
 * The shell finds this by scanning `src/diagrams/*​/client/register.ts` - it holds no list of
 * diagram types, so adding this one meant adding a module, not editing the shell.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === SANKEY_MIME,
    Panel: SankeyCanvas,
  },
];
