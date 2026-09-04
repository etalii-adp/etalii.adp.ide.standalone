import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type PipelineModel } from "./pipelineModel";

/** Re-exported so the module's own files keep one name for it; the shape is the shared one. */
export type { Viewport };

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

  const reportView = viewReportOf(client, projectId, watchId, path);

  return { model, loading, failed, reportView };
}
