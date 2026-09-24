import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { applyShaclDelta, emptyShaclModel, type ShaclModel } from "./shaclModel";

export interface ShaclStream {
  model: ShaclModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /** True once the backend answered permanently - this diagram cannot be opened at this path. */
  failed: boolean;

  /** Tells the backend what this canvas can see, so a large document arrives as it is looked at. */
  reportView: (viewport: Viewport) => void;
  /**
   * Stores an element's authored position. A drag is a layout edit: the backend dispatches the
   * core SetRegistrationLayoutCommand, the position lands in the `.adp`'s layout: block - never
   * in the shapes file - and the change is one undo away (shacl-diagram Requirement 3.2).
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
}

/**
 * Opens the shapes diagram at `path` and folds its delta stream into a `ShaclModel`. The
 * transport, lifecycle, retry and state live in the shared `useDiagramStream`.
 */
export function useShaclStream(projectId: Uint8Array, path: readonly string[]): ShaclStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyShaclModel, applyShaclDelta);

  const reportView = viewReportOf(client, projectId, watchId, path);

  return { model, loading, failed, reportView, moveElementTo };
}
