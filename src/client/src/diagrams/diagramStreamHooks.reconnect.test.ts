import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { RECONNECT_DELAY_MS } from "./useDiagramStream";

/**
 * A cleanly-ended stream behaves exactly like a dropped one, in every module that opens a
 * diagram (technical-debt-cleanup R3.4): it waits out {@link RECONNECT_DELAY_MS} before
 * re-opening, so a server that keeps closing the stream cannot hot-loop the client, and it
 * starts again from the empty model, so an element deleted while disconnected cannot survive
 * on the canvas.
 *
 * Nine modules once hand-rolled this loop and had both defects - the delay and the reset lived
 * only in the `catch` branch, so a clean end re-opened at once and kept the stale model. They
 * now delegate to `useDiagramStream`, and this table is what keeps them there by behaviour; the
 * source-level half is `diagramStreamOpensOnlyInHook.test.ts`.
 *
 * Each module's model is replaced by two sentinels. With the real models a delta the test can
 * build generically folds to a no-op, and then "the model is empty after a clean end" would
 * hold whether or not anything reset it. A sentinel `applyDelta` makes the fold observable.
 */

const { sentinel, sentinelModel } = vi.hoisted(() => {
  const sentinel = {
    empty: { sentinel: "empty" },
    touched: { sentinel: "touched" },
    applied: 0,
  };
  // Inside vi.hoisted because vi.mock factories run before any top-level declaration.
  const sentinelModel =
    (applyName: string, emptyName: string) => async (importOriginal: () => Promise<Record<string, unknown>>) => ({
      ...(await importOriginal()),
      [applyName]: () => {
        sentinel.applied++;
        return sentinel.touched;
      },
      [emptyName]: sentinel.empty,
    });
  return { sentinel, sentinelModel };
});

const open = vi.fn();

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return { ...actual, createClient: () => ({ open, moveElement: vi.fn(), updateView: vi.fn() }) };
});

vi.mock("@client/auth/AuthContext", () => {
  // One identity, as AuthContext guarantees: a fresh transport per render would churn the
  // effect it keys and reset the very state under test.
  const transport = {};
  return { useAuth: () => ({ transport }) };
});

const watchId = new Uint8Array(16);
vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  useContextConnection: () => ({ watchId }),
}));

vi.mock("../../../diagrams/databricks/client/databricksModel", sentinelModel("applyDelta", "emptyModel"));
vi.mock("../../../diagrams/dependency-graph/client/dependencyGraphModel", sentinelModel("applyDelta", "emptyModel"));
vi.mock("../../../diagrams/helm-charts/client/helmModel", sentinelModel("applyDelta", "emptyModel"));
vi.mock("../../../diagrams/rdf/client/owlModel", sentinelModel("applyOwlDelta", "emptyOwlModel"));
vi.mock("../../../diagrams/rdf/client/rdfModel", sentinelModel("applyDelta", "emptyModel"));
vi.mock("../../../diagrams/rdf/client/shaclModel", sentinelModel("applyShaclDelta", "emptyShaclModel"));
vi.mock("../../../diagrams/rdf/client/skosModel", sentinelModel("applySkosDelta", "emptySkosModel"));
vi.mock("../../../diagrams/sparql/client/sparqlModel", sentinelModel("applyDelta", "emptyModel"));
vi.mock("../../../diagrams/timeline/client/timelineModel", sentinelModel("applyDelta", "emptyModel"));

type StreamHook = (projectId: Uint8Array, path: readonly string[]) => { model: unknown; loading: boolean };

// Imported after the mocks so each hook picks them up.
const hooks: [string, StreamHook][] = [
  ["databricks", (await import("../../../diagrams/databricks/client/useDatabricksStream")).useDatabricksStream],
  ["dependency-graph", (await import("../../../diagrams/dependency-graph/client/useDependencyGraphStream")).useDependencyGraphStream],
  ["helm-charts", (await import("../../../diagrams/helm-charts/client/useHelmStream")).useHelmStream],
  ["owl", (await import("../../../diagrams/rdf/client/useOwlStream")).useOwlStream],
  ["rdf", (await import("../../../diagrams/rdf/client/useRdfStream")).useRdfStream],
  ["shacl", (await import("../../../diagrams/rdf/client/useShaclStream")).useShaclStream],
  ["skos", (await import("../../../diagrams/rdf/client/useSkosStream")).useSkosStream],
  ["sparql", (await import("../../../diagrams/sparql/client/useSparqlStream")).useSparqlStream],
  ["timeline", (await import("../../../diagrams/timeline/client/useTimelineStream")).useTimelineStream],
];

const delta = { action: { case: undefined } } as never;

/** Yields one delta, then ends cleanly - the server closing the stream without an error. */
function cleanlyEndingStream(): AsyncIterable<never> {
  let sent = false;
  return {
    [Symbol.asyncIterator]: () => ({
      next: () => {
        if (!sent) {
          sent = true;
          return Promise.resolve({ value: delta, done: false });
        }
        return Promise.resolve({ value: undefined as never, done: true });
      },
    }),
  };
}

/** A stream that never produces anything - the re-opened loop parks here. */
function pendingStream(): AsyncIterable<never> {
  return { [Symbol.asyncIterator]: () => ({ next: () => new Promise<never>(() => {}) }) };
}

/** Drains the microtask chain so the loop runs as far as its next timer. */
async function flush(): Promise<void> {
  await act(async () => {
    for (let i = 0; i < 20; i++) {
      await Promise.resolve();
    }
  });
}

const projectId = new Uint8Array(16).fill(1);

describe.each(hooks)("%s stream: a clean end", (_name, useStream) => {
  beforeEach(() => {
    vi.useFakeTimers();
    open.mockReset();
    sentinel.applied = 0;
    open.mockImplementationOnce(() => cleanlyEndingStream()).mockImplementation(() => pendingStream());
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("waits out the reconnect delay before re-opening", async () => {
    // Act: let the stream deliver and end, advancing no timer.
    renderHook(() => useStream(projectId, ["diagrams", "example"]));
    await flush();

    // Assert: no immediate re-open - the hot loop this guards against.
    expect(open).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(RECONNECT_DELAY_MS);
    });
    expect(open).toHaveBeenCalledTimes(2);
  });

  it("starts again from the empty model", async () => {
    // Act.
    const { result } = renderHook(() => useStream(projectId, ["diagrams", "example"]));
    await flush();

    // Assert: the delta really was folded - so an empty model below is a reset, not a stream
    // that never delivered - and what the canvas holds now is the empty model, shown as
    // reconnecting rather than as a stale document.
    expect(sentinel.applied).toBeGreaterThan(0);
    expect(result.current.model).toBe(sentinel.empty);
    expect(result.current.loading).toBe(true);
  });
});
