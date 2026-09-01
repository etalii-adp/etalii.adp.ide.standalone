import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { TextEditorPanel } from "@client/editors/TextEditorPanel";

/**
 * The plain editor: the shared text panel and nothing else - which is the module's whole
 * point. Save wiring arrives with the editor.save context action (spec group 6); until then
 * the panel shows honest dirty state rather than pretending a save happened.
 */
export function PlainEditorPanel(props: DiagramCanvasProps) {
  return <TextEditorPanel {...props} />;
}
