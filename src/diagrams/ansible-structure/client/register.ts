import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { AnsibleCanvas } from "./AnsibleCanvas";
import "./ansible-structure.css";

/**
 * What this module contributes to the client. The shell discovers this file by scanning every
 * `src/diagrams/<type>/client/register.ts`, so nothing in the shell names Ansible - adding a
 * diagram type means adding a module, not editing the shell.
 */
export const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === "ansible/structure",
    Canvas: AnsibleCanvas,
  },
];
