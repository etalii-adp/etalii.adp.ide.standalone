import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { applyDelta, emptyModel, type PipelineModel } from "./pipelineModel";

/** A viewport the client reports; the backend answers with what falls inside it. */
export interface Viewport {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface PipelineStream {
  model: PipelineModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /**
   * True once the backend answered with a permanent error - the diagram cannot be opened at this
   * path any more. The reconnect loop has stopped and the canvas shows an unavailable state.
   */
  failed: boolean;
  /** Tells the backend what the canvas can see. */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the pipeline at `path` and folds its delta stream into a `PipelineModel`. The
 * transport, lifecycle, retry and state live in the shared `useDiagramStream` - whose
 * reconnect shape this hook's pre-extraction loop supplied - and what is the pipeline's own
 * here is the model, its mapping, and the viewport report built on the returned client.
 */
export function usePipelineStream(projectId: Uint8Array, path: readonly string[]): PipelineStream {
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
        // A viewport report is advisory: the backend answers a pipeline with the whole thing
        // anyway, so a dropped report costs nothing and must not surface as an error.
      });
  };

  return { model, loading, failed, reportView };
}
