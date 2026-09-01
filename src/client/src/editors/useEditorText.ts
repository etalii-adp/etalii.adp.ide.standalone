import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import type { Delta } from "@client/generated/deltas_pb";

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
}

export const emptyEditorText: EditorTextModel = { text: "", revision: 0, loaded: false };

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

  return { text: decoder.decode(content.payload.value), revision: current.revision + 1, loaded: true };
}

/** The shared stream, folded to text - one hook for every editor module (task 5.1's base). */
export function useEditorText(projectId: Uint8Array, path: readonly string[]) {
  return useDiagramStream(projectId, path, emptyEditorText, applyEditorDelta);
}
