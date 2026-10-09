import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { Code, ConnectError } from "@connectrpc/connect";
import { TableBaselineSchema, TableChangeSchema } from "@client/generated/designers_pb";
import type { TableStreamEvent } from "@client/shell/context/workspaceStreams";
import { TABLE_RECONNECT_DELAY_MS, useTableStream } from "./useTableStream";

/**
 * The table stream hook by behaviour: the baseline and the changes fold into a model, the window
 * is sent once the stream runs and again after a reconnect, an edit is counted until its outcome
 * arrives, and a permanent refusal ends the loop.
 */

/** A stream the test feeds by hand and can end or fail. */
class ManualStream {
  private readonly queue: TableStreamEvent[] = [];
  private waiting: ((result: IteratorResult<TableStreamEvent>) => void) | undefined;
  private reject: ((error: unknown) => void) | undefined;
  private done = false;

  push(event: TableStreamEvent) {
    if (this.waiting) {
      const resolve = this.waiting;
      this.waiting = undefined;
      resolve({ value: event, done: false });
    } else {
      this.queue.push(event);
    }
  }

  end() {
    this.done = true;
    this.waiting?.({ value: undefined, done: true });
    this.waiting = undefined;
  }

  fail(error: unknown) {
    this.done = true;
    this.reject?.(error);
  }

  [Symbol.asyncIterator](): AsyncIterator<TableStreamEvent> {
    return {
      next: () => {
        const item = this.queue.shift();
        if (item !== undefined) {
          return Promise.resolve({ value: item, done: false });
        }
        if (this.done) {
          return Promise.resolve({ value: undefined, done: true });
        }
        return new Promise((resolve, reject) => {
          this.waiting = resolve;
          this.reject = reject;
        });
      },
    };
  }
}

const calls = vi.hoisted(() => ({
  streams: [] as { stream: unknown; streamId: Uint8Array }[],
  setTableWindow: vi.fn(async (_request: unknown) => ({})),
  setTableView: vi.fn(async (_request: unknown) => ({})),
  edit: vi.fn(async (_request: unknown) => ({ error: "" })),
}));

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return { ...actual, createClient: () => ({ setTableWindow: calls.setTableWindow, setTableView: calls.setTableView, edit: calls.edit }) };
});

vi.mock("@client/auth/AuthContext", () => {
  const transport = {};
  return { useAuth: () => ({ transport }) };
});

vi.mock("@client/shell/context/ContextConnectionProvider", () => {
  const watchId = new Uint8Array(16);
  // One object, as the provider's is memoised: the hook keys its effect on it.
  const streams = {
    openTableStream: (request: { streamId: Uint8Array }) => {
      const stream = new ManualStream();
      calls.streams.push({ stream, streamId: request.streamId });
      return stream;
    },
  };
  return { useContextConnection: () => ({ watchId }), useWorkspaceStreams: () => streams };
});

const baseline = (rowCount: number): TableStreamEvent => ({
  kind: "baseline",
  baseline: create(TableBaselineSchema, {
    title: "Cities",
    columns: [{ id: "p1", name: "Name", kind: "text", visible: true, isTitle: true }],
    views: [{ id: "v1", name: "All" }],
    settings: { viewId: "v1" },
    rowCount,
  }),
});

const rows = (first: number, ids: string[], rowCount: number): TableStreamEvent => ({
  kind: "change",
  change: create(TableChangeSchema, { change: { case: "rows", value: { first, rowCount, rows: ids.map((id) => ({ id })) } } }),
});

const outcome = (editId: Uint8Array, written: boolean, error = ""): TableStreamEvent => ({
  kind: "change",
  change: create(TableChangeSchema, { change: { case: "outcome", value: { editId: { value: editId }, written, error } } }),
});

const streamAt = (index: number) => calls.streams[index]!.stream as ManualStream;
const settle = () => act(async () => { await Promise.resolve(); await Promise.resolve(); });

beforeEach(() => {
  calls.streams.length = 0;
  calls.setTableWindow.mockClear();
  calls.setTableView.mockClear();
  calls.edit.mockClear();
  calls.edit.mockImplementation(async () => ({ error: "" }));
});

afterEach(() => {
  vi.useRealTimers();
});

