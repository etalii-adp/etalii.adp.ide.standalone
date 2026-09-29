import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { RdfCanvas } from "./RdfCanvas";
import { OwlCanvas } from "./OwlCanvas";
import { SkosCanvas } from "./SkosCanvas";
import { ShaclCanvas } from "./ShaclCanvas";
import "@client/canvas/canvas.css";
import "./rdf.css";
import "./owl.css";
import "./skos.css";
import "./shacl.css";

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
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "w3c/rdf",
    Panel: RdfCanvas,
  },
  {
    matches: (mimeType) => mimeType === "w3c/owl",
    Panel: OwlCanvas,
  },
  {
    matches: (mimeType) => mimeType === "w3c/skos",
    Panel: SkosCanvas,
  },
  {
    matches: (mimeType) => mimeType === "w3c/shacl",
    Panel: ShaclCanvas,
  },
];
