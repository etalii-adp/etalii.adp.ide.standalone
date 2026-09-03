import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { RdfCanvas } from "./RdfCanvas";
import { SkosCanvas } from "./SkosCanvas";
import "@client/canvas/canvas.css";
import "./rdf.css";
import "./skos.css";

/**
 * What this module contributes to the client: the data-graph canvas for `w3c/rdf` and the
 * concept-scheme canvas for `w3c/skos`. The remaining sibling readings register their own
 * canvases beside these as their specs land.
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
  {
    matches: (mimeType) => mimeType === "w3c/skos",
    Canvas: SkosCanvas,
  },
];