describe("useTableStream", () => {
  it("folds the baseline and the window's rows into the model", async () => {
    // Arrange.
    const { result } = renderHook(() => useTableStream(["cities.adp"]));
    expect(result.current.loading).toBe(true);

    // Act.
    await act(async () => streamAt(0).push(baseline(3)));
    await act(async () => streamAt(0).push(rows(1, ["r1", "r2"], 3)));

    // Assert.
    expect(result.current.loading).toBe(false);
    expect(result.current.model.title).toBe("Cities");
    expect(result.current.model.columns.map((column) => column.name)).toEqual(["Name"]);
    expect([...result.current.model.rows.entries()].map(([index, row]) => `${index}:${row.id}`)).toEqual(["1:r1", "2:r2"]);
  });

  it("sends the window once the stream runs, under the stream's id", async () => {
    // Arrange: the surface asks before the baseline has arrived.
    const { result } = renderHook(() => useTableStream(["cities.adp"]));
    act(() => result.current.setWindow({ first: 0, count: 25 }));
    expect(calls.setTableWindow).not.toHaveBeenCalled();

    // Act.
    await act(async () => streamAt(0).push(baseline(100)));

    // Assert.
    expect(calls.setTableWindow).toHaveBeenCalledTimes(1);
    expect(calls.setTableWindow.mock.calls[0]![0]).toMatchObject({ first: 0, count: 25, streamId: { value: calls.streams[0]!.streamId } });

    // And a later window goes straight out.
    act(() => result.current.setWindow({ first: 40, count: 25 }));
    expect(calls.setTableWindow).toHaveBeenCalledTimes(2);
    expect(calls.setTableWindow.mock.calls[1]![0]).toMatchObject({ first: 40, count: 25 });
  });

  it("starts over after the stream drops, and asks for the window again on the new stream", async () => {
    // Arrange.
    vi.useFakeTimers();
    const { result } = renderHook(() => useTableStream(["cities.adp"]));
    await act(async () => streamAt(0).push(baseline(100)));
    act(() => result.current.setWindow({ first: 40, count: 25 }));
    await act(async () => streamAt(0).push(rows(40, ["r40"], 100)));
    expect(result.current.model.rows.size).toBe(1);

    // Act: a clean end is a dropped connection.
    await act(async () => streamAt(0).end());

    // Assert: empty and loading at once, and not re-opened before the delay.
    expect(result.current.loading).toBe(true);
    expect(result.current.model.rows.size).toBe(0);
    expect(result.current.model.title).toBe("");
    expect(calls.streams).toHaveLength(1);

    await act(async () => { await vi.advanceTimersByTimeAsync(TABLE_RECONNECT_DELAY_MS); });
    expect(calls.streams).toHaveLength(2);
    expect(calls.streams[1]!.streamId).not.toEqual(calls.streams[0]!.streamId);

    // The new session has never heard the window.
    calls.setTableWindow.mockClear();
    await act(async () => streamAt(1).push(baseline(100)));
    expect(calls.setTableWindow).toHaveBeenCalledTimes(1);
    expect(calls.setTableWindow.mock.calls[0]![0]).toMatchObject({ first: 40, count: 25, streamId: { value: calls.streams[1]!.streamId } });
  });

  it("counts an edit until its outcome arrives", async () => {
    // Arrange.
    const { result } = renderHook(() => useTableStream(["cities.adp"]));
    await act(async () => streamAt(0).push(baseline(1)));

    // Act: accepted, not written yet.
    let answer = "?";
    await act(async () => { answer = await result.current.edit({ kind: "setCell", rowId: "r1", columnId: "p1", values: ["Amsterdam"] }); });

    // Assert.
    expect(answer).toBe("");
    expect(result.current.pendingEdits).toBe(1);
    const sent = calls.edit.mock.calls[0]![0] as { editId: { value: Uint8Array }; gesture: unknown };
    expect(sent.gesture).toMatchObject({ kind: "setCell", rowId: "r1", columnId: "p1", values: ["Amsterdam"], viewId: "", index: 0 });

    // The write is confirmed.
    await act(async () => streamAt(0).push(outcome(sent.editId.value, true)));
    expect(result.current.pendingEdits).toBe(0);
    expect(result.current.refusal).toBe("");
  });

  it("keeps the reason when a write is refused", async () => {
    // Arrange.
    const { result } = renderHook(() => useTableStream(["cities.adp"]));
    await act(async () => streamAt(0).push(baseline(1)));
    await act(async () => { await result.current.edit({ kind: "setCell" }); });
    const sent = calls.edit.mock.calls[0]![0] as { editId: { value: Uint8Array } };

    // Act.
    await act(async () => streamAt(0).push(outcome(sent.editId.value, false, "The file changed on disk.")));

    // Assert.
    expect(result.current.pendingEdits).toBe(0);
    expect(result.current.refusal).toBe("The file changed on disk.");
  });

  it("does not count an edit the backend refuses outright", async () => {
    // Arrange.
    calls.edit.mockImplementation(async () => ({ error: "A number is expected." }));
    const { result } = renderHook(() => useTableStream(["cities.adp"]));
    await act(async () => streamAt(0).push(baseline(1)));

    // Act.
    let answer = "";
    await act(async () => { answer = await result.current.edit({ kind: "setCell", values: ["many"] }); });

    // Assert.
    expect(answer).toBe("A number is expected.");
    expect(result.current.pendingEdits).toBe(0);
    expect(result.current.refusal).toBe("A number is expected.");
  });

  it("refuses an edit while no table is open, without calling the backend", async () => {
    // Arrange: no baseline yet.
    const { result } = renderHook(() => useTableStream(["cities.adp"]));

    // Act.
    let answer = "";
    await act(async () => { answer = await result.current.edit({ kind: "setCell" }); });

    // Assert.
    expect(answer).toBe("The table is not open.");
    expect(calls.edit).not.toHaveBeenCalled();
  });

  it("stops at a permanent refusal, with the backend's sentence", async () => {
    // Arrange.
    vi.useFakeTimers();
    const { result } = renderHook(() => useTableStream(["missing.adp"]));

    // Act.
    await act(async () => streamAt(0).fail(new ConnectError("The document cannot be opened.", Code.FailedPrecondition)));
    await settle();
    await act(async () => { await vi.advanceTimersByTimeAsync(TABLE_RECONNECT_DELAY_MS * 4); });

    // Assert: said once, and not retried.
    expect(result.current.failure).toBe("The document cannot be opened.");
    expect(result.current.loading).toBe(false);
    expect(calls.streams).toHaveLength(1);
  });

  it("tells the backend which view the tab shows", async () => {
    // Arrange.
    const { result } = renderHook(() => useTableStream(["cities.adp"]));
    await act(async () => streamAt(0).push(baseline(1)));

    // Act.
    act(() => result.current.setView("v2"));

    // Assert.
    expect(calls.setTableView.mock.calls[0]![0]).toMatchObject({ viewId: "v2", streamId: { value: calls.streams[0]!.streamId } });
  });
});
