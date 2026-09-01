import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { canvasFor } from "@client/shell/panels/diagramCanvases";
import { TextEditorPanel } from "./TextEditorPanel";
import { useEditorText } from "./useEditorText";

/**
 * The canvas behind an "Open as text" tab whose editor is not yet known client-side (mime
 * `editor/*`): the stream itself names the module - the content element's type is
 * `editor/<id>` - so this panel reads the first delta, then mounts that module's own canvas,
 * markdown chrome and all (R5.2's "the editor its extension resolves to", never a raw
 * lowest-common-denominator view). The client keeps no file-type table of its own; the
 * backend's resolver stays the one authority.
 */
export function ResolvedTextEditorPanel(props: DiagramCanvasProps) {
  const { model, failed } = useEditorText(props.projectId, props.path, props.editorId ?? "*");

  if (!failed && model.loaded && model.contentMime !== "") {
    const Canvas = canvasFor(model.contentMime)?.Canvas;
    if (Canvas !== undefined && Canvas !== ResolvedTextEditorPanel) {
      // The module's canvas opens its own stream with the same forced resolution; this
      // probe's stream closes on unmount. Two short-lived streams beat teaching every module
      // panel to adopt a half-consumed one.
      return <Canvas {...props} editorId={props.editorId ?? "*"} />;
    }
  }

  // Loading, refused, or a module whose client half is missing: the shared panel shows the
  // stream's own state honestly (a refusal arrives as a failed stream, not a blank page).
  return <TextEditorPanel {...props} editorId={props.editorId ?? "*"} />;
}
