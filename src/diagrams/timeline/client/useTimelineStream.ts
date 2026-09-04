import { useEffect, useRef, useState } from "react";
import { Code, ConnectError, createClient } from "@connectrpc/connect";
import { useAuth } from "@client/auth/AuthContext";
import { DiagramService } from "@client/generated/diagrams_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
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
 * `TimelineModel`, re-baselining on reconnect - the shape `useWardleyStream` established.
 *
 * `reportView` sends what the reader can see up the paired `UpdateView` leg, and the session
 * answers on this stream with the deltas that bring the connection into line
 * (view-delta-adoption Requirements 1.1-1.4). It is built from the shared
 * {@link viewReportOf} on the client this hook already holds, so reporting a view did not mean
 * adopting {@link useDiagramStream} - this hook still runs its own open loop (Requirement 3.6).
 *
 * This used to say there was deliberately no `reportView`, because the whole timeline arrived at
 * open and zoom and pan were the canvas's own transform. The first half stopped being true when
 * the session began filtering; the second half is still true and is the point - the transform
 * stays here, and only the rectangle it works out reaches the backend.
 */
export function useTimelineStream(projectId: Uint8Array, path: readonly string[]): TimelineStream {
  const { transport } = useAuth();
  const { watchId } = useContextConnection();
  const [model, setModel] = useState<TimelineModel>(emptyModel);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const clientRef = useRef(createClient(DiagramService, transport));

  const pathKey = path.join("/");

  useEffect(() => {
    const client = clientRef.current;
    const controller = new AbortController();
    let active = true;
    setModel(emptyModel);
    setFailed(false);
    setLoading(true);

    void (async () => {
      while (active) {
        try {
          const stream = client.open(
            { projectId: { value: projectId }, watchId: { value: watchId }, path: { segments: [...path] } },
            { signal: controller.signal },
          );
          for await (const delta of stream) {
            if (!active) {
              return;
            }
            setLoading(false);
            setModel((current) => applyDelta(current, delta));
          }
        } catch (error) {
          if (!active) {
            return;
          }
          if (
            error instanceof ConnectError &&
            (error.code === Code.FailedPrecondition ||
              error.code === Code.NotFound ||
              error.code === Code.Unimplemented)
          ) {
            setFailed(true);
            setLoading(false);
            return;
          }
          await new Promise((resolve) => setTimeout(resolve, 500));
          setModel(emptyModel);
        }
      }
    })();

    return () => {
      active = false;
      controller.abort();
    };
    // path is compared by value through pathKey, not by array identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId, watchId, pathKey]);

  const reportView = viewReportOf(clientRef.current, projectId, watchId, path);

  const moveElementTo = async (elementId: string, x: number, y: number): Promise<string> => {
    try {
      const response = await clientRef.current.moveElement({
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
