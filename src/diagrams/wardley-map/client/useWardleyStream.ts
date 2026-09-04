import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type WardleyModel } from "./wardleyModel";

export interface WardleyStream {
  model: WardleyModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /**
   * True once the backend answered with a permanent error - the map cannot be opened at this
   * path any more. The reconnect loop has stopped and the canvas shows an unavailable state.
   */
  failed: boolean;
  /**
   * Moves an element to a position, in canvas units.
   *
   * On this diagram type a drag is a **document edit**, not a view change: moving a component
   * right asserts that it is more evolved. The backend converts the point back into the
   * document's own axes and dispatches it as a command, so it is written to the `.owm`, reaches
   * every other connection, and is one undo away (Requirement 7.2).
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
  /**
   * Reports what the reader can see, in the map's own 0..1 space.
   *
   * The unit is the module's business and the shared library converts nothing
   * (view-delta-adoption Requirement 3.4): this canvas draws into a fixed box of canvas units,
   * so the conversion back to 0..1 happens at the call site in WardleyCanvas.
   */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the map at `path` and folds its delta stream into a `WardleyModel`. The transport,
 * lifecycle, retry and state live in the shared `useDiagramStream`; what is this map's own
 * here is the model, its mapping, and the move call built on the returned client.
 *
 * The viewport report goes up the paired `UpdateView` leg, and the backend answers on the open
 * stream with the deltas that bring this connection into line. This module used to decline the
 * report because the session answered every viewport with the whole map; the session now
 * filters, so a reader zoomed into one corner stops paying for the rest
 * (view-delta-adoption Requirement 1).
 */
export function useWardleyStream(projectId: Uint8Array, path: readonly string[]): WardleyStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client } = useDiagramStream(projectId, path, emptyModel, applyDelta);
  const reportView = viewReportOf(client, projectId, watchId, path);

  const moveElementTo = async (elementId: string, x: number, y: number): Promise<string> => {
    try {
      const response = await client.moveElement({
        projectId: { value: projectId },
        watchId: { value: watchId },
        path: { segments: [...path] },
        elementId,
        // The position is what makes this an arrangement rather than a re-parenting; the
        // backend routes on its presence.
        position: { x, y },
      });
      return response.error;
    } catch (error) {
      return error instanceof Error ? error.message : "The move could not be sent.";
    }
  };

  return { model, loading, failed, moveElementTo, reportView };
}
