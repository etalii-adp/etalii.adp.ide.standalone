import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { applySkosDelta, emptySkosModel, type SkosModel } from "./skosModel";

export interface SkosStream {
  model: SkosModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /** True once the backend answered permanently - this diagram cannot be opened at this path. */
  failed: boolean;

  /** Tells the backend what this canvas can see, so a large document arrives as it is looked at. */
  reportView: (viewport: Viewport) => void;
  /**
   * Stores an element's authored position. A drag is a layout edit: the backend dispatches the
   * core SetRegistrationLayoutCommand, the position lands in the `.adp`'s layout: block - never
   * in the SKOS file - and the change is one undo away (skos-diagram Requirement 4.3).
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
}

/**
 * Opens the scheme diagram at `path` and folds its delta stream into a `SkosModel`. The
 * transport, lifecycle, retry and state live in the shared `useDiagramStream`.
 */
export function useSkosStream(projectId: Uint8Array, path: readonly string[]): SkosStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client } = useDiagramStream(projectId, path, emptySkosModel, applySkosDelta);

  const moveElementTo = async (elementId: string, x: number, y: number): Promise<string> => {
    try {
      const response = await client.moveElement({
        projectId: { value: projectId },
        watchId: { value: watchId },
        path: { segments: [...path] },
        elementId,
        position: { x, y },
      });
      return response.error;
    } catch (error) {
      return error instanceof Error ? error.message : "The move could not be sent.";
    }
  };

  const reportView = viewReportOf(client, projectId, watchId, path);

  return { model, loading, failed, reportView, moveElementTo };
}
