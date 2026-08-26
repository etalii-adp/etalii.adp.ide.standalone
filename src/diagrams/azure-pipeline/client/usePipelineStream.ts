import { useEffect, useRef, useState } from "react";
import { Code, ConnectError, createClient } from "@connectrpc/connect";
import { useAuth } from "@client/auth/AuthContext";
import { DiagramService } from "@client/generated/diagrams_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { applyDelta, emptyModel, type PipelineModel } from "./pipelineModel";

/** A viewport the client reports; the backend answers with what falls inside it. */
export interface Viewport {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface PipelineStream {
  model: PipelineModel;
  /** True until the first delta arrives, so the canvas shows it is loading rather than empty. */
  loading: boolean;
  /**
   * True once the backend answered with a permanent error - the diagram cannot be opened at this
   * path any more. The reconnect loop has stopped and the canvas shows an unavailable state.
   */
  failed: boolean;
  /** Tells the backend what the canvas can see. */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the pipeline at `path` over `DiagramService.Open` and folds its delta stream into a
 * `PipelineModel`, re-baselining on reconnect. Viewport reports go up the paired unary
 * `UpdateView` leg - the shape `useMindmapStream` established and `useC4Stream` followed.
 */
export function usePipelineStream(projectId: Uint8Array, path: readonly string[]): PipelineStream {
  const { transport } = useAuth();
  const { watchId } = useContextConnection();
  const [model, setModel] = useState<PipelineModel>(emptyModel);
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

          // A permanent answer ends the loop: the backend said this diagram cannot be opened
          // here - missing, unroutable, or a file that does not parse.
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
        }

        if (!active) {
          return;
        }

        // The stream ended without a permanent answer, so the connection dropped. Re-open, and
        // start from an empty model: the next baseline is the whole truth, and folding it into a
        // stale one would leave whatever went away while we were disconnected on the canvas.
        setModel(emptyModel);
        setLoading(true);
        await new Promise((resolve) => setTimeout(resolve, 500));
      }
    })();

    return () => {
      active = false;
      controller.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId, pathKey, watchId]);

  const reportView = (viewport: Viewport) => {
    void clientRef.current
      .updateView({
        projectId: { value: projectId },
        watchId: { value: watchId },
        path: { segments: [...path] },
        view: {
          center: { x: (viewport.minX + viewport.maxX) / 2, y: (viewport.minY + viewport.maxY) / 2 },
          boundingBox: { min: { x: viewport.minX, y: viewport.minY }, max: { x: viewport.maxX, y: viewport.maxY } },
        },
      })
      .catch(() => {
        // A viewport report is advisory: the backend answers a pipeline with the whole thing
        // anyway, so a dropped report costs nothing and must not surface as an error.
      });
  };

  return { model, loading, failed, reportView };
}
