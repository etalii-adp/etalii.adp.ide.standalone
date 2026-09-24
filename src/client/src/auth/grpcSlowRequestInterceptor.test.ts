import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createSlowRequestInterceptor } from "./grpcSlowRequestInterceptor";

/**
 * The bound on how long an incomplete request may look like success
 * (two-tab-connection-wedge Requirement 5.1, task 4).
 *
 * The streaming case is the one that matters most and is easiest to leave out: the three
 * server-streaming calls are SUPPOSED to stay open for the life of the page, so a bound that
 * measured them would fire on every healthy connection within seconds. A guard that fires
 * constantly on correct behaviour is worse than none, because it trains its reader to ignore it.
 */

/** A request shaped as connect-es hands one over; only `stream` is read by the interceptor. */
function requestFor(stream: boolean) {
  return { stream } as never;
}

describe("createSlowRequestInterceptor", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("reports a unary call that has not come back within the bound", async () => {
    // Arrange. A call that never settles - the wedge, from inside the page.
    const reported: string[] = [];
    const interceptor = createSlowRequestInterceptor((text) => reported.push(text), { afterMs: 100 });
    const never = new Promise<never>(() => {});
    void interceptor(() => never)(requestFor(false));

    // Act.
    await vi.advanceTimersByTimeAsync(100);

    // Assert. One message, and it says the application is not getting answers rather than
    // claiming to know why - a request queued by the browser and one the server never answered
    // are indistinguishable from here (Requirement 5.2).
    expect(reported).toHaveLength(1);
    expect(reported[0]).toContain("not getting answers from the server");
    expect(reported[0]).toContain("closing them may help");
  });

  it("says nothing about a server-streaming call that stays open, which is what they all do", async () => {
    // Arrange. A stream that never completes is a HEALTHY stream.
    const reported: string[] = [];
    const interceptor = createSlowRequestInterceptor((text) => reported.push(text), { afterMs: 100 });
    const never = new Promise<never>(() => {});
    void interceptor(() => never)(requestFor(true));

    // Act. Ten times the bound.
    await vi.advanceTimersByTimeAsync(1_000);

    // Assert.
    expect(reported).toEqual([]);
  });

  it("says nothing about a unary call that comes back inside the bound", async () => {
    // Arrange.
    const reported: string[] = [];
    const interceptor = createSlowRequestInterceptor((text) => reported.push(text), { afterMs: 100 });

    // Act.
    await interceptor(() => Promise.resolve("done" as never))(requestFor(false));
    await vi.advanceTimersByTimeAsync(1_000);

    // Assert. And the timer was cleared rather than left to fire later.
    expect(reported).toEqual([]);
  });

  it("speaks once per episode however many calls are stranded", async () => {
    // Arrange. A wedged origin strands every subsequent request, so one notice each would bury
    // the first under identical copies of itself.
    const reported: string[] = [];
    const interceptor = createSlowRequestInterceptor((text) => reported.push(text), { afterMs: 100 });
    const never = new Promise<never>(() => {});
    for (let i = 0; i < 5; i++) {
      void interceptor(() => never)(requestFor(false));
    }

    // Act.
    await vi.advanceTimersByTimeAsync(500);

    // Assert.
    expect(reported).toHaveLength(1);
  });
});
