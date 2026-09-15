import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type HelmModel } from "./helmModel";

export interface HelmStream {
  model: HelmModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /** True once the backend answered permanently - this diagram cannot be opened at this path. */
  failed: boolean;
  /**
   * Stores a box's authored position. A drag is a layout edit and nothing more: the backend
   * dispatches the core SetRegistrationLayoutCommand, the position lands in the `.adp`'s
   * `layout:` block - never in a chart file - and the change is one undo away (Requirement 6).
   * This is the one member the read-only sibling hook deliberately lacks, because this is the
   * one edit this type has.
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
  /**
   * Reports what the reader is looking at, so the backend answers with the elements that
   * newly fall inside it and removes the ones that left (view-delta-adoption Requirement 1.2).
   */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the chart diagram at `path` over `DiagramService.Open` and folds its delta stream into
 * a `HelmModel`. The transport, lifecycle, retry and state live in the shared `useDiagramStream`.
 * The whole chart is delivered at open and the view report narrows it from there.
 */
export function useHelmStream(projectId: Uint8Array, path: readonly string[]): HelmStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const reportView = viewReportOf(client, projectId, watchId, path);

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

  return { model, loading, failed, moveElementTo, reportView };
}
