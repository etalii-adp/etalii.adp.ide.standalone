import { useCallback, useEffect, useRef, useState } from "react";
import type { Extension } from "@codemirror/state";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { markTabDirty } from "@client/shell/panels/dirtyTabs";
import { BaseTextEditor, scrollToLine } from "./BaseTextEditor";
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
   * Overrides the save pipeline. Unset - the ordinary case - the panel saves through the
   * stream's own `SaveText` call, which lands the write on the project's history (R6.2).
   */
  onSave?: (content: string) => Promise<string>;
  /** Extra chrome rendered beside the editor - markdown's preview pane. */
  aside?: (text: string) => React.ReactNode;
  /** Chrome above the editor - markdown's heading outline. */
  header?: (text: string, goToLine: (line: number) => void) => React.ReactNode;
}

export function TextEditorPanel({ projectId, path, editorId, initialLine, extensions, onSave, aside, header }: TextEditorPanelProps) {
  const { model, loading, failed, save: streamSave } = useEditorText(projectId, path, editorId);
  const [localText, setLocalText] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [saveError, setSaveError] = useState("");
  const [lineNotice, setLineNotice] = useState("");
  const baselineRevision = useRef(0);
  const appliedInitialLine = useRef<number | undefined>(undefined);
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

  // A problem's go-to-line, honoured once per requested line and only against loaded text.
  // A line the file no longer has opens the file and says so, rather than guessing a nearby
  // one (R8.3) - the file may have changed since the problem was found.
  useEffect(() => {
    if (initialLine === undefined || !model.loaded || appliedInitialLine.current === initialLine) {
      return;
    }
    appliedInitialLine.current = initialLine;

    const lineCount = model.text.split("\n").length;
    if (initialLine > lineCount) {
      setLineNotice(`Line ${initialLine} is not in this file any more - it has ${lineCount} lines now.`);
    } else {
      setLineNotice("");
      if (hostRef.current) {
        scrollToLine(hostRef.current, initialLine);
      }
    }
  }, [initialLine, model.loaded, model.text]);

  const save = useCallback(async () => {
    const performSave = onSave ?? streamSave;
    if (localText === null) {
      return;
    }

    const error = await performSave(localText);
    setSaveError(error);
    if (error === "") {
      setLocalText(null);
    }
  }, [onSave, streamSave, localText]);

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
      {lineNotice !== "" && (
        <div className="text-editor-line-notice" role="status">
          {lineNotice}
        </div>
      )}
      {header?.(shown, (line) => hostRef.current && scrollToLine(hostRef.current, line))}
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
