/**
 * The client-side half of the notice surface: a message the *page* noticed, rather than one the
 * backend sent (two-tab-connection-wedge Requirement 5.1).
 *
 * The same module-level-registry shape `textTabRequests` and `dirtyTabs` already use, and for the
 * same reason: the observer is outside React. A transport interceptor sees every request and
 * cannot read a context, so it asks here and `ContextConnectionProvider` listens - exactly as the
 * tab strip listens for a text-tab request.
 *
 * **Why notices and not the errors-and-warnings panel**, even though that panel is where a reader
 * would look first. `NoticeHost` already wrote the reason down for its own case: that panel is
 * owned by validation, keyed by file and replaced wholesale on the next pass, so anything put
 * there is wiped by the validator moments later. A "the server has stopped answering" message
 * would be erased by the next validation push - which is the one moment it is most needed and
 * least likely to survive. The notice surface is transient by design and dismissed by hand.
 */
type Listener = (text: string, copy?: string) => void;

const listeners = new Set<Listener>();

/** The provider subscribes; the unsubscribe is the return value, effect-style. */
export function onLocalNotice(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/**
 * Raises a notice the page itself observed. Safe to call with no provider mounted - the message
 * is dropped rather than queued, because a notice about a request nobody is waiting for any more
 * has nothing to tell anybody.
 */
export function raiseLocalNotice(text: string, copy?: string): void {
  for (const listener of listeners) {
    listener(text, copy);
  }
}
