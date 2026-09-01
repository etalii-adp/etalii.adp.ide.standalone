/**
 * The client-side half of the "open this file as text" gesture (modular-text-editors R5.2,
 * R4.4, R8.1). Workspace tabs are per-connection client state fed by the selection push, so
 * a context action or a problem row that wants a text tab cannot open one through the wire -
 * the context channel carries no gesture data. It asks here instead, and the tab strip
 * listens: the same module-level-registry shape `dirtyTabs` already uses.
 */
export interface TextTabRequest {
  /** Project-relative segments of the file to open. */
  path: string[];
  /**
   * Which editor the tab's stream should force: "*" for whichever the backend's resolver
   * answers, a definition id for one chosen through "Open with…".
   */
  editorId: string;
  /** 1-based line to scroll to once loaded - go-to-line from a problem. */
  line?: number;
}

type Listener = (request: TextTabRequest) => void;

const listeners = new Set<Listener>();

/** The tab strip subscribes; the unsubscribe is the return value, effect-style. */
export function onTextTabRequested(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** Opens (or re-focuses) a text tab for the file. Safe to call with no tab strip mounted. */
export function requestTextTab(request: TextTabRequest): void {
  for (const listener of listeners) {
    listener(request);
  }
}
