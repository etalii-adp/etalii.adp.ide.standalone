import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type MindmapModel } from "./mindmapModel";

/** Re-exported so the module's own files keep one name for it; the shape is the shared one. */
export type { Viewport };

export interface MindmapStream {
  model: MindmapModel;
  /** True until the first delta arrives, so the canvas can show it is loading rather than empty. */
  loading: boolean;
  /**
   * True once the backend answered with a permanent error - the diagram cannot be opened at
   * this path any more (deleted, moved, unroutable). The reconnect loop has stopped; the
   * canvas shows an unavailable state instead (diagram-workspace-tabs Requirement 5.1).
   */
  failed: boolean;
  /** Tells the backend what the canvas can see, so a large map does not stream in full. */
  reportView: (viewport: Viewport) => void;
  /**
   * Moves an element under a new parent - the drop half of a drag. The backend dispatches it
   * as a command (one undo away) and the change comes back as ordinary deltas; the returned
   * string is empty on success, or the backend's own reason for refusing.
   */
  moveElement: (elementId: string, newParentId: string) => Promise<string>;
}

/**
 * Opens the diagram at `path` and folds its delta stream into a `MindmapModel`. The transport,
 * lifecycle, retry and state live in the shared `useDiagramStream`; what is the mindmap's own
 * here is the model, its mapping, and the two unary calls built on the returned client -
 * viewport reports up the paired `UpdateView` leg (mindmap-diagram Requirement 11), and the
 * re-parenting move.
 */
export function useMindmapStream(projectId: Uint8Array, path: readonly string[]): MindmapStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const reportView = viewReportOf(client, projectId, watchId, path);

  const moveElement = async (elementId: string, newParentId: string): Promise<string> => {
    try {
      const response = await client.moveElement({
        projectId: { value: projectId },
        watchId: { value: watchId },
        path: { segments: [...path] },
        elementId,
        newParentId,
        index: -1, // append as the last child - where a drop lands
      });
      return response.error;
    } catch (error) {
      return error instanceof Error ? error.message : "The move could not be sent.";
    }
  };

  return { model, loading, failed, reportView, moveElement };
}
