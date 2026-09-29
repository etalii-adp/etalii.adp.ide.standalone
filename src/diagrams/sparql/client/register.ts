import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { SparqlCanvas } from "./SparqlCanvas";
import "@client/canvas/canvas.css";
import "./sparql.css";

/**
 * What this module contributes to the client: the query canvas for `w3c/sparql`.
 *
 * The shell finds this by scanning `src/diagrams/*​/client/register.ts` - it holds no list of
 * diagram types, exactly as the backend holds none and discovers `Diagram.Definitions` by
 * scanning its own assemblies.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "w3c/sparql",
    Panel: SparqlCanvas,
  },
];
