import { useEffect, useMemo, useState } from "react";
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
   * The service client the stream runs on, so a module can build its own unary calls on the same
   * client without a second `createClient`. The move is no longer one of them - see
   * {@link DiagramStreamResult.moveElementTo}.
   */
  client: Client<typeof DiagramService>;
  /**
   * Records where the user dropped an element, in canvas units. Resolves to the backend's refusal
   * as a sentence, or to `""` when the move was accepted.
   *
   * <b>One implementation, where fourteen modules each wrote their own</b>
   * (client-centralization Requirement 7). Thirteen of those bodies were byte-identical once
   * comments were set aside. The fourteenth, `ansible-structure`'s, caught the failure and
   * returned a fixed "The position could not be saved." - so a backend explaining WHY a move was
   * refused was replaced by a sentence that explains nothing. This reports what was actually said,
   * which is the specification's one permitted visible change besides the theme.
   *
   * <b>This reverses a deliberate decision</b>, and the reason is recorded where the decision was:
   * archived `technical-debt-cleanup` R3.2 kept the move per module so each could shape its own;
   * the user reversed it on 2026-09-20 because in practice none did - every body was the same call.
   */
  moveElementTo: (elementId: string, x: number, y: number) => Promise<string>;
}

/**
 * The transport, lifecycle, retry and state shared by every diagram type's stream hook: opens
 * the diagram at `path` over `DiagramService.Open` and folds its delta stream into a model with
 * `applyDelta`, re-baselining on reconnect. A dropped stream re-opens with the same request;
 * the first message is always the current document, so the client re-baselines without a
 * protocol of its own.
 *
 * What stays per module: the model type, its empty value and the response-to-model mapping.
 *
 * <b>The move used to be on that list, and is not any more.</b> technical-debt-cleanup R3.2 kept
 * it per module deliberately, built by each module's own wrapper on the returned client; the user
 * reversed that on 2026-09-20 (client-centralization Requirement 7), because all fourteen wrappers
 * turned out to be the same call. It is {@link DiagramStreamResult.moveElementTo} now. The sentence
 * saying otherwise stood here until the change that made it false, which is the only point at
 * which a comment like it gets corrected rather than repeated.
 *
 * The view report is no longer among them: it is shared, in `viewReport.ts` beside this file, and
 * a module builds it with `viewReportOf` on that same client.
 *
 * This is the only place the stream is opened. Nine modules once hand-rolled their own open loop
 * and drifted from this one - a clean end re-opened at once and kept the stale model - so they
 * were moved onto it, and `diagramStreamOpensOnlyInHook.test.ts` fails if a copy reappears.
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
  // Forces the editor family on the backend's resolution - the "Open as text" tab's stream
  // (modular-text-editors R5.2). Empty, the default, opens the file as it resolves today.
  editorId = "",
): DiagramStreamResult<TModel> {
  const { transport } = useAuth();
  const { watchId } = useContextConnection();
  const [model, setModel] = useState<TModel>(emptyModel);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  // One shape for acquiring a service client, `useMemo` on the transport - the same at every
  // site that needs one. The `useRef` this replaced was safe, and it is worth saying why so
  // the next reader does not have to re-derive it: `transport` is memoised on a `[]`-stable
  // callback and reads its token through a ref, so it never changes identity. What it cost was
  // small and real - `createClient` ran on every render and the result was thrown away, and
  // this hook backs every open diagram tab.
  const client = useMemo(() => createClient(DiagramService, transport), [transport]);

  const pathKey = path.join("/");

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    setModel(emptyModel);
    setFailed(false);
    setLoading(true);

    void (async () => {
      while (active) {
        try {
          const stream = client.open(
            { projectId: { value: projectId }, watchId: { value: watchId }, path: { segments: [...path] }, editorId },
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
  }, [client, projectId, watchId, pathKey, editorId]);

  const moveElementTo = async (elementId: string, x: number, y: number): Promise<string> => {
    try {
      const response = await client.moveElement({
        projectId: { value: projectId },
        watchId: { value: watchId },
        path: { segments: [...path] },
        elementId,
        // The position is what makes this an arrangement rather than a re-parenting; the backend
        // routes on its presence.
        position: { x, y },
      });
      return response.error;
    } catch (error) {
      return error instanceof Error ? error.message : "The move could not be sent.";
    }
  };

  return { model, loading, failed, client, moveElementTo };
}
