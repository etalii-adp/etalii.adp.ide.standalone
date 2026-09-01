import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { TextEditorPanel } from "@client/editors/TextEditorPanel";

/**
 * The plain editor: the shared text panel and nothing else - which is the module's whole
 * point. Saving included: the shared panel's default pipeline is the stream's own SaveText
 * call, which lands the write on the project's history (R6.2).
 */
export function PlainEditorPanel(props: DiagramCanvasProps) {
  return <TextEditorPanel {...props} />;
}
