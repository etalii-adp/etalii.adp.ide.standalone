import { createContext, useContext, useMemo, useState } from "react";

/**
 * What a canvas is doing, said once by the library (client-centralization Requirement 2.3).
 *
 * Before this, sixteen canvases said it three ways: seven on the shared `canvas-status`, three with
 * blocks of their own, and the rest by returning a message INSTEAD of their canvas - "Reading the
 * folder…", "This timeline could not be opened." - so the library could never draw the status
 * itself: it was not mounted while there was a status to show. Each diagram stream now reports its
 * state here, and the surface around the canvas draws it.
 */
export type CanvasStreamState =
  | { kind: "open" }
  /** Before the first delta. */
  | { kind: "opening" }
  /** A stream that had a model dropped and is waiting out its backoff before re-opening. */
  | { kind: "reconnecting" }
  /** The backend said this diagram cannot be opened here; `reason` is its own sentence, if it gave one. */
  | { kind: "unavailable"; reason: string };

/** Where each stream on a canvas reports its state. A canvas may hold more than one stream. */
export interface CanvasStatusReporter {
  report(stream: symbol, state: CanvasStreamState): void;
  forget(stream: symbol): void;
}

export const CanvasStatusContext = createContext<CanvasStatusReporter | null>(null);

/** The status reporter of the canvas this component is drawn in, or null when it is in none. */
export function useCanvasStatusReporter(): CanvasStatusReporter | null {
  return useContext(CanvasStatusContext);
}

/**
 * One state for the whole canvas from every stream on it: unavailable if any is, else reconnecting,
 * else opening, else open. The worst wins because the canvas is only as usable as its least usable
 * stream.
 */
export function combinedState(states: Iterable<CanvasStreamState>): CanvasStreamState {
  let worst: CanvasStreamState = { kind: "open" };
  const rank = { open: 0, opening: 1, reconnecting: 2, unavailable: 3 } as const;
  for (const state of states) {
    if (rank[state.kind] > rank[worst.kind]) {
      worst = state;
    }
  }
  return worst;
}

/** The state behind one canvas's status, and the stable reporter feeding it. */
export function useCanvasStatus(): { status: CanvasStreamState; reporter: CanvasStatusReporter } {
  const [states, setStates] = useState<ReadonlyMap<symbol, CanvasStreamState>>(new Map());
  const reporter = useMemo<CanvasStatusReporter>(
    () => ({
      report: (stream, state) =>
        setStates((current) => {
          const previous = current.get(stream);
          if (previous !== undefined && sameState(previous, state)) {
            return current;
          }
          return new Map(current).set(stream, state);
        }),
      forget: (stream) =>
        setStates((current) => {
          if (!current.has(stream)) {
            return current;
          }
          const next = new Map(current);
          next.delete(stream);
          return next;
        }),
    }),
    [],
  );
  const status = useMemo(() => combinedState(states.values()), [states]);
  return { status, reporter };
}

function sameState(a: CanvasStreamState, b: CanvasStreamState): boolean {
  return a.kind === b.kind && (a.kind !== "unavailable" || a.reason === (b as { reason: string }).reason);
}

/** What the status says, in the one wording every canvas now shares; null when there is nothing to say. */
export function statusSentence(status: CanvasStreamState): string | null {
  switch (status.kind) {
    case "open":
      return null;
    case "opening":
      return "Opening…";
    case "reconnecting":
      return "Reconnecting…";
    case "unavailable":
      return status.reason !== "" ? status.reason : "This diagram could not be opened.";
  }
}
