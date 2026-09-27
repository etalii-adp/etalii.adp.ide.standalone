import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createElement } from "react";
import { act, renderHook } from "@testing-library/react";
import { Code, ConnectError } from "@connectrpc/connect";
import { CanvasStatusContext, type CanvasStatusReporter, type CanvasStreamState } from "@client/canvas/library/surface/canvasStatus";
import { useDiagramStream } from "./useDiagramStream";

/**
 * Inside a canvas, the stream reports what the canvas is doing, and the library's frame says it
 * (client-centralization Requirement 2.3). Before this, each module returned a status block of its
 * own INSTEAD of its canvas, so the library was not even mounted while there was a status to show.
 *
 * <b>Reconnecting is told apart from opening by whether the stream ever had a model</b> - the hook
 * already returned to loading before each reconnect; what was missing was anybody saying so.
 */

type Step = { delta: object } | { end: true } | { fail: Error } | { park: true };
let script: Step[][] = [];

/** Each call to `open` plays the next scripted stream: yields its deltas, then ends, fails or parks. */
const open = vi.fn(() => {
  const steps = script.shift() ?? [{ park: true }];
  return {
    [Symbol.asyncIterator]: () => {
      let index = 0;
      return {
        next: async () => {
          const step = steps[index++];
          if (step === undefined || "end" in step) {
            return { done: true, value: undefined };
          }
          if ("fail" in step) {
            throw step.fail;
          }
          if ("park" in step) {
            return new Promise<never>(() => {});
          }
          return { done: false, value: step.delta };
        },
      };
    },
  };
});

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return { ...actual, createClient: () => ({ open, moveElement: vi.fn(), updateView: vi.fn() }) };
});

vi.mock("@client/auth/AuthContext", () => {
  const transport = {};
  return { useAuth: () => ({ transport }) };
});

const watchId = new Uint8Array(16);
// The deltas ride the tab's one stream now; the stream the hook is handed is still `open`'s. One
// object, as the provider's is memoised: the hook keys its effect on it, so a fresh one per render
// would re-open the stream on every render.
const workspaceStreams = { openDiagramStream: () => open() };
vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  useContextConnection: () => ({ watchId }),
  useWorkspaceStreams: () => workspaceStreams,
}));

let reported: CanvasStreamState[] = [];
let forgotten = 0;
const reporter: CanvasStatusReporter = {
  report: (_stream, state) => reported.push(state),
  forget: () => forgotten++,
};

// Stable identities, as a real module's are: the hook keys its effect on projectId, so a fresh array
// per render re-opens the stream on every render - which is how this file first hung its worker.
const projectId = new Uint8Array([7]);
const path = ["example.adp"] as const;
const emptyModel = { deltas: 0 };
const fold = (model: { deltas: number }) => ({ deltas: model.deltas + 1 });

function mountStream() {
  return renderHook(() => useDiagramStream(projectId, path, emptyModel, fold), {
    wrapper: ({ children }) => createElement(CanvasStatusContext.Provider, { value: reporter }, children),
  });
}

const last = () => reported.at(-1);

describe("useDiagramStream's status, inside a canvas", () => {
  beforeEach(() => {
    reported = [];
    forgotten = 0;
    script = [];
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it("is opening until the first delta arrives, then open", async () => {
    // Arrange.
    script = [[{ delta: {} }, { park: true }]];

    // Act.
    mountStream();
    expect(reported[0]).toEqual({ kind: "opening" });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    // Assert.
    expect(last()).toEqual({ kind: "open" });
  });

  it("is reconnecting, not opening, when a stream that had a model drops", async () => {
    // Arrange: one delta, then the stream ends - the connection dropped.
    script = [[{ delta: {} }, { end: true }], [{ park: true }]];

    // Act.
    mountStream();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    // Assert: the delta and the drop land in one batch here, the case a ref could not render.
    expect(last()).toEqual({ kind: "reconnecting" });
  });

  it("is unavailable, in the backend's own words, when the backend answers permanently", async () => {
    // Arrange.
    script = [[{ fail: new ConnectError("'x' diagrams cannot be opened yet.", Code.Unimplemented) }]];

    // Act.
    mountStream();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    // Assert.
    expect(last()).toEqual({ kind: "unavailable", reason: "'x' diagrams cannot be opened yet." });
  });

  it("stays opening through a transient fault before any model, rather than claiming a reconnect", async () => {
    // Arrange: the first open fails transiently; nothing was ever shown.
    script = [[{ fail: new Error("the transport dropped") }], [{ park: true }]];

    // Act.
    mountStream();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    // Assert.
    expect(reported.every((state) => state.kind === "opening")).toBe(true);
  });

  it("forgets its state when the canvas unmounts it", () => {
    // Arrange.
    const { unmount } = mountStream();

    // Act.
    unmount();

    // Assert.
    expect(forgotten).toBe(1);
  });
});
