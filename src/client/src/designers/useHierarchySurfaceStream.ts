import { useCanvasRefusalReporter } from "@client/canvas/library/surface/canvasRefusals";
import { type CanvasStreamState, useCanvasStatusReporter } from "@client/canvas/library/surface/canvasStatus";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Code, ConnectError, createClient } from "@connectrpc/connect";
import { create } from "@bufbuild/protobuf";
import { useAuth } from "@client/auth/AuthContext";
import { PathSchema } from "@client/generated/connection_pb";
import { DesignerService } from "@client/generated/designers_pb";
import { EMPTY_HIERARCHY_SURFACE, type HierarchySurfaceModel } from "@client/hierarchySurface/library/api/hierarchySurfaceModel";
import { useContextConnection, useWorkspaceStreams } from "@client/shell/context/ContextConnectionProvider";
import { fromWire } from "./hierarchySurfaceWire";
import { editKey } from "./tableWire";

/** How long a dropped stream waits before re-opening: the table stream's delay, for the same reason. */
export const HIERARCHY_SURFACE_RECONNECT_DELAY_MS = 500;

export interface HierarchySurfaceStreamResult {
  model: HierarchySurfaceModel;
  /** True until the first model arrives - and again while a reconnect waits out its delay. */
  loading: boolean;
  /** The backend's own sentence when the page cannot be opened at this path; empty otherwise. */
  failure: string;
  /** How many gestures are accepted and not written yet. */
  pendingEdits: number;
  /** The reason the last gesture was refused or not written, until the next one is made; empty otherwise. */
  refusal: string;
  /**
   * Sends one gesture. Resolves to the backend's refusal as a sentence, or to `""` when it was accepted - which is
   * before it is written: its outcome arrives on the stream, and the model after it.
   */
  gesture: (operation: string, targetId: string, params?: Readonly<Record<string, string>>) => Promise<string>;
}

/**
 * A designer's hierarchy surface on the tab's one stream: opens the page at `path`, keeps the latest whole model,
 * re-opens on reconnect, and carries the person's gestures back (spec 029 R8).
 *
 * <b>This is the only place a hierarchy surface stream is opened</b>, as `useTableStream` is for a table:
 * `hierarchySurfaceStreamOpensOnlyInHook.test.ts` fails if a copy of the loop appears. A designer module calls this
 * hook and hands the model to the hierarchy surface library; it opens nothing itself.
 *
 * Every model is the whole page, so there is nothing to fold: the latest one replaces the one before it.
 */
