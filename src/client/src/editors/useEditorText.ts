import { useCallback, useMemo } from "react";
import { createClient } from "@connectrpc/connect";
import { useAuth } from "@client/auth/AuthContext";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { EditorService } from "@client/generated/editors_pb";
import type { Delta } from "@client/generated/deltas_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";

/**
 * What an editor tab's stream carries: the file's current text on the wire, and nothing else.
 * The backend's session adapter baselines one synthetic "content" element and replays an
 * external change as Remove+Add on the same id, so folding the stream is: take the text of
 * whatever "content" element arrives.
 */
export interface EditorTextModel {
  text: string;
  /** Counts remote arrivals, so a component can tell "the disk changed again" from a re-render. */
  revision: number;
  /** False until the baseline arrived - an empty file and a not-yet-loaded one differ. */
  loaded: boolean;
  /**
   * The content element's own type - `editor/<id>` for whichever module the backend resolved.
   * What an "Open as text" tab reads to mount the right module's canvas (R5.2): the stream
   * names the editor, so the client never keeps a file-type table of its own.
   */
  contentMime: string;
}

export const emptyEditorText: EditorTextModel = { text: "", revision: 0, loaded: false, contentMime: "" };

const decoder = new TextDecoder();

export function applyEditorDelta(current: EditorTextModel, delta: Delta): EditorTextModel {
  if (delta.action.case !== "add") {
    // The Remove half of a replacement pair carries nothing to show; the Add that follows does.
    return current;
  }

  const content = delta.action.value.elements.find((element) => element.id?.value === "content");
  if (!content?.payload) {
    return current;
  }

  return {
    text: decoder.decode(content.payload.value),
    revision: current.revision + 1,
    loaded: true,
    contentMime: content.type,
  };
}

/** What {@link useEditorText} hands a panel: the folded stream, plus the save pipeline. */
export interface EditorTextResult {
  model: EditorTextModel;
  loading: boolean;
  failed: boolean;
  /**
   * Saves the full text through `EditorService.SaveText`, whose backend half dispatches a
   * command on the project's history - one undo away, like every other change (R6.2).
   * Resolves to "" on success, or the sentence to show beside the dirty indicator.
   */
  save: (content: string) => Promise<string>;
}

/** The shared stream, folded to text - one hook for every editor module (task 5.1's base). */
export function useEditorText(projectId: Uint8Array, path: readonly string[], editorId = ""): EditorTextResult {
  const { model, loading, failed } = useDiagramStream(projectId, path, emptyEditorText, applyEditorDelta, editorId);
  const { watchId } = useContextConnection();
  // The save is the editor family's own call, on its own service; the text itself rides the
  // shared stream above like every other tool's content.
  const { transport } = useAuth();
  const client = useMemo(() => createClient(EditorService, transport), [transport]);
  const pathKey = path.join("/");

  const save = useCallback(
    async (content: string): Promise<string> => {
      try {
        const response = await client.saveText({
          projectId: { value: projectId },
          watchId: { value: watchId },
          path: { segments: pathKey.length > 0 ? pathKey.split("/") : [] },
          content,
        });
        return response.error;
      } catch (error) {
        return error instanceof Error ? error.message : "The save did not reach the backend.";
      }
    },
    [client, projectId, watchId, pathKey],
  );

  return { model, loading, failed, save };
}
