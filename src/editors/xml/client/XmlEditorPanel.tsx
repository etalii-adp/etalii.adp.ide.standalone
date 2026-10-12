import { xml } from "@codemirror/lang-xml";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { TextEditorPanel } from "@client/editors/TextEditorPanel";

const extensions = [xml()];

/**
 * The XML editor: the shared text panel with CodeMirror's XML grammar. Colour comes from the
 * shared scheme the base editor installs, so this module declares none.
 */
export function XmlEditorPanel(props: ToolContentProps) {
  return <TextEditorPanel {...props} extensions={extensions} />;
}
