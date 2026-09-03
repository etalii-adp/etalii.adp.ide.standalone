import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { RdfCanvas } from "./RdfCanvas";
import { OwlCanvas } from "./OwlCanvas";
import { SkosCanvas } from "./SkosCanvas";
import "@client/canvas/canvas.css";
import "./rdf.css";
import "./owl.css";
import "./skos.css";

/**
 * What this module contributes to the client: the data-graph canvas for `w3c/rdf`, the ontology
 * canvas for `w3c/owl` and the concept-scheme canvas for `w3c/skos` - one module, one engine, a
 * canvas per reading. The remaining sibling reading registers its own beside these as its spec
 * lands.
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
  {
    matches: (mimeType) => mimeType === "w3c/skos",
    Canvas: SkosCanvas,
  },
];
