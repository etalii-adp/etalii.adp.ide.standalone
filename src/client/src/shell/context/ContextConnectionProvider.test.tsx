import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import {
  ContextMessageSchema,
  ContextSelectionAction,
  ContextSelectionSource,
  type ContextMessage,
} from "../../generated/context_pb";
import {
  ContextConnectionProvider,
  SELECT_COALESCE_MS,
  innermostAction,
  innermostKey,
  selectionFor,
  useContextConnection,
  useContextPrompt,
  useContextSelection,
} from "./ContextConnectionProvider";

/** A server stream the test feeds by hand and can end (to exercise a reconnect). */
class FakeStream implements AsyncIterable<ContextMessage> {
  private queue: ContextMessage[] = [];
  private waiters: Array<(result: IteratorResult<ContextMessage>) => void> = [];
  private ended = false;

  push(message: ContextMessage) {
    const waiter = this.waiters.shift();
    if (waiter) {
      waiter({ value: message, done: false });
    } else {
      this.queue.push(message);
    }
  }

  end() {
    this.ended = true;
    for (const waiter of this.waiters.splice(0)) {
      waiter({ value: undefined as unknown as ContextMessage, done: true });
    }
  }

  [Symbol.asyncIterator](): AsyncIterator<ContextMessage> {
    return {
      next: () => {
        const queued = this.queue.shift();
        if (queued) {
          return Promise.resolve({ value: queued, done: false });
        }
        if (this.ended) {
          return Promise.resolve({ value: undefined as unknown as ContextMessage, done: true });
        }
        return new Promise((resolve) => this.waiters.push(resolve));
      },
    };
  }
}

const streams: FakeStream[] = [];
const select = vi.fn(async () => ({ error: "" }));
const executeAction = vi.fn(async () => ({ accepted: true, error: "" }));
const proposeInput = vi.fn(async () => ({ revision: 1, valid: true, reason: "" }));
const submitInteraction = vi.fn(async () => ({ completed: true, error: "" }));
const cancelInteraction = vi.fn(async () => ({}));
const watch = vi.fn(() => {
  const stream = new FakeStream();
  streams.push(stream);
  return stream;
});

vi.mock("../../auth/AuthContext", () => ({
  useAuth: () => ({ transport: {} }),
}));

vi.mock("@connectrpc/connect", () => ({
  createClient: () => ({ select, watch, executeAction, proposeInput, submitInteraction, cancelInteraction }),
}));

const projectId = new Uint8Array(16).fill(1);
const entryA = new Uint8Array(16).fill(7);
const entryB = new Uint8Array(16).fill(9);

function selectionMessage(entryId: Uint8Array | null, options: { transient?: boolean; path?: string[] } = {}): ContextMessage {
  return create(ContextMessageSchema, {
    message: {
      case: "selection",
      value: {
        selection: entryId ? selectionFor(ContextSelectionSource.EXPLORER, entryId, options.path ?? ["a.txt"], { case: "none", value: {} }) : undefined,
        levels: entryId ? [{ detail: { case: "entry", value: { kind: 1, available: true } } }] : [],
        actions: entryId ? [{ actions: [{ id: "hierarchy.rename", label: "Rename", icon: "", available: true }] }] : [],
        transient: options.transient ?? false,
      },
    },
  });
}

function promptMessage(): ContextMessage {
  return create(ContextMessageSchema, {
    message: {
      case: "prompt",
      value: { interactionId: { value: new Uint8Array(16).fill(3) }, prompt: { case: "inputDialog", value: { title: "Rename", initialValue: "a.txt" } } },
    },
  });
}

/** Shows what the hooks hand out, and exposes the connection for the test to drive. */
let connection: ReturnType<typeof useContextConnection> | undefined;
let promptValue: ReturnType<typeof useContextPrompt> | undefined;

function Probe() {
  connection = useContextConnection();
  promptValue = useContextPrompt();
  const { selection, actions, preview, connected } = useContextSelection();
  return (
    <div>
      <span data-testid="selection">{innermostKey(selection) ?? "none"}</span>
      <span data-testid="actions">{actions.flatMap((g) => g.actions.map((a) => a.id)).join(",")}</span>
      <span data-testid="preview">{innermostKey(preview?.selection) ?? "none"}</span>
      <span data-testid="connected">{String(connected)}</span>
      <span data-testid="prompt">{promptValue.prompt?.prompt.case ?? "none"}</span>
    </div>
  );
}

function renderProvider() {
  return render(
    <ContextConnectionProvider projectId={projectId}>
      <Probe />
    </ContextConnectionProvider>,
  );
}

async function flush() {
  await act(async () => {
    await Promise.resolve();
  });
}

