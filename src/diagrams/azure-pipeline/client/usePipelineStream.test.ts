import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { Code, ConnectError } from "@connectrpc/connect";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";

const open = vi.fn();
const updateView = vi.fn(async () => ({ error: "" }));

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return {
    ...actual,
    createClient: () => ({ open, updateView }),
  };
});

vi.mock("@client/auth/AuthContext", () => {
  // One identity for the transport, which is what AuthContext actually guarantees: it memoises
  // the transport on a `[]`-stable callback and reads the token through a ref, so it is built
  // once. A fresh object per call would be a mock making a promise the real thing does not,
  // and a client memoised on it would then be rebuilt - churning the effect it keys.
  const transport = {};
  return { useAuth: () => ({ transport }) };
});

// One stable identity: the hook keys its effect on the watchId, so a fresh array per render
// would churn the effect and reset the very state under test.
const watchId = new Uint8Array(16);
vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  useContextConnection: () => connection,
}));

const connection = fakeContextConnection({ watchId });

// Imported after the mocks so the hook picks them up.
const { usePipelineStream } = await import("./usePipelineStream");

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
 * Characterises the shape design.md's R3 section chooses as canonical - the OPPOSITE of what
 * `useC4Stream`/`useWardleyStream`'s tests pin: loading returns to true before a retry, and a
 * clean stream end waits out the same backoff as a thrown error. This test should not need to
 * change when R3's shared hook lands (technical-debt-cleanup R12.2).
 */
describe("usePipelineStream reconnect timing", () => {
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
    const { result } = renderHook(() => usePipelineStream(projectId, ["ci", "build.adp"]));
    await flush();

    // Assert: mid-backoff the canvas is told it is loading again - reconnecting, not empty.
    expect(open).toHaveBeenCalledTimes(1);
    expect(result.current.loading).toBe(true);
    expect(result.current.failed).toBe(false);

    // And the retry waits out the delay rather than spinning.
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
    const { result } = renderHook(() => usePipelineStream(projectId, ["ci", "build.adp"]));
    await flush();

    // Assert: no immediate reopen - a server that keeps closing the stream cannot hot-loop
    // this client - and the wait is announced as loading.
    expect(open).toHaveBeenCalledTimes(1);
    expect(result.current.loading).toBe(true);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });
    expect(open).toHaveBeenCalledTimes(2);
  });
});
