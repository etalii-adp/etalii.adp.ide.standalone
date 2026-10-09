import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Code, ConnectError, createClient } from "@connectrpc/connect";
import { create } from "@bufbuild/protobuf";
import { useAuth } from "@client/auth/AuthContext";
import { PathSchema } from "@client/generated/connection_pb";
import { DesignerService } from "@client/generated/designers_pb";
import { useContextConnection, useWorkspaceStreams } from "@client/shell/context/ContextConnectionProvider";
import type { TableGesture } from "@client/table/library/api/tableEvents";
import { applyTableEvent, EMPTY_TABLE, type TableModel } from "@client/table/library/api/tableModel";
import type { RowWindow } from "@client/table/library/rows/windowing";
import { editKey, fromWire } from "./tableWire";

/** How long a dropped stream waits before re-opening: the diagram stream's delay, for the same reason. */
export const TABLE_RECONNECT_DELAY_MS = 500;

export interface TableStreamResult {
  model: TableModel;
  /** True until the baseline arrives - and again while a reconnect waits out its delay. */
  loading: boolean;
  /** The backend's own sentence when the table cannot be opened at this path; empty otherwise. */
  failure: string;
  /** How many edits are shown and not written yet. */
  pendingEdits: number;
  /** The reason the last edit was refused or taken back, until the next edit is made; empty otherwise. */
  refusal: string;
  /** Tells the backend which lines are in sight. The rows arrive on the stream. */
  setWindow: (window: RowWindow) => void;
  /** Tells the backend which view this tab shows. The structure arrives on the stream. */
  setView: (viewId: string) => void;
  /**
   * Sends one gesture. Resolves to the backend's refusal as a sentence, or to `""` when the edit
   * was accepted - which is before it is written: its outcome arrives on the stream.
   */
  edit: (gesture: TableGesture) => Promise<string>;
}

/**
 * A designer's table on the tab's one stream: opens the table at `path`, folds the stream's
 * events into a table model, re-baselines on reconnect, and carries the window, the view and
 * the author's gestures back.
 *
 * <b>This is the only place a table stream is opened</b>, as `useDiagramStream` is for a diagram:
 * `tableStreamOpensOnlyInHook.test.ts` fails if a copy of the loop appears. A designer module
 * calls this hook and hands the model to the table library; it opens nothing itself.
 *
 * <b>An edit is shown at once and written behind.</b> The call that sends a gesture answers when
 * the backend accepts it; the write follows, and its outcome arrives on the stream under the
 * edit's id. Until then the edit is counted in `pendingEdits`, and a refused write takes it, and
 * every edit that waited on it, back - the backend pushes the rows as they really are, and the
 * reason is kept in `refusal`.
 */
export function useTableStream(path: readonly string[]): TableStreamResult {
  const { transport } = useAuth();
  const { watchId } = useContextConnection();
  const { openTableStream } = useWorkspaceStreams();
  const client = useMemo(() => createClient(DesignerService, transport), [transport]);
  const [model, setModel] = useState<TableModel>(EMPTY_TABLE);
  const [loading, setLoading] = useState(true);
  const [failure, setFailure] = useState("");
  const [pending, setPending] = useState<ReadonlySet<string>>(new Set());
  const [refusal, setRefusal] = useState("");
  // The stream the table's other calls name, while one is open. A ref: the calls are made from
  // handlers, which must reach the stream that is open now, not the one of the render they closed over.
  const streamIdRef = useRef<Uint8Array | null>(null);
  const windowRef = useRef<RowWindow | null>(null);

  const pathKey = path.join("/");

  const sendWindow = useCallback(
    (streamId: Uint8Array, window: RowWindow) => {
      void client
        .setTableWindow({ watchId: { value: watchId }, streamId: { value: streamId }, first: window.first, count: window.count })
        .catch(() => {
          // The stream is gone or going; the reconnect sends the window again.
        });
    },
    [client, watchId],
  );

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    setModel(EMPTY_TABLE);
    setFailure("");
    setLoading(true);
    setPending(new Set());
    setRefusal("");

    void (async () => {
      while (active) {
        const streamId = crypto.getRandomValues(new Uint8Array(16));
        try {
          const stream = openTableStream({ path: create(PathSchema, { segments: [...path] }), streamId }, { signal: controller.signal });
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
                outcome.takenBack.forEach((id) => next.delete(id));
                return next;
              });
              if (!outcome.written) {
                setRefusal(outcome.error);
              }
              continue;
            }

            if (event.kind === "baseline") {
              // The stream is running from here: its other calls may name it, and the window the
              // surface last asked for is asked again, since this session has never heard it.
              streamIdRef.current = streamId;
              setLoading(false);
              if (windowRef.current !== null) {
                sendWindow(streamId, windowRef.current);
              }
            }
            setModel((current) => applyTableEvent(current, event));
          }
        } catch (error) {
          if (!active) {
            return;
          }

          // A permanent answer ends the loop, as it does for a diagram: the backend said this
          // table cannot be opened here, and retrying would spin behind a tab showing nothing.
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

        // The connection dropped. Start over from an empty table: the next baseline is the whole
        // truth, and the edits that were waiting died with the session that held them.
        streamIdRef.current = null;
        setModel(EMPTY_TABLE);
        setPending(new Set());
        setLoading(true);
        await new Promise((resolve) => setTimeout(resolve, TABLE_RECONNECT_DELAY_MS));
      }
    })();

    return () => {
      active = false;
      streamIdRef.current = null;
      controller.abort();
    };
    // path is compared by value through pathKey, not by array identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openTableStream, pathKey, sendWindow]);

  const setWindow = useCallback(
    (window: RowWindow) => {
      windowRef.current = window;
      if (streamIdRef.current !== null) {
        sendWindow(streamIdRef.current, window);
      }
    },
    [sendWindow],
  );

  const setView = useCallback(
    (viewId: string) => {
      const streamId = streamIdRef.current;
      if (streamId !== null) {
        void client.setTableView({ watchId: { value: watchId }, streamId: { value: streamId }, viewId }).catch(() => {
          // The stream is gone or going; the table re-opens on the view the document names.
        });
      }
    },
    [client, watchId],
  );

  const edit = useCallback(
    async (gesture: TableGesture): Promise<string> => {
      const streamId = streamIdRef.current;
      if (streamId === null) {
        return "The table is not open.";
      }

      const editId = crypto.getRandomValues(new Uint8Array(16));
      const key = editKey(editId);
      setRefusal("");
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
        }
        return error;
      };

      try {
        const response = await client.edit({
          watchId: { value: watchId },
          streamId: { value: streamId },
          editId: { value: editId },
          gesture: {
            kind: gesture.kind,
            rowId: gesture.rowId ?? "",
            columnId: gesture.columnId ?? "",
            viewId: gesture.viewId ?? "",
            values: [...(gesture.values ?? [])],
            index: gesture.index ?? 0,
            targetId: gesture.targetId ?? "",
            settings: { ...(gesture.settings ?? {}) },
          },
        });
        return settle(response.error);
      } catch (error) {
        return settle(error instanceof Error ? error.message : "The edit could not be sent.");
      }
    },
    [client, watchId],
  );

  return { model, loading, failure, pendingEdits: pending.size, refusal, setWindow, setView, edit };
}