export function useHierarchySurfaceStream(path: readonly string[]): HierarchySurfaceStreamResult {
  const { transport } = useAuth();
  const { watchId } = useContextConnection();
  const { openHierarchySurfaceStream } = useWorkspaceStreams();
  const client = useMemo(() => createClient(DesignerService, transport), [transport]);
  const [model, setModel] = useState<HierarchySurfaceModel>(EMPTY_HIERARCHY_SURFACE);
  const [loading, setLoading] = useState(true);
  const [failure, setFailure] = useState("");
  const [pending, setPending] = useState<ReadonlySet<string>>(new Set());
  const [refusal, setRefusal] = useState("");
  // True while a page that had arrived is being opened again after its connection dropped.
  const [reopening, setReopening] = useState(false);

  // Opening, reconnecting and unavailable are said by the frame around the tool, in the one appearance every tool
  // shares, and so is a refused gesture. A designer draws neither itself.
  const status = useCanvasStatusReporter();
  const refusals = useCanvasRefusalReporter();
  const refusalsRef = useRef(refusals);
  refusalsRef.current = refusals;
  const streamRef = useRef(Symbol("hierarchy surface stream"));
  useEffect(() => {
    const state: CanvasStreamState =
      failure !== "" ? { kind: "unavailable", reason: failure } : !loading ? { kind: "open" } : reopening ? { kind: "reconnecting" } : { kind: "opening" };
    status?.report(streamRef.current, state);
  }, [status, loading, failure, reopening]);
  useEffect(() => {
    const stream = streamRef.current;
    return () => status?.forget(stream);
  }, [status]);

  // The stream a gesture names, while one is open. A ref: gestures are sent from handlers, which must reach the
  // stream that is open now, not the one of the render they closed over.
  const streamIdRef = useRef<Uint8Array | null>(null);

  const pathKey = path.join("/");

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    setModel(EMPTY_HIERARCHY_SURFACE);
    setFailure("");
    setLoading(true);
    setPending(new Set());
    setRefusal("");
    setReopening(false);
    let arrived = false;

    void (async () => {
      while (active) {
        const streamId = crypto.getRandomValues(new Uint8Array(16));
        try {
          const stream = openHierarchySurfaceStream({ path: create(PathSchema, { segments: [...path] }), streamId }, { signal: controller.signal });
          for await (const wireEvent of stream) {
            if (!active) {
              return;
            }

            const event = fromWire(wireEvent);
            if (event === undefined) {
              continue;
            }

            if (event.kind === "outcome") {
              const { outcome } = event;
              setPending((current) => {
                const next = new Set(current);
                next.delete(outcome.editId);
                return next;
              });
              if (!outcome.written) {
                setRefusal(outcome.error);
                refusalsRef.current?.refused(outcome.error);
              }
              continue;
            }

            streamIdRef.current = streamId;
            arrived = true;
            setLoading(false);
            setReopening(false);
            setModel(event.model);
          }
        } catch (error) {
          if (!active) {
            return;
          }

          // A permanent answer ends the loop, as it does for a table: the backend said this page cannot be opened
          // here, and retrying would spin behind a tab showing nothing.
          if (
            error instanceof ConnectError &&
            (error.code === Code.FailedPrecondition || error.code === Code.NotFound || error.code === Code.Unimplemented)
          ) {
            streamIdRef.current = null;
            setFailure(error.rawMessage);
            setLoading(false);
            return;
          }
        }

        if (!active) {
          return;
        }

        // The connection dropped. The next model is the whole truth, and the gestures that were waiting died with the
        // session that held them. The page keeps showing the last model meanwhile, as the frame says it reconnects.
        streamIdRef.current = null;
        setPending(new Set());
        setReopening(arrived);
        setLoading(true);
        await new Promise((resolve) => setTimeout(resolve, HIERARCHY_SURFACE_RECONNECT_DELAY_MS));
      }
    })();

    return () => {
      active = false;
      streamIdRef.current = null;
      controller.abort();
    };
    // path is compared by value through pathKey, not by array identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openHierarchySurfaceStream, pathKey]);

  const gesture = useCallback(
    async (operation: string, targetId: string, params: Readonly<Record<string, string>> = {}): Promise<string> => {
      const streamId = streamIdRef.current;
      if (streamId === null) {
        return "The page is not open.";
      }

      const editId = crypto.getRandomValues(new Uint8Array(16));
      const key = editKey(editId);
      setRefusal("");
      refusalsRef.current?.attempted();
      // Counted before the call: the outcome can overtake the call's own answer.
      setPending((current) => new Set(current).add(key));
      const settle = (error: string) => {
        if (error !== "") {
          setPending((current) => {
            const next = new Set(current);
            next.delete(key);
            return next;
          });
          setRefusal(error);
          refusalsRef.current?.refused(error);
        }
        return error;
      };

      try {
        const response = await client.hierarchySurfaceGesture({
          watchId: { value: watchId },
          streamId: { value: streamId },
          editId: { value: editId },
          operation,
          targetId,
          params: { ...params },
        });
        return settle(response.error);
      } catch (error) {
        return settle(error instanceof Error ? error.message : "The change could not be sent.");
      }
    },
    [client, watchId],
  );

  return { model, loading, failure, pendingEdits: pending.size, refusal, gesture };
}
