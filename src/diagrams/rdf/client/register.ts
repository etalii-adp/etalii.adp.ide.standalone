import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { RdfCanvas } from "./RdfCanvas";
import { OwlCanvas } from "./OwlCanvas";
import "@client/canvas/canvas.css";
import "./rdf.css";
import "./owl.css";

/**
 * What this module contributes to the client: the data-graph canvas for `w3c/rdf` and the
 * ontology canvas for `w3c/owl` - one module, one engine, a canvas per reading. The remaining
 * sibling readings register their own beside these as their specs land.
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
    matches: (mimeType) => mimeType === "w3c/owl",
    Canvas: OwlCanvas,
  },
];
