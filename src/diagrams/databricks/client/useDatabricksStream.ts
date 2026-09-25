import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type DatabricksModel } from "./databricksModel";

export type { Viewport };

export interface DatabricksStream {
  model: DatabricksModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /** True once the backend answered permanently - this diagram cannot be opened at this path. */
  failed: boolean;
  /**
   * Stores a box's authored position, in the module's own coordinate space. A drag is a layout
   * edit: the backend dispatches the core SetRegistrationLayoutCommand, the position lands in
   * the `.adp`'s layout: block - never in the body file - and the change is one undo away
   * (Requirement 7).
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
  /**
   * Tells the backend what this connection can currently see, so it can answer on the open
   * stream with the deltas that bring the diagram into line. Advisory: a rejected report leaves
   * the backend on the last viewport it had.
   */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens one of the family's diagrams at `path` over `DiagramService.Open` and folds its delta
 * stream into a `DatabricksModel`. The transport, lifecycle, retry and state live in the shared
 * `useDiagramStream`. One hook for the family's three types: the backend decides which reading a
 * path shows, and the model holds whichever element types arrive.
 */
export function useDatabricksStream(projectId: Uint8Array, path: readonly string[]): DatabricksStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const reportView = viewReportOf(client, projectId, watchId, path);

  return { model, loading, failed, moveElementTo, reportView };
}
