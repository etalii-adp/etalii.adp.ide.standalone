import { createContext, useCallback, useContext, useMemo, useState } from "react";

/**
 * The one refusal line a canvas shows, and the channel every refusal reaches it by
 * (client-centralization Requirement 2).
 *
 * <b>Why a channel and not a prop.</b> A refusal is answered where the call was made: a module's
 * own `executeAction`, its `moveElementTo`, its `setProperty`, and the library's declared
 * keystroke all resolve inside whichever hook sent them, above the canvas in the tree. Before this
 * channel each of those callers kept a `rejection` state and drew a banner of its own - sixteen of
 * them, three rules for when they cleared, and four canvases that showed nothing at all. The calls
 * now report into the nearest canvas's reporter instead, so there is one line to draw and one rule
 * for it, and a module that sends a call has nothing to wire.
 *
 * <b>The rule</b> (the user's ruling, relayed by the Scrum master, 2026-09-25): the line clears on
 * any new gesture that reaches the backend - a move, a command, a shortcut or a drop - and is
 * dismissed by a click. So a refusal means <i>your last attempt failed</i>, not <i>something failed
 * at some point</i>. Selection is not a gesture in that sense and does not clear it.
 *
 * <b>Outside a canvas nothing is reported.</b> With no reporter in scope - the ribbon, the explorer,
 * the property grid, a library test mounting a bare canvas - every call behaves exactly as it did.
 */
export interface CanvasRefusalReporter {
  /** A move, command, shortcut or drop has been sent: whatever the line said is now stale. */
  attempted(): void;
  /** The backend refused, in its own words. An empty sentence is not a refusal worth showing. */
  refused(message: string): void;
}

/** The nearest canvas's reporter, or null outside one. */
export const CanvasRefusalContext = createContext<CanvasRefusalReporter | null>(null);

/** The reporter of the canvas this component is drawn in, or null when it is in none. */
export function useCanvasRefusalReporter(): CanvasRefusalReporter | null {
  return useContext(CanvasRefusalContext);
}

/** What the line currently says (null when it says nothing), the reporter feeding it, and its dismissal. */
export interface CanvasRefusalLine {
  message: string | null;
  reporter: CanvasRefusalReporter;
  dismiss: () => void;
}

/**
 * The state behind one canvas's refusal line. The reporter is stable for the life of the canvas, so
 * a hook wrapping calls with it does not rebuild them on every refusal.
 */
export function useCanvasRefusalLine(): CanvasRefusalLine {
  const [message, setMessage] = useState<string | null>(null);
  const reporter = useMemo<CanvasRefusalReporter>(
    () => ({
      attempted: () => setMessage(null),
      refused: (sentence) => {
        if (sentence !== "") {
          setMessage(sentence);
        }
      },
    }),
    [],
  );
  const dismiss = useCallback(() => setMessage(null), []);
  return { message, reporter, dismiss };
}

/**
 * Runs one call and tells the reporter its story: attempted before it is sent, refused after if the
 * outcome says so. The outcome is returned unchanged - a caller that branches on it still can.
 *
 * <b>A refusal that lands after a newer attempt is still shown.</b> Clearing happens only when an
 * attempt starts; a refusal is never discarded for arriving late, because a dropped refusal is the
 * silence this channel exists to end.
 */
export async function reportedCall<T extends { accepted: boolean; error: string }>(
  reporter: CanvasRefusalReporter | null,
  call: () => Promise<T>,
): Promise<T> {
  reporter?.attempted();
  const outcome = await call();
  if (!outcome.accepted) {
    reporter?.refused(outcome.error);
  }
  return outcome;
}
