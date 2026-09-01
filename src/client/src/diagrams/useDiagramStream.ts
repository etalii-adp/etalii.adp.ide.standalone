import { useEffect, useRef, useState } from "react";
import { Code, ConnectError, createClient, type Client } from "@connectrpc/connect";
import { useAuth } from "@client/auth/AuthContext";
import type { Delta } from "@client/generated/deltas_pb";
import { DiagramService } from "@client/generated/diagrams_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";

/**
 * How long a dropped stream waits before re-opening - the one place this number is written.
 * Applied to a clean stream end and a transient error alike, so a server that keeps closing
 * the stream cannot hot-loop the client (technical-debt-cleanup R3.4).
 */
export const RECONNECT_DELAY_MS = 500;

export interface DiagramStreamResult<TModel> {
  model: TModel;
  /** True until the first delta arrives - and again while a reconnect waits out its backoff. */
  loading: boolean;
  /**
   * True once the backend answered with a permanent error - the diagram cannot be opened at
   * this path any more (deleted, moved, unroutable, or a type without a session). The
   * reconnect loop has stopped; the canvas shows an unavailable state instead
   * (diagram-workspace-tabs Requirement 5.1).
   */
  failed: boolean;
  /**
   * The service client the stream runs on, so a module's thin wrapper can build its own unary
   * calls (`reportView`, a move mutation) on the same client without a second `createClient`.
   */
  client: Client<typeof DiagramService>;
}

/**
 * The transport, lifecycle, retry and state shared by every diagram type's stream hook: opens
 * the diagram at `path` over `DiagramService.Open` and folds its delta stream into a model with
 * `applyDelta`, re-baselining on reconnect. A dropped stream re-opens with the same request;
 * the first message is always the current document, so the client re-baselines without a
 * protocol of its own.
 *
 * What stays per module, deliberately (technical-debt-cleanup R3.2): the model type, its empty
 * value, the response-to-model mapping, and any `reportView`/move call - built by the module's
 * own wrapper on the returned {@link DiagramStreamResult.client}.
 *
 * The reconnect shape is `usePipelineStream`'s pre-extraction one, chosen per R3.3's recorded
 * comparison: `loading` returns to `true` before the retry delay (the canvas shows
 * "reconnecting", not a silently-empty, not-loading model), and a clean stream end waits out
 * the same {@link RECONNECT_DELAY_MS} as a thrown error.
 */
export function useDiagramStream<TModel>(
  projectId: Uint8Array,
  path: readonly string[],
  emptyModel: TModel,
  applyDelta: (current: TModel, delta: Delta) => TModel,
): DiagramStreamResult<TModel> {
  const { transport } = useAuth();
  const { watchId } = useContextConnection();
  const [model, setModel] = useState<TModel>(emptyModel);
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
          // here. Retrying would spin forever behind a tab showing nothing. Everything else
          // stays a transient fault and keeps the reconnect behaviour.
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
        // start from an empty model: the next baseline is the whole truth, and folding it into
        // a stale one would leave whatever went away while we were disconnected on the canvas.
        setModel(emptyModel);
        setLoading(true);
        await new Promise((resolve) => setTimeout(resolve, RECONNECT_DELAY_MS));
      }
    })();

    return () => {
      active = false;
      controller.abort();
    };
    // path is compared by value through pathKey, not by array identity; emptyModel and
    // applyDelta are a module's own constants, stable by construction.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId, watchId, pathKey]);

  return { model, loading, failed, client: clientRef.current };
}
