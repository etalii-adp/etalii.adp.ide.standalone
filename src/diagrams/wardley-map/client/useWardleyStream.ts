import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
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
}

/**
 * Opens the map at `path` and folds its delta stream into a `WardleyModel`. The transport,
 * lifecycle, retry and state live in the shared `useDiagramStream`; what is this map's own
 * here is the model, its mapping, and the move call built on the returned client.
 *
 * There is deliberately **no** viewport report here. The backend answers one with the whole
 * map - a Wardley map is a bounded space of tens of elements, so there is nothing to filter -
 * and a report the backend ignores would be a round trip per pan for no result
 * (Requirement 10.5).
 */
export function useWardleyStream(projectId: Uint8Array, path: readonly string[]): WardleyStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client } = useDiagramStream(projectId, path, emptyModel, applyDelta);

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

  return { model, loading, failed, moveElementTo };
}
