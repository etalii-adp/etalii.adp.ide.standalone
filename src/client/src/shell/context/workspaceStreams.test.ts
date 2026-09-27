import { describe, expect, it, vi } from "vitest";
import { create } from "@bufbuild/protobuf";
import { Code, ConnectError } from "@connectrpc/connect";
import { DeltaSchema } from "../../generated/deltas_pb";
import { HierarchyMessageSchema } from "../../generated/hierarchy_pb";
import { DiagramStreamMessageSchema } from "../../generated/workspace_pb";
import { HIERARCHY_LOST_TEXT, WorkspaceStreams } from "./workspaceStreams";

/**
 * The routing half of the tab's one stream (two-tab-connection-wedge Requirement 3.1): each feed
 * has to behave as the stream it replaced did, because `useDiagramStream`'s reconnect policy and
 * the explorer's error line were written against those streams and did not change.
 */

/** A delta told apart from others by the one element id it removes. */
function delta(id: string) {
  return create(DeltaSchema, { action: { case: "remove", value: { elementIds: [{ value: id }] } } });
}

function diagramMessage(streamId: Uint8Array, event: "delta" | "ended", id = "a") {
  return create(DiagramStreamMessageSchema, {
    streamId: { value: streamId },
    event: event === "delta" ? { case: "delta", value: delta(id) } : { case: "ended", value: {} },
  });
}

/** Opens a diagram stream; `opened` resolves with the id it was opened under once the open call is made. */
function openOn(streams: WorkspaceStreams, answer: Promise<unknown> = Promise.resolve({})) {
  const controller = new AbortController();
  let markOpened!: (streamId: Uint8Array) => void;
  const opened = new Promise<Uint8Array>((resolve) => (markOpened = resolve));
  const calls = {
    open: vi.fn((streamId: Uint8Array) => {
      markOpened(streamId);
      return answer;
    }),
    close: vi.fn((_streamId: Uint8Array) => Promise.resolve({})),
  };
  const iterator = streams.openDiagram(calls, controller.signal)[Symbol.asyncIterator]();
  return { controller, opened, calls, next: () => iterator.next() };
}

describe("the tab's one stream, fanned out", () => {
  it("waits for the connection before opening a diagram stream", async () => {
    // Arrange.
    const streams = new WorkspaceStreams();
    const stream = openOn(streams);
    const first = stream.next();

    // Act & assert: nothing is opened on a connection the backend has not registered yet.
    await Promise.resolve();
    expect(stream.calls.open).not.toHaveBeenCalled();
    streams.connect();
    const streamId = await stream.opened;
    streams.diagram(diagramMessage(streamId, "delta", "x"));
    expect(await first).toEqual({ done: false, value: delta("x") });
  });

  it("keeps deltas that overtake the open call's answer", async () => {
    // Arrange: the backend starts pumping before its answer to OpenDiagram is back.
    const streams = new WorkspaceStreams();
    streams.connect();
    let answer!: () => void;
    const stream = openOn(streams, new Promise<void>((resolve) => (answer = resolve)));
    const first = stream.next();
    const streamId = await stream.opened;

    // Act.
    streams.diagram(diagramMessage(streamId, "delta", "early"));
    answer();

    // Assert.
    expect(await first).toEqual({ done: false, value: delta("early") });
  });

  it("routes deltas by stream id, so two open diagrams do not see each other's", async () => {
    // Arrange.
    const streams = new WorkspaceStreams();
    streams.connect();
    const one = openOn(streams);
    const two = openOn(streams);
    const nextOne = one.next();
    const nextTwo = two.next();
    const [idOne, idTwo] = await Promise.all([one.opened, two.opened]);

    // Act.
    streams.diagram(diagramMessage(idTwo, "delta", "two"));
    streams.diagram(diagramMessage(idOne, "delta", "one"));

    // Assert.
    expect(await nextOne).toEqual({ done: false, value: delta("one") });
    expect(await nextTwo).toEqual({ done: false, value: delta("two") });
  });

  it("rejects with the backend's refusal, so a permanent answer still reads as one", async () => {
    // Arrange.
    const streams = new WorkspaceStreams();
    streams.connect();
    const refusal = new ConnectError("The diagram cannot be opened.", Code.FailedPrecondition);
    const stream = openOn(streams, Promise.reject(refusal));

    // Act & assert.
    await expect(stream.next()).rejects.toBe(refusal);
    expect(streams.diagramStreamCount).toBe(0);
  });

  it("ends every diagram stream when the connection drops, without closing what the backend already stopped", async () => {
    // Arrange.
    const streams = new WorkspaceStreams();
    streams.connect();
    const stream = openOn(streams);
    const next = stream.next();
    await stream.opened;

    // Act.
    streams.disconnect();

    // Assert: a clean end is what makes useDiagramStream re-baseline and re-open.
    expect(await next).toEqual({ done: true, value: undefined });
    expect(stream.calls.close).not.toHaveBeenCalled();
    expect(streams.diagramStreamCount).toBe(0);
  });

  it("ends a diagram stream the backend ended, as a dropped stream", async () => {
    // Arrange.
    const streams = new WorkspaceStreams();
    streams.connect();
    const stream = openOn(streams);
    const next = stream.next();
    const streamId = await stream.opened;

    // Act.
    streams.diagram(diagramMessage(streamId, "ended"));

    // Assert.
    expect(await next).toEqual({ done: true, value: undefined });
  });

  it("closes the stream on the backend when its consumer stops", async () => {
    // Arrange.
    const streams = new WorkspaceStreams();
    streams.connect();
    const stream = openOn(streams);
    const next = stream.next();
    const streamId = await stream.opened;

    // Act.
    stream.controller.abort();
    await next;

    // Assert.
    expect(stream.calls.close).toHaveBeenCalledWith(streamId);
    expect(streams.diagramStreamCount).toBe(0);
  });

  it("feeds the hierarchy's changes, and fails the feed when the connection drops", async () => {
    // Arrange.
    const streams = new WorkspaceStreams();
    const feed = streams.watchHierarchy(new AbortController().signal)[Symbol.asyncIterator]();
    const first = feed.next();
    const message = create(HierarchyMessageSchema, { message: { case: "change", value: {} } });

    // Act.
    streams.hierarchy(message);
    const second = feed.next();
    streams.disconnect();

    // Assert: the explorer showed its stream's failure as an error line, and still does.
    expect(await first).toEqual({ done: false, value: message });
    await expect(second).rejects.toThrow(HIERARCHY_LOST_TEXT);
  });
});
