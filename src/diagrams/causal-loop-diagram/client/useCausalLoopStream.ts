import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type CausalLoopModel } from "./causalLoopModel";

/** Re-exported so the module's own files keep one name for it; the shape is the shared one. */
export type { Viewport };

export interface CausalLoopStream {
  model: CausalLoopModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /** True once the backend answered permanently - this diagram cannot be opened at this path. */
  failed: boolean;
  /**
   * Records where the user dropped a variable, in canvas units. A position rather than a
   * parent: nothing in this diagram nests, and the backend keeps the arrangement in the .adp
   * beside the .cld rather than in it, so a drag is one undo away and never a document edit.
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
  /** Tells the backend what the canvas can see, so what it holds follows the reader. */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the diagram at `path` and folds its delta stream into a `CausalLoopModel`. The transport,
 * lifecycle, retry and state live in the shared `useDiagramStream`; what is this module's own is
 * the model, its mapping, and the two unary calls built on the returned client.
 */
export function useCausalLoopStream(projectId: Uint8Array, path: readonly string[]): CausalLoopStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const reportView = viewReportOf(client, projectId, watchId, path);

  return { model, loading, failed, moveElementTo, reportView };
}