describe("ContextConnectionProvider", () => {
  beforeEach(() => {
    streams.length = 0;
    select.mockClear();
    executeAction.mockClear();
    watch.mockClear();
    connection = undefined;
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("opens exactly one Watch stream for any number of consumers", async () => {
    render(
      <ContextConnectionProvider projectId={projectId}>
        <Probe />
        <Probe />
      </ContextConnectionProvider>,
    );
    await flush();

    expect(watch).toHaveBeenCalledTimes(1);
  });

  it("applies a pushed selection with its actions and marks the connection live", async () => {
    renderProvider();
    await flush();
    streams[0]!.push(selectionMessage(null));
    await flush();
    expect(screen.getByTestId("connected").textContent).toBe("true");

    streams[0]!.push(selectionMessage(entryA));

    await waitFor(() => expect(screen.getByTestId("selection").textContent).toBe(innermostKey(selectionFor(0, entryA, [], { case: "none", value: {} }))));
    expect(screen.getByTestId("actions").textContent).toBe("hierarchy.rename");
  });

  it("keeps a transient message as a preview without replacing the current selection", async () => {
    renderProvider();
    await flush();
    streams[0]!.push(selectionMessage(entryA));
    await waitFor(() => expect(screen.getByTestId("selection").textContent).not.toBe("none"));
    const current = screen.getByTestId("selection").textContent;

    streams[0]!.push(selectionMessage(entryB, { transient: true }));

    await waitFor(() => expect(screen.getByTestId("preview").textContent).not.toBe("none"));
    expect(screen.getByTestId("selection").textContent).toBe(current);

    streams[0]!.push(selectionMessage(null));
    await waitFor(() => expect(screen.getByTestId("preview").textContent).toBe("none"));
  });

  it("surfaces a pushed prompt and clears it on a completed submit", async () => {
    renderProvider();
    await flush();
    streams[0]!.push(promptMessage());
    await waitFor(() => expect(screen.getByTestId("prompt").textContent).toBe("inputDialog"));

    await act(async () => {
      await promptValue!.onSubmit("b.txt");
    });

    expect(submitInteraction).toHaveBeenCalled();
    expect(screen.getByTestId("prompt").textContent).toBe("none");
  });

  it("coalesces plain selections and flushes a gesture at once", async () => {
    vi.useFakeTimers();
    renderProvider();
    const plain = selectionFor(ContextSelectionSource.EXPLORER, entryA, ["a.txt"], { case: "none", value: {} });
    const gesture = selectionFor(ContextSelectionSource.EXPLORER, entryB, ["b.txt"], { case: "action", value: ContextSelectionAction.CONTEXT_MENU });

    act(() => {
      connection!.select(plain);
      connection!.select(plain);
    });
    expect(select).not.toHaveBeenCalled();
    act(() => {
      vi.advanceTimersByTime(SELECT_COALESCE_MS);
    });
    expect(select).toHaveBeenCalledTimes(1);

    act(() => {
      connection!.select(plain);
      connection!.select(gesture);
    });
    expect(select).toHaveBeenCalledTimes(2);
    const sent = (select.mock.calls[1] as unknown as [{ selection?: { id?: { source: { value: { value: Uint8Array } } } } }])[0];
    expect(sent.selection?.id?.source.value.value).toEqual(entryB);
  });

  it("sends executeAction without a scope and only with the source it was given", async () => {
    renderProvider();
    await flush();

    await act(async () => {
      await connection!.executeAction("hierarchy.rename");
    });

    const request = (executeAction.mock.calls[0] as unknown as [Record<string, unknown>])[0];
    expect(request).not.toHaveProperty("scope");
    expect(request.source).toBeUndefined();
    expect(request.trigger).toEqual({ case: "actionId", value: "hierarchy.rename" });
  });

  it("re-sends its last selection when a reconnected stream reports nothing selected", async () => {
    vi.useFakeTimers();
    renderProvider();
    await flush();
    const plain = selectionFor(ContextSelectionSource.EXPLORER, entryA, ["a.txt"], { case: "none", value: {} });
    act(() => {
      connection!.select(plain);
      vi.advanceTimersByTime(SELECT_COALESCE_MS);
    });
    expect(select).toHaveBeenCalledTimes(1);

    streams[0]!.end();
    await flush();
    await act(async () => {
      vi.advanceTimersByTime(500);
      await Promise.resolve();
    });
    await flush();
    expect(watch).toHaveBeenCalledTimes(2);

    streams[1]!.push(selectionMessage(null));
    await flush();

    expect(select).toHaveBeenCalledTimes(2);
  });
});

describe("innermost helpers", () => {
  it("walk a chain to its innermost level", () => {
    const inner = selectionFor(ContextSelectionSource.DIAGRAM_CANVAS, entryB, ["node"], { case: "action", value: ContextSelectionAction.ACTIVATE });
    const outer = selectionFor(ContextSelectionSource.EXPLORER, entryA, ["a.mm"], { case: "child", value: inner });

    expect(innermostKey(outer)).toBe(innermostKey(inner));
    expect(innermostAction(outer)).toBe(ContextSelectionAction.ACTIVATE);
    expect(innermostKey(null)).toBeUndefined();
  });
});
