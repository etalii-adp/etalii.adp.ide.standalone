import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { Code, ConnectError } from "@connectrpc/connect";

const open = vi.fn();
const moveElement = vi.fn(async () => ({ error: "" }));

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return {
    ...actual,
    createClient: () => ({ open, moveElement }),
  };
});

vi.mock("@client/auth/AuthContext", () => ({
  useAuth: () => ({ transport: {} }),
}));

// One stable identity: the hook keys its effect on the watchId, so a fresh array per render
// would churn the effect and reset the very state under test.
const watchId = new Uint8Array(16);
vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  useContextConnection: () => ({ watchId }),
}));

// Imported after the mocks so the hook picks them up.
const { useWardleyStream } = await import("./useWardleyStream");

/** A delta the model folds as a no-op, so the hook's own state is all that moves. */
const noopDelta = { action: { case: undefined } } as never;

/** Yields the given deltas, then either throws or ends cleanly. */
function streamOf(deltas: readonly never[], end?: Error): AsyncIterable<never> {
  let index = 0;
  return {
    [Symbol.asyncIterator]: () => ({
      next: () => {
        if (index < deltas.length) {
          return Promise.resolve({ value: deltas[index++], done: false });
        }
        return end ? Promise.reject(end) : Promise.resolve({ value: undefined as never, done: true });
      },
    }),
  };
}

/** A stream that never produces anything - the reconnected loop parks here. */
function pendingStream(): AsyncIterable<never> {
  return { [Symbol.asyncIterator]: () => ({ next: () => new Promise<never>(() => {}) }) };
}

/** Drains the microtask chain so the hook's async loop runs as far as its next timer. */
async function flush(): Promise<void> {
  await act(async () => {
    for (let i = 0; i < 20; i++) {
      await Promise.resolve();
    }
  });
}

const projectId = new Uint8Array(16).fill(1);

/**
 * Verifies the hook's reconnect timing now that it delegates to `useDiagramStream`: the two
 * assertions R12 pinned against the pre-extraction drift, flipped under R3.3 to the canonical
 * shape `usePipelineStream` supplied (technical-debt-cleanup R3.3, R12.2).
 */
describe("useWardleyStream reconnect timing", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    open.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("returns to loading before retrying a transient error, after the shared backoff", async () => {
    // Arrange: a delta arrives (loading ends), then the connection drops with a retryable code.
    open
      .mockImplementationOnce(() => streamOf([noopDelta], new ConnectError("backend restarting", Code.Unavailable)))
      .mockImplementation(() => pendingStream());

    // Act: run the loop up to its 500ms retry sleep.
    const { result } = renderHook(() => useWardleyStream(projectId, ["maps", "landscape.adp"]));
    await flush();

    // Assert: mid-backoff the canvas is told it is loading again - reconnecting, not empty.
    expect(open).toHaveBeenCalledTimes(1);
    expect(result.current.loading).toBe(true);
    expect(result.current.failed).toBe(false);

    // And the retry itself happens only once the delay elapses.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });
    expect(open).toHaveBeenCalledTimes(2);
  });

  it("applies the same backoff to a cleanly-ended stream as to an error", async () => {
    // Arrange: the server closes the stream without an error.
    open
      .mockImplementationOnce(() => streamOf([noopDelta]))
      .mockImplementation(() => pendingStream());

    // Act: drain microtasks only - no timer is advanced yet.
    renderHook(() => useWardleyStream(projectId, ["maps", "landscape.adp"]));
    await flush();

    // Assert: no immediate reopen - a server that keeps closing the stream cannot hot-loop
    // this client.
    expect(open).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(open).toHaveBeenCalledTimes(2);
  });
});

/**
 * The hook's surface rather than its streaming behaviour. This used to pin the *absence* of a
 * report - the hook's doc comment argued a bounded map had nothing to gain from one - and the
 * assertion is inverted rather than deleted, because the surface is what the canvas destructures
 * and a silently missing `reportView` would leave the loop wired to `undefined`.
 */
describe("useWardleyStream surface", () => {
  it("exposes reportView, so the canvas has something to report through", () => {
    // Arrange.
    open.mockReturnValue(pendingStream());

    // Act.
    const { result } = renderHook(() => useWardleyStream(new Uint8Array([1]), ["map.owm"]));

    // Assert.
    expect(typeof result.current.reportView).toBe("function");
  });
});
