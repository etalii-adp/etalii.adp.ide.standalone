import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { Code, ConnectError } from "@connectrpc/connect";

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
  useContextConnection: () => ({ watchId }),
}));

// Imported after the mocks so the hook picks them up.
const { useMindmapStream } = await import("./useMindmapStream");

/** A stream that throws the given error as soon as it is read. */
function failingStream(error: Error): AsyncIterable<never> {
  return {
    [Symbol.asyncIterator]: () => ({
      next: () => Promise.reject(error),
    }),
  };
}

const projectId = new Uint8Array(16).fill(1);

describe("useMindmapStream failure policy", () => {
  beforeEach(() => {
    open.mockReset();
  });

  it("stops for good on a permanent answer, reporting failed", async () => {
    // Arrange.
    // FailedPrecondition is the backend's "this diagram cannot be opened": deleted, moved,
    // or unroutable. Retrying would spin forever behind the tab (Requirement 5.1).
    open.mockImplementation(() => failingStream(new ConnectError("cannot be opened", Code.FailedPrecondition)));

    const { result } = renderHook(() => useMindmapStream(projectId, ["docs", "gone.adp"]));

    // Act and assert, step by step.
    await waitFor(() => expect(result.current.failed).toBe(true));
    expect(result.current.loading).toBe(false);

    // Well past the 500ms retry back-off: the loop has genuinely ended.
    await new Promise((resolve) => setTimeout(resolve, 700));
    expect(open).toHaveBeenCalledTimes(1);
  }, 10000);

  it("keeps reconnecting on a transient error, never reporting failed", async () => {
    // Arrange.
    open.mockImplementation(() => failingStream(new ConnectError("backend restarting", Code.Unavailable)));

    // Act.
    const { result } = renderHook(() => useMindmapStream(projectId, ["docs", "map.adp"]));

    // Assert.
    await waitFor(() => expect(open.mock.calls.length).toBeGreaterThanOrEqual(2), { timeout: 5000 });
    expect(result.current.failed).toBe(false);
  }, 10000);
});
