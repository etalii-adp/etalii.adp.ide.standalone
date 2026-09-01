import { useCallback, useEffect, useRef, useState } from "react";
import type { Extension } from "@codemirror/state";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { markTabDirty } from "@client/shell/panels/dirtyTabs";
import { BaseTextEditor } from "./BaseTextEditor";
import { useEditorText } from "./useEditorText";
import "./editors.css";

/**
 * The whole text-editing tab, shared by every editor module (modular-text-editors task 5.2):
 * the stream in, the base editor, the unsaved-changes indicator, and the external-change
 * conflict presentation - a banner offering both sides, never a silent discard (R6.4, per
 * tech.md's Frontend-backend synchronization rule). A module wraps this with its own
 * extensions and any extra chrome - markdown's preview and outline.
 */
export interface TextEditorPanelProps extends DiagramCanvasProps {
  /** Module-supplied CodeMirror extensions. */
  extensions?: Extension[];
  /**
   * Saves the text. Wired to the save pipeline where the deployment has one; until group 6
   * lands the editor.save action, a module may pass undefined and the indicator simply stays
   * dirty - deferral over pretence.
   */
  onSave?: (content: string) => Promise<string>;
  /** Extra chrome rendered beside the editor - markdown's preview pane. */
  aside?: (text: string) => React.ReactNode;
  /** Chrome above the editor - markdown's heading outline. */
  header?: (text: string, goToLine: (line: number) => void) => React.ReactNode;
}

export function TextEditorPanel({ projectId, path, extensions, onSave, aside, header }: TextEditorPanelProps) {
  const { model, loading, failed } = useEditorText(projectId, path);
  const [localText, setLocalText] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [saveError, setSaveError] = useState("");
  const baselineRevision = useRef(0);
  const hostRef = useRef<HTMLDivElement | null>(null);

  const dirty = localText !== null && localText !== model.text;
  const tabKey = path.join("/");

  useEffect(() => {
    markTabDirty(tabKey, dirty);
    return () => markTabDirty(tabKey, false);
  }, [tabKey, dirty]);

  useEffect(() => {
    if (!model.loaded || model.revision === baselineRevision.current) {
      return;
    }

    // The disk changed while this tab was open. With no local edits the new text simply takes
    // over; with edits in flight, both sides are real and the user decides (R6.4).
    if (localText !== null && localText !== model.text) {
      setConflict(true);
    } else {
      setLocalText(null);
    }

    baselineRevision.current = model.revision;
  }, [model.revision, model.loaded, model.text, localText]);

  const save = useCallback(async () => {
    if (onSave === undefined || localText === null) {
      return;
    }

    const error = await onSave(localText);
    setSaveError(error);
    if (error === "") {
      setLocalText(null);
    }
  }, [onSave, localText]);

  if (failed) {
    return <p className="text-editor-unavailable">This file cannot be opened as text any more.</p>;
  }

  if (loading && !model.loaded) {
    return <p className="text-editor-loading">Loading…</p>;
  }

  const shown = localText ?? model.text;

  return (
    <div className="text-editor-panel" ref={hostRef}>
      {conflict && (
        <div className="text-editor-conflict" role="alert">
          <span>This file changed on disk while you were editing.</span>
          <button
            type="button"
            onClick={() => {
              // Their side: the disk wins, the local edit is deliberately let go.
              setLocalText(null);
              setConflict(false);
            }}
          >
            Load the disk version
          </button>
          <button type="button" onClick={() => setConflict(false)}>
            Keep my changes
          </button>
        </div>
      )}
      {saveError !== "" && (
        <div className="text-editor-save-error" role="alert">
          {saveError}
        </div>
      )}
      {header?.(shown, (line) => hostRef.current && scrollHostToLine(hostRef.current, line))}
      <div className="text-editor-body">
        <div className="text-editor-surface">
          <BaseTextEditor
            content={shown}
            onChange={setLocalText}
            extensions={extensions}
            onSave={() => void save()}
          />
        </div>
        {aside && <div className="text-editor-aside">{aside(shown)}</div>}
      </div>
      <div className="text-editor-status">
        <span data-testid="dirty-indicator" className={dirty ? "text-editor-dirty" : "text-editor-clean"}>
          {dirty ? "● Unsaved changes" : "Saved"}
        </span>
      </div>
    </div>
  );
}

function scrollHostToLine(host: HTMLElement, line: number) {
  // Late import shape avoided deliberately: BaseTextEditor exports the mechanism.
  void import("./BaseTextEditor").then(({ scrollToLine }) => scrollToLine(host, line));
}
