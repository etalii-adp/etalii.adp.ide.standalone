import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { Code, ConnectError } from "@connectrpc/connect";

const open = vi.fn();
const updateView = vi.fn(async () => ({ error: "" }));
const moveElement = vi.fn(async () => ({ error: "" }));

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return {
    ...actual,
    createClient: () => ({ open, updateView, moveElement }),
  };
});

vi.mock("@client/auth/AuthContext", () => {
  // One identity for the transport, which is what AuthContext actually guarantees. A fresh
  // object per call would churn the effect that is under test here.
  const transport = {};
  return { useAuth: () => ({ transport }) };
});

const watchId = new Uint8Array(16);
vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  useContextConnection: () => ({ watchId }),
}));

// Imported after the mocks so the hook picks them up.
const { useDotNetDependencyGraphStream } = await import("./useDotNetDependencyGraphStream");

/** A stream that ends immediately and cleanly - the server closed it, nothing threw. */
function endingStream(): AsyncIterable<never> {
  return {
    [Symbol.asyncIterator]: () => ({
      next: () => Promise.resolve({ done: true as const, value: undefined as never }),
    }),
  };
}

/** A stream that throws as soon as it is read. */
function failingStream(error: Error): AsyncIterable<never> {
  return {
    [Symbol.asyncIterator]: () => ({
      next: () => Promise.reject(error),
    }),
  };
}

const projectId = new Uint8Array(16).fill(1);
const path = ["PipelineToolkit.adp"];

describe("useDotNetDependencyGraphStream reconnect policy", () => {
  beforeEach(() => {
    open.mockReset();
  });

  it("waits between re-opens when the server keeps closing the stream", async () => {
    // THE GUARD FOR THE HAND-ROLLED LOOP THIS HOOK USED TO CARRY. That loop re-opened a
    // cleanly-ended stream IMMEDIATELY, with no delay, so a backend that closes on every
    // connect had this client re-opening as fast as the event loop allowed. The shared
    // useDiagramStream waits RECONNECT_DELAY_MS between attempts for exactly that reason
    // (technical-debt-cleanup R3.4), and adopting it is what fixes this.
    //
    // The assertion is an UPPER bound on attempts in a fixed window, which is the only shape
    // that can catch a hot loop: a lower bound ("it reconnected") passes against both the
    // defect and the fix, and would have passed against the code this replaced.

    // Arrange.
    // A CEILING in the mock, not just in the assertion, and it is what makes this test
    // readable rather than fatal. An unthrottled loop re-opening a stream that ends
    // immediately never yields to the macrotask queue: timers stop firing, and the run does
    // not fail, it HANGS and the worker is killed. That is a true signal about the defect and
    // a useless one to debug. So after `ceiling` opens the mock answers with a permanent
    // error, which ends any correct loop and lets the assertion below report a number.
    const ceiling = 200;
    open.mockImplementation(() =>
      open.mock.calls.length > ceiling
        ? failingStream(new ConnectError("enough", Code.FailedPrecondition))
        : endingStream(),
    );

    // Act.
    renderHook(() => useDotNetDependencyGraphStream(projectId, path));

    // It must reconnect at all - otherwise this would pass against a hook that gave up.
    await waitFor(() => expect(open.mock.calls.length).toBeGreaterThanOrEqual(1), { timeout: 2000 });
    await new Promise((resolve) => setTimeout(resolve, 600));

    // Assert.
    // At 500ms apart, ~600ms of closing streams is one or two opens. The unthrottled loop
    // reaches the ceiling in a fraction of that.
    expect(
      open.mock.calls.length,
      `re-opened ${open.mock.calls.length} times in ~600ms; the loop is not backing off`,
    ).toBeLessThanOrEqual(4);
  });

  it("stops for good on a permanent answer, rather than retrying behind an empty canvas", async () => {
    // The pairing, and it is what keeps the test above honest: an upper bound on re-opens is
    // also satisfied by a hook that never retries. FailedPrecondition is the backend's "this
    // diagram cannot be opened here", so the loop must END - and `failed` must be reported so
    // the canvas can say so rather than showing an empty graph for ever.

    // Arrange.
    open.mockImplementation(() => failingStream(new ConnectError("cannot be opened", Code.FailedPrecondition)));

    // Act.
    const { result } = renderHook(() => useDotNetDependencyGraphStream(projectId, path));

    await waitFor(() => expect(result.current.failed).toBe(true), { timeout: 2000 });
    // Well past the back-off: if the loop were still running it would have tried again.
    await new Promise((resolve) => setTimeout(resolve, 700));

    // Assert.
    expect(open).toHaveBeenCalledTimes(1);
    expect(result.current.loading).toBe(false);
  });

  it("keeps retrying a transient error without ever reporting failed", async () => {
    // Unavailable is a backend restarting, not a verdict about this diagram. The loop must
    // keep going - and must not tell the canvas the diagram is unopenable while it does.

    // Arrange.
    open.mockImplementation(() => failingStream(new ConnectError("backend restarting", Code.Unavailable)));

    // Act.
    const { result } = renderHook(() => useDotNetDependencyGraphStream(projectId, path));

    // Assert.
    await waitFor(() => expect(open.mock.calls.length).toBeGreaterThanOrEqual(2), { timeout: 5000 });
    expect(result.current.failed).toBe(false);
  });
});
