/**
 * Where an action pointed (etalii.adp spec 092, FR-014): a document to open in its tool, or bring to the front, and the element
 * of it to select once its canvas draws it. The backend decides it and pushes it on the stream; the tab strip opens the tab and
 * the canvas that draws the document selects the element. The same module-level-registry shape `textTabRequests` uses, because
 * tabs are client state and a canvas that is not mounted yet cannot be told directly.
 */
export interface RevealRequest {
  /** The document's entry on this connection, which its tab is opened with. */
  entryId: Uint8Array;
  /** Project-relative segments of the document. */
  path: string[];
  /** Which tool opens it, as an activation's entry detail says. */
  mimeType: string;
  /** The element to select; empty for the document alone. */
  elementId: string;
}

type Listener = (request: RevealRequest) => void;

const listeners = new Set<Listener>();
const elementListeners = new Set<() => void>();

/** The element waiting to be selected, per document path, until the canvas drawing that document takes it. */
const pendingElements = new Map<string, string>();

const keyOf = (path: readonly string[]) => path.join("/");

/** The tab strip subscribes; the unsubscribe is the return value, effect-style. */
export function onRevealRequested(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** A canvas subscribes, to hear of an element to select in a document it already draws. */
export function onElementRevealRequested(listener: () => void): () => void {
  elementListeners.add(listener);
  return () => elementListeners.delete(listener);
}

/** Opens the document's tab, or brings it to the front, and leaves its element for its canvas to select. */
export function requestReveal(request: RevealRequest): void {
  if (request.elementId !== "") {
    pendingElements.set(keyOf(request.path), request.elementId);
  }
  for (const listener of listeners) {
    listener(request);
  }
  for (const listener of elementListeners) {
    listener();
  }
}

/**
 * The element waiting to be selected in the document at `path`, when `has` says the canvas draws it; taken, so it is selected
 * once. An element the canvas does not draw yet stays waiting, for the model that brings it.
 */
export function takeElementReveal(path: readonly string[], has: (elementId: string) => boolean): string | null {
  const key = keyOf(path);
  const waiting = pendingElements.get(key);
  if (waiting === undefined || !has(waiting)) {
    return null;
  }
  pendingElements.delete(key);
  return waiting;
}
