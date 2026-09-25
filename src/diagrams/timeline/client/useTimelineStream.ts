import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type TimelineModel } from "./timelineModel";

export interface TimelineStream {
  model: TimelineModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /** True once the backend answered permanently - this timeline cannot be opened at this path. */
  failed: boolean;
  /**
   * Moves an element to a placement, in the module's own coordinate space: x in seconds since
   * the epoch, y in row-height units. A drag is a document edit - the backend converts the
   * point back into a begin, an end and a row, dispatches one command, and the change is one
   * undo away and reaches every other connection (Requirement 6.7).
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
  /**
   * Reports what the reader can see, in the module's own units - seconds across, row-derived y
   * down. The conversion from this canvas's seconds-per-pixel and vertical scale happens at the
   * call site, because the shared code converts nothing (Requirement 3.4).
   */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the timeline at `path` over `DiagramService.Open` and folds its delta stream into a
 * `TimelineModel`. The transport, lifecycle, retry and state live in the shared
 * {@link useDiagramStream}.
 *
 * `reportView` sends what the reader can see up the paired `UpdateView` leg, and the session
 * answers on this stream with the deltas that bring the connection into line
 * (view-delta-adoption Requirements 1.1-1.4). It is built from the shared
 * {@link viewReportOf} on the client the shared hook returns.
 *
 * This used to say there was deliberately no `reportView`, because the whole timeline arrived at
 * open and zoom and pan were the canvas's own transform. The first half stopped being true when
 * the session began filtering; the second half is still true and is the point - the transform
 * stays here, and only the rectangle it works out reaches the backend.
 */
export function useTimelineStream(projectId: Uint8Array, path: readonly string[]): TimelineStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);

  const reportView = viewReportOf(client, projectId, watchId, path);

  return { model, loading, failed, moveElementTo, reportView };
}
