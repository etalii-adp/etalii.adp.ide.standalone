import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type SparqlModel } from "./sparqlModel";

export interface SparqlStream {
  model: SparqlModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /** True once the backend answered permanently - this diagram cannot be opened at this path. */
  failed: boolean;
  /**
   * Stores an element's authored position, in the module's own coordinate space. This is the
   * only mutating call the client can make: the backend dispatches the core
   * SetRegistrationLayoutCommand, the position lands in the `.adp`'s layout: block - never in
   * the query - and the change is one undo away (Requirement 5.1).
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
  /**
   * Reports what the reader can see, so the session answers with the elements that newly fall
   * inside the viewport and removes the ones that left (view-delta-adoption Requirement 1.2).
   */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the diagram at `path` over `DiagramService.Open` and folds its delta stream into a
 * `SparqlModel`. The transport, lifecycle, retry and state live in the shared `useDiagramStream`.
 */
export function useSparqlStream(projectId: Uint8Array, path: readonly string[]): SparqlStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const reportView = viewReportOf(client, projectId, watchId, path);

  return { model, loading, failed, moveElementTo, reportView };
}
