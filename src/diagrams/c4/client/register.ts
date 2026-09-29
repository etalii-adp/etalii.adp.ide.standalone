import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { C4Canvas } from "./C4Canvas";
import "@client/canvas/canvas.css";
import "./c4.css";

/**
 * What this module contributes to the client: one canvas for six of C4's seven notations, and
 * an explanation for the seventh.
 *
 * The backend declares all seven in one `Diagram.Definitions` array for the same reason the
 * canvas is shared - they are views of one model, not seven notations.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    // The six view types share one canvas. c4/code is excluded deliberately and handled
    // below, so ordering between these two entries does not matter.
    matches: (mimeType) => mimeType.startsWith("c4/") && mimeType !== "c4/code",
    Panel: C4Canvas,
  },
  {
    // C4 specifies UML class or entity-relationship notation for the code level and advises
    // generating it from an IDE rather than drawing it, so this waits on a class diagram type
    // rather than growing a fourth notation here (c4-diagrams Requirement 11). The explanation
    // lives with the module that understands the notation, not in a fallback in the shell.
    matches: (mimeType) => mimeType === "c4/code",
    unsupported: {
      description:
        "C4 code diagrams use UML class or entity-relationship notation, which ADP does not implement yet - and C4 itself recommends generating this level from an IDE rather than drawing it. Use a Component diagram to describe what is inside a container.",
      futureSpec: "c4-diagrams",
    },
  },
];
