import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { applyDelta, emptyModel, type AnsibleModel } from "./ansibleModel";

/** A viewport the client reports; the backend answers with what falls inside it. */
export interface Viewport {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface AnsibleStream {
  model: AnsibleModel;
  /** True until the first delta arrives, so the canvas can show it is loading rather than empty. */
  loading: boolean;
  /**
   * True once the backend answered with a permanent error - the diagram cannot be opened at
   * this path any more (deleted, moved, unroutable). The reconnect loop has stopped.
   */
  failed: boolean;
  /** Tells the backend what the canvas can see, so a large project does not stream in full. */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the diagram at `path` and folds its delta stream into an `AnsibleModel`. The
 * transport, lifecycle, retry and state live in the shared `useDiagramStream`; what is this
 * module's own here is the model, its mapping, and the viewport report built on the returned
 * client.
 *
 * Note what this hook does NOT expose, compared with `useMindmapStream`: there is no move
 * call. The backend refuses one with a sentence, so offering it here would put a gesture in
 * the client's reach whose only possible outcome is a refusal. A read-only type's client
 * should not be able to ask.
 */
export function useAnsibleStream(projectId: Uint8Array, path: readonly string[]): AnsibleStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const reportView = (viewport: Viewport) => {
    void client
      .updateView({
        projectId: { value: projectId },
        watchId: { value: watchId },
        path: { segments: [...path] },
        view: {
          center: { x: (viewport.minX + viewport.maxX) / 2, y: (viewport.minY + viewport.maxY) / 2 },
          boundingBox: { min: { x: viewport.minX, y: viewport.minY }, max: { x: viewport.maxX, y: viewport.maxY } },
        },
      })
      .catch(() => {
        // A view report is advisory; if it fails the backend keeps the last one it had.
      });
  };

  return { model, loading, failed, reportView };
}
