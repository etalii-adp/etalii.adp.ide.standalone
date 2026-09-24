import type { Interceptor } from "@connectrpc/connect";

/**
 * How long a unary call may go unanswered before the page says so. Long enough that ordinary
 * slowness never trips it, short enough that a user has not yet concluded the application is
 * broken and reloaded.
 */
export const UNANSWERED_AFTER_MS = 8_000;

/** The one sentence shown. A bound on TIME, deliberately not a diagnosis - see below. */
export const NOT_RESPONDING_TEXT =
  "The application is not getting answers from the server. Anything you do now may not take effect. " +
  "If you have ADP open in other browser tabs, closing them may help.";

/**
 * Surfaces a request that has not completed within a bounded time, so a wedged application does
 * not look like a working one (two-tab-connection-wedge Requirement 5.1).
 *
 * **It is a bound on time, not a diagnosis, and Requirement 5.2 forbids the alternative.** From
 * inside the page a request queued by the browser and a request the server never answered are
 * *identical*: there is no API that distinguishes them. Six attempts to tell them apart failed
 * during the investigation and the seventh needed `netstat` from outside the browser. So this says
 * the application has stopped getting answers and does not claim to know why - and the recovery is
 * offered as a suggestion, because closing another tab is not a remedy any user reaches unaided
 * (Requirement 5.3).
 *
 * **Unary calls only.** The server-streaming calls - `WatchHierarchy`, `ContextService/Watch`,
 * `DiagramService/Open` - are *supposed* to stay open for the life of the page, so a completion
 * bound on them would fire on every healthy connection within seconds. That is not a refinement:
 * a guard that fires constantly on correct behaviour is worse than none, because it trains its
 * reader to ignore it.
 *
 * **No retry.** Requirement 5.2's neighbour in the design: on an exhausted pool a retry joins the
 * same queue, so it makes the symptom worse while appearing to act.
 *
 * **One notice per episode, not one per request.** A wedged origin strands every subsequent
 * request, so a notice each would bury the first under identical copies of itself. The count of
 * outstanding-and-overdue calls decides: the first to go overdue speaks, and nothing speaks again
 * until every overdue call has settled.
 */
export function createSlowRequestInterceptor(
  report: (text: string) => void,
  options: { afterMs?: number; text?: string } = {},
): Interceptor {
  const afterMs = options.afterMs ?? UNANSWERED_AFTER_MS;
  const text = options.text ?? NOT_RESPONDING_TEXT;

  // Shared across every call the interceptor sees, which is what makes "per episode" possible.
  let overdue = 0;

  return (next) => async (request) => {
    if (request.stream) {
      return next(request);
    }

    let timer: ReturnType<typeof setTimeout> | undefined = setTimeout(() => {
      timer = undefined;
      overdue += 1;
      if (overdue === 1) {
        report(text);
      }
    }, afterMs);

    try {
      return await next(request);
    } finally {
      if (timer !== undefined) {
        clearTimeout(timer);
      } else {
        // It had already gone overdue, so this settling is what lets a later episode speak.
        overdue -= 1;
      }
    }
  };
}
