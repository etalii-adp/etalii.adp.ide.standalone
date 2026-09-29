import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { AnsibleCanvas } from "./AnsibleCanvas";
import "@client/canvas/canvas.css";
import "./ansible-structure.css";

/**
 * What this module contributes to the client. The shell discovers this file by scanning every
 * `src/diagrams/<type>/client/register.ts`, so nothing in the shell names Ansible - adding a
 * diagram type means adding a module, not editing the shell.
 */
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "ansible/structure",
    Panel: AnsibleCanvas,
  },
];
