import { useEffect, useRef, useState } from "react";
import { Code, ConnectError, createClient } from "@connectrpc/connect";
import { useAuth } from "@client/auth/AuthContext";
import { DiagramService } from "@client/generated/diagrams_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
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
 * stream into a `DatabricksModel`, re-baselining on reconnect - the shape `useTimelineStream`
 * established. One hook for the family's three types: the backend decides which reading a path
 * shows, and the model holds whichever element types arrive.
 */
export function useDatabricksStream(projectId: Uint8Array, path: readonly string[]): DatabricksStream {
  const { transport } = useAuth();
  const { watchId } = useContextConnection();
  const [model, setModel] = useState<DatabricksModel>(emptyModel);
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

  const reportView = viewReportOf(clientRef.current, projectId, watchId, path);

  return { model, loading, failed, moveElementTo, reportView };
}
