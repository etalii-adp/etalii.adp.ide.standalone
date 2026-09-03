import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { RdfCanvas } from "./RdfCanvas";
import "@client/canvas/canvas.css";
import "./rdf.css";

/**
 * What this module contributes to the client: the data-graph canvas for `w3c/rdf`. The sibling
 * readings joining the family register their own canvases beside this one as their specs land.
 *
 * The shell finds this by scanning `src/diagrams/*​/client/register.ts` - it holds no list of
 * diagram types, exactly as the backend holds none and discovers `Diagram.Definitions` by
 * scanning its own assemblies.
 */
export const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === "w3c/rdf",
    Canvas: RdfCanvas,
  },
];
