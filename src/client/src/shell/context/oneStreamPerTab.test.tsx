import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import type { DescService } from "@bufbuild/protobuf";
import { ContextMessageSchema } from "../../generated/context_pb";
import { WorkspaceMessageSchema } from "../../generated/workspace_pb";
import { useDiagramStream } from "../../diagrams/useDiagramStream";
import { ExplorerTreePanel } from "../panels/ExplorerTreePanel";
import { ContextConnectionProvider } from "./ContextConnectionProvider";

/**
 * **A tab holds one long-lived server stream, whatever it has open** (two-tab-connection-wedge
 * Requirement 3.1).
 *
 * Over cleartext HTTP/1.1 every live stream holds one of the browser's six connections per origin,
 * and a tab used to hold three - the context's `Watch`, the explorer's `WatchHierarchy` and the
 * active document's `Open` - so a second tab exhausted the pool and the whole origin stopped
 * answering. Serving over TLS is the fix; this is the robustness beside it, and it only lasts while
 * nothing adds a second stream. Every stream anyone adds divides the number of tabs again.
 *
 * **It counts what the transport is asked for, not what the code looks like.** The mocked client
 * reads each method's kind from the real service descriptor it is built with, so any server-streaming
 * call from any service counts - including one added later, under a name this file has never heard
 * of - and the population is *live* streams, started and not yet ended, read at its peak.
 *
 * **It was seen to fail.** With `ExplorerTreePanel` put back on `hierarchyClient.watchHierarchy`, or
 * `useDiagramStream` back on `DiagramService.Open`, the peak is 2 - recorded in the implementation
 * log for the task.
 */

const streams = vi.hoisted(() => ({ live: 0, peak: 0, started: [] as string[], openDiagram: 0 }));

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return {
    ...actual,
    createClient: (service: DescService) =>
      new Proxy(
        {},
        {
          get: (_target, name) => {
            const method = service.methods.find((candidate) => candidate.localName === name);
            if (method?.methodKind === "server_streaming") {
              return (_request: unknown, options?: { signal?: AbortSignal }) => serverStream(`${service.typeName}.${method.name}`, options?.signal);
            }
            if (name === "openDiagram") {
              streams.openDiagram += 1;
            }
            return () => Promise.resolve({ accepted: true, error: "", items: [], properties: [] });
          },
        },
      ),
  };
});

vi.mock("../../auth/AuthContext", () => {
  const transport = {};
  return { useAuth: () => ({ transport }) };
});

/**
 * A live server stream: counted while it runs. Its first message is the context's baseline, which
 * is what tells the provider its connection is registered - so a diagram really is opened on it.
 */
async function* serverStream(name: string, signal: AbortSignal | undefined) {
  streams.started.push(name);
  streams.live += 1;
  streams.peak = Math.max(streams.peak, streams.live);
  try {
    if (name.endsWith("WorkspaceService.Watch")) {
      yield create(WorkspaceMessageSchema, {
        message: { case: "context", value: create(ContextMessageSchema, { message: { case: "selection", value: {} } }) },
      });
    }
    await new Promise<void>((resolve) => {
      if (signal?.aborted) {
        resolve();
      }
      signal?.addEventListener("abort", () => resolve());
    });
  } finally {
    streams.live -= 1;
  }
}

const projectId = new Uint8Array(16).fill(1);
const path = ["one.adp"] as const;
const emptyModel = { deltas: 0 };
const fold = (model: { deltas: number }) => ({ deltas: model.deltas + 1 });

/** An open document, as a canvas holds one: the shared stream hook, nothing else. */
function OpenDocument() {
  useDiagramStream(projectId, path, emptyModel, fold);
  return null;
}

async function settle() {
  for (let turn = 0; turn < 5; turn++) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}

describe("a tab's long-lived streams", () => {
  beforeEach(() => {
    streams.live = 0;
    streams.peak = 0;
    streams.started = [];
    streams.openDiagram = 0;
  });

  afterEach(() => {
    cleanup();
  });

  it("is one, with the explorer showing the hierarchy and a document open", async () => {
    // Arrange & act: everything that used to open a stream of its own, mounted at once.
    render(
      <ContextConnectionProvider projectId={projectId}>
        <ExplorerTreePanel projectId={projectId} />
        <OpenDocument />
      </ContextConnectionProvider>,
    );
    await settle();

    // Assert: the invariant.
    expect(
      streams.peak,
      `a tab held ${streams.peak} long-lived streams at once (${streams.started.join(", ")}); over HTTP/1.1 each ` +
        "costs one of the browser's six connections per origin. Push it on WorkspaceService.Watch instead.",
    ).toBe(1);

    // And the floor, so a count of one cannot mean "nothing ran": the document really was opened -
    // on the tab's stream, through the unary call - and that stream is the one that started.
    expect(streams.openDiagram, "the document was never opened, so the count above measures nothing").toBe(1);
    expect(streams.started).toEqual(["etalii.adp.WorkspaceService.Watch"]);
  });

  it("releases it when the tab's shell unmounts", async () => {
    // Arrange.
    const { unmount } = render(
      <ContextConnectionProvider projectId={projectId}>
        <OpenDocument />
      </ContextConnectionProvider>,
    );
    await settle();
    expect(streams.live).toBe(1);

    // Act.
    unmount();
    await settle();

    // Assert.
    expect(streams.live).toBe(0);
  });
});
