import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { SupplyChainCanvas } from "./SupplyChainCanvas";
import { SUPPLY_CHAIN_MIME } from "./supplyChainIds";
import "@client/canvas/canvas.css";
import "./supply-chain.css";

/**
 * What this module contributes to the client: one canvas for `etalii/supply-chain`.
 *
 * The shell finds this by scanning `src/diagrams/*​/client/register.ts` - it holds no list of
 * diagram types, so adding this one meant adding a module, not editing the shell.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === SUPPLY_CHAIN_MIME,
    Panel: SupplyChainCanvas,
  },
];
