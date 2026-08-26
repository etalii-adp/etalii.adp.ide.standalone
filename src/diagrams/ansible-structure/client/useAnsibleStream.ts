import { useEffect, useRef, useState } from "react";
import { Code, ConnectError, createClient } from "@connectrpc/connect";
import { useAuth } from "@client/auth/AuthContext";
import { DiagramService } from "@client/generated/diagrams_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { applyDelta, emptyModel, type AnsibleModel } from "./ansibleModel";

/** A viewport the client reports; the backend answers with what falls inside it. */
export interface Viewport {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface AnsibleStream {
  model: AnsibleModel;
  /** True until the first delta arrives, so the canvas can show it is loading rather than empty. */
  loading: boolean;
  /**
   * True once the backend answered with a permanent error - the diagram cannot be opened at
   * this path any more (deleted, moved, unroutable). The reconnect loop has stopped.
   */
  failed: boolean;
  /** Tells the backend what the canvas can see, so a large project does not stream in full. */
  reportView: (viewport: Viewport) => void;
}

/**
 * Opens the diagram at `path` over `DiagramService.Open` and folds its delta stream into an
 * `AnsibleModel`, re-baselining on reconnect.
 *
 * Note what this hook does NOT expose, compared with `useMindmapStream`: there is no
 * `moveElement`. The backend refuses one with a sentence, so offering the call here would put a
 * gesture in the client's reach whose only possible outcome is a refusal. A read-only type's
 * client should not be able to ask.
 */
export function useAnsibleStream(projectId: Uint8Array, path: readonly string[]): AnsibleStream {
  const { transport } = useAuth();
  const { watchId } = useContextConnection();
  const [model, setModel] = useState<AnsibleModel>(emptyModel);
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
      // A dropped stream re-opens with the same request; the first message is always the
      // current diagram, so the client re-baselines without a protocol of its own.
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
          // here. Retrying would spin forever behind a tab showing nothing. Everything else
          // stays a transient fault and keeps the reconnect behaviour.
          if (
            error instanceof ConnectError &&
            (error.code === Code.FailedPrecondition || error.code === Code.NotFound || error.code === Code.Unimplemented)
          ) {
            setFailed(true);
            setLoading(false);
            return;
          }
          // Brief back-off before re-opening, so a persistent failure does not spin.
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
        // A view report is advisory; if it fails the backend keeps the last one it had.
      });
  };

  return { model, loading, failed, reportView };
}
