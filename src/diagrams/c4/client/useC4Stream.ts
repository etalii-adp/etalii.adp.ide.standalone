import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type C4Model } from "./c4Model";

/** Re-exported so the module's own files keep one name for it; the shape is the shared one. */
export type { Viewport };

export interface C4Stream {
  model: C4Model;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /**
   * True once the backend answered with a permanent error - the diagram cannot be opened at
   * this path any more. The reconnect loop has stopped and the canvas shows an unavailable
   * state instead (diagram-workspace-tabs Requirement 5.1).
   */
  failed: boolean;
  /**
   * Records where the user dropped an element, in canvas units.
   *
   * A position rather than a parent: a C4 element belongs to whatever declares it in the
   * document, and no drag says otherwise. The backend keeps this in the layout sidecar beside
   * the `.dsl` rather than in it, and dispatches it as a command - so a drag is one undo away.
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
  /** Tells the backend what the canvas can see, so a large model does not stream in full. */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the diagram at `path` and folds its delta stream into a `C4Model`. The transport,
 * lifecycle, retry and state live in the shared `useDiagramStream`; what is C4's own here is
 * the model, its mapping, and the two unary calls built on the returned client.
 */
export function useC4Stream(projectId: Uint8Array, path: readonly string[]): C4Stream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const reportView = viewReportOf(client, projectId, watchId, path);

  return { model, loading, failed, reportView, moveElementTo };
}
