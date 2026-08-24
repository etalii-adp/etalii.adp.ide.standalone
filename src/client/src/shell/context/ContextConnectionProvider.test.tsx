import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import {
  ContextMessageSchema,
  ContextSelectionAction,
  ContextSelectionSource,
  ProblemSetState,
  ProblemSeverity,
  type ContextMessage,
} from "../../generated/context_pb";
import {
  ContextConnectionProvider,
  NONE_DETAIL,
  PROBLEMS_SOURCE,
  SELECT_COALESCE_MS,
  innermostAction,
  innermostKey,
  selectionFor,
  useContextConnection,
  useContextPrompt,
  useContextProblems,
  useContextSelection,
  useProjectActions,
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
        selection: entryId ? selectionFor(ContextSelectionSource.EXPLORER, entryId, options.path ?? ["a.txt"], NONE_DETAIL) : undefined,
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

    await waitFor(() => expect(screen.getByTestId("selection").textContent).toBe(innermostKey(selectionFor(0, entryA, [], NONE_DETAIL))));
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
    const plain = selectionFor(ContextSelectionSource.EXPLORER, entryA, ["a.txt"], NONE_DETAIL);
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
    const plain = selectionFor(ContextSelectionSource.EXPLORER, entryA, ["a.txt"], NONE_DETAIL);
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

function projectActionsMessage(undoAvailable: boolean): ContextMessage {
  return create(ContextMessageSchema, {
    message: {
      case: "projectActions",
      value: {
        actions: [
          {
            actions: [
              { id: "history.undo", label: "Undo", icon: "mdi-undo", available: undoAvailable, unavailableReason: undoAvailable ? "" : "There is nothing to undo." },
              { id: "history.redo", label: "Redo", icon: "mdi-redo", available: false, unavailableReason: "There is nothing to redo." },
            ],
          },
        ],
      },
    },
  });
}

describe("ContextConnectionProvider project actions", () => {
  beforeEach(() => {
    streams.length = 0;
    watch.mockClear();
  });

  it("routes a projectActions message to useProjectActions without touching the selection", async () => {
    // A selection consumer whose render count the test can watch: a project-actions push must
    // not re-render it, because project actions are their own state (Deviation 1).
    let selectionRenders = 0;
    let projectActionsSeen: string[] = [];

    function SelectionOnly() {
      selectionRenders++;
      const { selection } = useContextSelection();
      return <span data-testid="sel">{innermostKey(selection) ?? "none"}</span>;
    }

    function ProjectActionsOnly() {
      const groups = useProjectActions();
      projectActionsSeen = groups.flatMap((g) => g.actions.map((a) => `${a.id}:${a.available}`));
      return <span data-testid="project-actions">{projectActionsSeen.join(",")}</span>;
    }

    render(
      <ContextConnectionProvider projectId={projectId}>
        <SelectionOnly />
        <ProjectActionsOnly />
      </ContextConnectionProvider>,
    );
    await flush();

    // Establish a selection, then note how many times the selection consumer has rendered.
    streams[0]!.push(selectionMessage(entryA));
    await waitFor(() => expect(screen.getByTestId("sel").textContent).not.toBe("none"));
    const rendersBefore = selectionRenders;
    const selectionBefore = screen.getByTestId("sel").textContent;

    // A project-actions push updates the hook...
    streams[0]!.push(projectActionsMessage(true));
    await waitFor(() => expect(screen.getByTestId("project-actions").textContent).toContain("history.undo:true"));
    expect(projectActionsSeen).toEqual(["history.undo:true", "history.redo:false"]);

    // ...and leaves the selection consumer exactly as it was - same value, no extra render.
    expect(screen.getByTestId("sel").textContent).toBe(selectionBefore);
    expect(selectionRenders).toBe(rendersBefore);
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

function problemsMessage(state: ProblemSetState, messages: string[]): ContextMessage {
  return create(ContextMessageSchema, {
    message: {
      case: "problems",
      value: {
        state,
        problems: messages.map((message) => ({
          severity: ProblemSeverity.ERROR,
          message,
          path: { segments: ["flow.adp"] },
          ruleId: "core.unknown-type",
        })),
        errorCount: messages.length,
      },
    },
  });
}

describe("ContextConnectionProvider problems", () => {
  beforeEach(() => {
    streams.length = 0;
    watch.mockClear();
  });

  it("routes a problems message to useContextProblems without touching the selection", async () => {
    // A validation finishing must re-render the panel, not every selection consumer.
    let selectionRenders = 0;

    function SelectionOnly() {
      selectionRenders++;
      const { selection } = useContextSelection();
      return <span data-testid="sel">{innermostKey(selection) ?? "none"}</span>;
    }

    function ProblemsOnly() {
      const problems = useContextProblems();
      return (
        <span data-testid="problems">
          {problems ? `${ProblemSetState[problems.state]}:${problems.problems.map((p) => p.message).join(",")}` : "no baseline"}
        </span>
      );
    }

    render(
      <ContextConnectionProvider projectId={projectId}>
        <SelectionOnly />
        <ProblemsOnly />
      </ContextConnectionProvider>,
    );
    await flush();

    streams[0]!.push(selectionMessage(entryA));
    await waitFor(() => expect(screen.getByTestId("sel").textContent).not.toBe("none"));
    const rendersBefore = selectionRenders;
    const selectionBefore = screen.getByTestId("sel").textContent;

    // A problems push updates the hook - exactly what the backend sent...
    streams[0]!.push(problemsMessage(ProblemSetState.VALIDATED, ["'vendor/unheard-of' is not a known diagram type."]));
    await waitFor(() => expect(screen.getByTestId("problems").textContent).toContain("VALIDATED"));
    expect(screen.getByTestId("problems").textContent).toContain("not a known diagram type");

    // ...and leaves the selection consumer exactly as it was - same value, no extra render.
    expect(screen.getByTestId("sel").textContent).toBe(selectionBefore);
    expect(selectionRenders).toBe(rendersBefore);
  });

  it("keeps the problems through an unrelated selection change", async () => {
    function ProblemsOnly() {
      const problems = useContextProblems();
      return <span data-testid="problems">{problems ? String(problems.problems.length) : "no baseline"}</span>;
    }

    function SelectionOnly() {
      const { selection } = useContextSelection();
      return <span data-testid="sel">{innermostKey(selection) ?? "none"}</span>;
    }

    render(
      <ContextConnectionProvider projectId={projectId}>
        <SelectionOnly />
        <ProblemsOnly />
      </ContextConnectionProvider>,
    );
    await flush();

    streams[0]!.push(problemsMessage(ProblemSetState.VALIDATED, ["one", "two"]));
    await waitFor(() => expect(screen.getByTestId("problems").textContent).toBe("2"));

    streams[0]!.push(selectionMessage(entryA));
    await waitFor(() => expect(screen.getByTestId("sel").textContent).not.toBe("none"));

    expect(screen.getByTestId("problems").textContent).toBe("2");
  });

  it("names the problems source for the panel", () => {
    expect(PROBLEMS_SOURCE.source.case).toBe("problems");
  });
});
