import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type DependencyGraphModel } from "./dependencyGraphModel";

export type { Viewport };

export interface DependencyGraphStream {
  model: DependencyGraphModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /** True once the backend answered permanently - this graph cannot be opened at this path. */
  failed: boolean;
  /**
   * Moves a node to a placement, in the module's own coordinate space: x in canvas units, y in
   * row-height units. A drag is a document edit - the backend converts the point back into a
   * coordinate and a row, dispatches one command, and the change is one undo away and reaches
   * every other connection.
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
  /**
   * Tells the backend which rectangle the canvas can see, so a graph larger than the view is
   * not held in full. The rectangle is in the module's own units - x as authored, y in the
   * same row-height units `moveElementTo` uses - because the shared library converts nothing.
   */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the graph at `path` over `DiagramService.Open` and folds its delta stream into a
 * `DependencyGraphModel`. The transport, lifecycle, retry and state live in the shared
 * `useDiagramStream`; `reportView` and `moveElementTo` are built on the client it returns.
 *
 * The earlier note here said there was deliberately no report because the whole graph is
 * delivered at open; that is what view-delta adoption changed.
 */
export function useDependencyGraphStream(projectId: Uint8Array, path: readonly string[]): DependencyGraphStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const reportView = viewReportOf(client, projectId, watchId, path);

  return { model, loading, failed, moveElementTo, reportView };
}
