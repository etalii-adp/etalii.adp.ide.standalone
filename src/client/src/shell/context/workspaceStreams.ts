import { base64Encode } from "@bufbuild/protobuf/wire";
import type { Delta } from "../../generated/deltas_pb";
import type { HierarchyMessage } from "../../generated/hierarchy_pb";
import type { DiagramStreamMessage } from "../../generated/workspace_pb";

/**
 * The sentence a hierarchy feed fails with when the tab's one stream drops - what the explorer
 * showed when its own stream dropped, so a reader sees the same thing for the same event.
 */
export const HIERARCHY_LOST_TEXT = "Lost connection to the file hierarchy.";

/** A queue a producer pushes into and one consumer iterates, ended or failed by the producer. */
class Feed<T> implements AsyncIterable<T> {
  private readonly items: T[] = [];
  private waiter: ((result: IteratorResult<T>) => void) | undefined;
  private failWaiter: ((error: unknown) => void) | undefined;
  private finished = false;
  private error: unknown = undefined;

  push(item: T) {
    if (this.finished) {
      return;
    }
    if (this.waiter) {
      const resolve = this.waiter;
      this.waiter = undefined;
      this.failWaiter = undefined;
      resolve({ value: item, done: false });
      return;
    }
    this.items.push(item);
  }

  end() {
    if (this.finished) {
      return;
    }
    this.finished = true;
    if (this.waiter) {
      const resolve = this.waiter;
      this.waiter = undefined;
      this.failWaiter = undefined;
      resolve({ value: undefined, done: true });
    }
  }

  fail(error: unknown) {
    if (this.finished) {
      return;
    }
    this.error = error;
    this.finished = true;
    if (this.failWaiter) {
      const reject = this.failWaiter;
      this.waiter = undefined;
      this.failWaiter = undefined;
      reject(error);
    }
  }

  [Symbol.asyncIterator](): AsyncIterator<T> {
    return {
      next: () => {
        const item = this.items.shift();
        if (item !== undefined) {
          return Promise.resolve({ value: item, done: false });
        }
        if (this.finished) {
          return this.error === undefined ? Promise.resolve({ value: undefined, done: true }) : Promise.reject(this.error);
        }
        return new Promise<IteratorResult<T>>((resolve, reject) => {
          this.waiter = resolve;
          this.failWaiter = reject;
        });
      },
    };
  }
}

/** What a diagram stream needs from the connection that carries it: its two unary calls. */
export interface DiagramStreamCalls {
  open: (streamId: Uint8Array) => Promise<unknown>;
  close: (streamId: Uint8Array) => Promise<unknown>;
}

/**
 * Fans the tab's one `WorkspaceService.Watch` stream out to what used to open streams of their own
 * (two-tab-connection-wedge Requirement 3.1): the explorer's hierarchy feed, and each open diagram's
 * delta stream. It holds no React state, so the provider owns the stream and this owns the routing.
 *
 * <b>What a consumer sees is what its own stream used to give it.</b> A diagram stream yields the
 * baseline and then every change, ends when the connection drops or the backend ends it - the
 * consumer then re-opens, as it always did - and rejects with the backend's own refusal when the
 * diagram cannot be opened, so a permanent answer still reads as one. A hierarchy feed yields the
 * changes and fails when the connection drops, which is what the explorer's stream did.
 */
export class WorkspaceStreams {
  private connected = false;
  private generation = 0;
  private readonly connectedWaiters = new Set<() => void>();
  private readonly diagrams = new Map<string, Feed<Delta>>();
  private readonly hierarchies = new Set<Feed<HierarchyMessage>>();

  /** The number of diagram streams this tab has open on its connection. */
  get diagramStreamCount(): number {
    return this.diagrams.size;
  }

  /** The stream is up and the backend has registered it: diagram streams may be opened on it. */
  connect() {
    if (this.connected) {
      return;
    }
    this.connected = true;
    this.generation += 1;
    for (const waiter of [...this.connectedWaiters]) {
      waiter();
    }
    this.connectedWaiters.clear();
  }

  /** The stream dropped. Everything it carried ends with it; the backend has already stopped it all. */
  disconnect() {
    this.connected = false;
    for (const feed of this.diagrams.values()) {
      feed.end();
    }
    this.diagrams.clear();
    for (const feed of this.hierarchies) {
      feed.fail(new Error(HIERARCHY_LOST_TEXT));
    }
    this.hierarchies.clear();
  }

  hierarchy(message: HierarchyMessage) {
    for (const feed of this.hierarchies) {
      feed.push(message);
    }
  }

  diagram(message: DiagramStreamMessage) {
    const streamId = message.streamId?.value;
    const feed = streamId === undefined ? undefined : this.diagrams.get(base64Encode(streamId));
    if (feed === undefined) {
      // A stream this tab already closed, whose last deltas were on their way.
      return;
    }
    if (message.event.case === "delta") {
      feed.push(message.event.value);
    } else if (message.event.case === "ended") {
      feed.end();
    }
  }

  /** The hierarchy's changes, until the connection drops or `signal` aborts. */
  async *watchHierarchy(signal: AbortSignal): AsyncGenerator<HierarchyMessage> {
    const feed = new Feed<HierarchyMessage>();
    const stop = () => feed.end();
    this.hierarchies.add(feed);
    signal.addEventListener("abort", stop);
    try {
      yield* feed;
    } finally {
      signal.removeEventListener("abort", stop);
      this.hierarchies.delete(feed);
    }
  }

  /**
   * One diagram's deltas on the connection, opened through `calls.open` once the connection is up
   * and closed through `calls.close` when the consumer stops. The stream id is generated here, so
   * deltas that overtake the open call's answer are already routed to this stream.
   */
  async *openDiagram(calls: DiagramStreamCalls, signal: AbortSignal): AsyncGenerator<Delta> {
    await this.whenConnected(signal);
    const generation = this.generation;
    const streamId = crypto.getRandomValues(new Uint8Array(16));
    const key = base64Encode(streamId);
    const feed = new Feed<Delta>();
    const stop = () => feed.end();
    this.diagrams.set(key, feed);
    signal.addEventListener("abort", stop);
    try {
      // Deliberately not aborted with the signal: an open whose answer is abandoned may still start
      // on the backend, and only the close below - sent once the answer is in - is sure to stop it.
      await calls.open(streamId);
      yield* feed;
    } finally {
      signal.removeEventListener("abort", stop);
      this.diagrams.delete(key);
      // A connection that dropped took the stream with it, and a new one never had it. A refused
      // open is closed too: closing a stream that never started is not an error, and an open that
      // failed in transit may have started one.
      if (this.connected && this.generation === generation) {
        void calls.close(streamId).catch(() => {
          // Advisory: the stream dies with the connection whatever happens to this call.
        });
      }
    }
  }

  private whenConnected(signal: AbortSignal): Promise<void> {
    if (this.connected) {
      return Promise.resolve();
    }
    return new Promise<void>((resolve, reject) => {
      const onAbort = () => {
        this.connectedWaiters.delete(onConnected);
        reject(new DOMException("The diagram stream was closed before the connection opened.", "AbortError"));
      };
      const onConnected = () => {
        signal.removeEventListener("abort", onAbort);
        resolve();
      };
      if (signal.aborted) {
        onAbort();
        return;
      }
      this.connectedWaiters.add(onConnected);
      signal.addEventListener("abort", onAbort);
    });
  }
}
