import { afterEach, describe, expect, it, vi } from "vitest";
import { StrictMode } from "react";
import { act, cleanup, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import type { DescService } from "@bufbuild/protobuf";
import { ContextMessageSchema } from "../../generated/context_pb";
import { WorkspaceMessageSchema } from "../../generated/workspace_pb";
import { ExplorerTreePanel } from "../panels/ExplorerTreePanel";
import { ContextConnectionProvider } from "./ContextConnectionProvider";
import { HIERARCHY_LOST_TEXT } from "./workspaceStreams";

/**
 * **The explorer's hierarchy feed outlives React's development remount.**
 *
 * `main.tsx` renders under `StrictMode`, which mounts every effect, cleans it up and mounts it
 * again. The provider's first `Watch` loop is aborted by that cleanup, and its stream rejects a
 * moment later - after the explorer's second mount has already subscribed to the shared hierarchy
 * feed. When the aborted loop still called `disconnect()` on the way out, it failed that live feed,
 * and every local debugging session opened on "Lost connection to the file hierarchy." while the
 * backend's stream was up and healthy. Production builds do not double-mount, so only `npm run dev`
 * showed it.
 *
 * **It was seen to fail**: with the `disconnect()` put back ahead of the abort check in
 * `ContextConnectionProvider`, the explorer shows the lost-connection sentence.
 */

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

/** A live server stream that, like the real transport, rejects once its signal aborts. */
async function* serverStream(name: string, signal: AbortSignal | undefined) {
  if (name.endsWith("WorkspaceService.Watch")) {
    yield create(WorkspaceMessageSchema, {
      message: { case: "context", value: create(ContextMessageSchema, { message: { case: "selection", value: {} } }) },
    });
  }
  await new Promise<void>((_resolve, reject) => {
    const abort = () => reject(new DOMException("The operation was aborted.", "AbortError"));
    if (signal?.aborted) {
      abort();
    }
    signal?.addEventListener("abort", abort);
  });
}

const projectId = new Uint8Array(16).fill(1);

async function settle() {
  for (let turn = 0; turn < 5; turn++) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}

describe("the explorer under StrictMode", () => {
  afterEach(() => {
    cleanup();
  });

  it("keeps its hierarchy feed when the provider's first stream is aborted by the remount", async () => {
    // Arrange & act.
    render(
      <StrictMode>
        <ContextConnectionProvider projectId={projectId}>
          <ExplorerTreePanel projectId={projectId} />
        </ContextConnectionProvider>
      </StrictMode>,
    );
    await settle();

    // Assert.
    expect(screen.queryByText(HIERARCHY_LOST_TEXT)).toBeNull();
  });
});
