import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { applyDelta, emptyModel, type RdfModel } from "./rdfModel";

export interface RdfStream {
  model: RdfModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /** True once the backend answered permanently - this diagram cannot be opened at this path. */
  failed: boolean;

  /** Tells the backend what this canvas can see, so a large document arrives as it is looked at. */
  reportView: (viewport: Viewport) => void;
  /**
   * Stores a card's authored position, in the module's own coordinate space. A drag is a layout
   * edit: the backend dispatches the core SetRegistrationLayoutCommand, the position lands in
   * the `.adp`'s layout: block - never in the RDF file - and the change is one undo away
   * (Requirement 4).
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
}

/**
 * Opens the diagram at `path` over `DiagramService.Open` and folds its delta stream into an
 * `RdfModel`. The transport, lifecycle, retry and state live in the shared `useDiagramStream`.
 */
export function useRdfStream(projectId: Uint8Array, path: readonly string[]): RdfStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const moveElementTo = async (elementId: string, x: number, y: number): Promise<string> => {
    try {
      const response = await client.moveElement({
        projectId: { value: projectId },
        watchId: { value: watchId },
        path: { segments: [...path] },
        elementId,
        // The position is what makes this an arrangement rather than a re-parenting; the
        // backend routes on its presence.
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
