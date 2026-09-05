import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import type { Transport } from "@connectrpc/connect";
import { AuthProvider, useAuth } from "./AuthContext";

/**
 * The invariant this file guards:
 *
 * **`AuthProvider` hands out one transport for the life of the provider.**
 *
 * It is not a performance preference. Every service client in the client is acquired with
 * `useMemo(() => createClient(Service, transport), [transport])`, and `useDiagramStream` keys
 * its open-and-reconnect effect on the client it gets. A transport that changed identity per
 * render would therefore rebuild every client and tear down and re-open every diagram stream,
 * continuously - and nothing in those files can detect it, because from their side a new
 * transport is indistinguishable from a genuinely new connection they ought to re-open on.
 *
 * The invariant was previously unguarded and held only by reading: `transport` is memoised on
 * `clearSession`, which is a `useCallback` with an empty dependency list, and the token is
 * reached through a ref rather than through state. Six test mocks had already drifted away
 * from it, handing out a fresh object per call, and exactly one test anywhere noticed.
 */

const transportsBuilt: object[] = [];
vi.mock("@connectrpc/connect-web", () => ({
  // A distinguishable object per call, so a transport rebuilt on re-render is visible as a
  // different identity rather than accidentally passing by being the same empty object.
  createGrpcWebTransport: () => {
    const transport = { built: transportsBuilt.length + 1 };
    transportsBuilt.push(transport);
    return transport;
  },
}));

/**
 * What the developer-session probe answers. The default is the ordinary released-build answer -
 * no session - so every test here runs against today's behaviour unless it says otherwise.
 */
const developerSession: { respond: () => unknown } = {
  respond: () => ({ result: { case: undefined }, bypassed: false }),
};

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return {
    ...actual,
    createClient: () => ({
      login: async () => ({ result: { case: "session", value: { value: "a-session-token" } } }),
      logout: async () => ({}),
      developerSession: async () => developerSession.respond(),
    }),
  };
});

/** Lets the provider's mount probe settle; until it does, it renders no children at all. */
async function settled() {
  await act(async () => {
    await Promise.resolve();
  });
}

describe("AuthProvider", () => {
  beforeEach(() => {
    developerSession.respond = () => ({ result: { case: undefined }, bypassed: false });
  });

  it("hands out one transport identity across renders, including the sign-in that changes its state", async () => {
    // Arrange.
    transportsBuilt.length = 0;
    const handedOut: Transport[] = [];
    let auth: ReturnType<typeof useAuth> | undefined;

    function Probe() {
      const value = useAuth();
      auth = value;
      handedOut.push(value.transport);
      return <span data-testid="authenticated">{String(value.isAuthenticated)}</span>;
    }

    const { rerender } = render(
      <AuthProvider>
        <Probe />
      </AuthProvider>,
    );
    // The provider asks once whether this build hands out a session without a credential, and
    // renders nothing until the answer is in - so there is nothing to observe before this.
    await settled();

    // Act.
    // Two kinds of re-render, because they fail differently. A parent re-render catches a
    // transport built in the render body; a state change inside the provider catches one
    // memoised on something that state moves - which is the mistake worth guarding, since it
    // looks memoised and only churns once somebody signs in.
    rerender(
      <AuthProvider>
        <Probe />
      </AuthProvider>,
    );
    await act(async () => {
      await auth!.login("someone", "a-credential");
    });

    // Assert.
    // First that the renders under test actually happened. Without this the identity check
    // below passes on a single render, proving nothing about what it claims to prove.
    expect(screen.getByTestId("authenticated").textContent).toBe("true");
    expect(
      handedOut.length,
      `The provider rendered ${handedOut.length} times, so there was no re-render to hold an identity across. ` +
        "This guard has stopped reaching its subject rather than found it sound.",
    ).toBeGreaterThanOrEqual(3);

    expect(
      new Set(handedOut).size,
      "AuthProvider handed out more than one transport identity. Every service client is memoised on the " +
        "transport, so each new identity rebuilds every client and makes useDiagramStream tear down and " +
        "re-open every diagram stream - and no consumer can tell that apart from a connection it should " +
        "genuinely re-open on.",
    ).toBe(1);
    expect(
      transportsBuilt.length,
      `createGrpcWebTransport was called ${transportsBuilt.length} times for one provider; it must be called once.`,
    ).toBe(1);
  });

  it("renders nothing at all until the developer session has answered, so no sign-in form can flash", async () => {
    // Arrange.
    // This is the requirement the whole spec rests on, and the one an ordinary test would
    // miss: every other test here awaits the probe before asserting, so all of them pass
    // whether or not the children were rendered a frame earlier. An effect runs *after* the
    // first render, so without the provider withholding its children, App would render the
    // sign-in form and then replace it - and "the form must not appear" is the requirement,
    // not "the form should go away quickly". A form that flashes has been rendered, and a
    // screenshot taken a moment too early still contains it.
    developerSession.respond = () => ({
      result: { case: "session", value: { value: "a-developer-token" } },
      bypassed: true,
    });

    function Probe() {
      const { isAuthenticated } = useAuth();
      return <span data-testid="child">{String(isAuthenticated)}</span>;
    }

    // Act.
    render(
      <AuthProvider>
        <Probe />
      </AuthProvider>,
    );

    // Assert, before letting the probe settle.
    expect(
      screen.queryByTestId("child"),
      "AuthProvider rendered its children before knowing whether this build hands out a session " +
        "without a credential. Whatever is inside has now decided what to show while unauthenticated, " +
        "which for this application is the sign-in form - the one thing this spec exists to keep from " +
        "appearing at all.",
    ).toBeNull();

    // And once it has answered, they render - authenticated.
    await settled();
    expect(screen.getByTestId("child").textContent).toBe("true");
  });

  it("is already authenticated from a developer session, on the one transport it already had", async () => {
    // Arrange.
    // The bypass supplies an identity earlier; it does not add a second of anything
    // (developer-sign-in-bypass Requirement 4.4). Asserting the transport count here rather
    // than restating the invariant is the point - this is the change most likely to break it.
    transportsBuilt.length = 0;
    developerSession.respond = () => ({
      result: { case: "session", value: { value: "a-developer-token" } },
      bypassed: true,
    });
    const handedOut: Transport[] = [];

    function Probe() {
      const value = useAuth();
      handedOut.push(value.transport);
      return (
        <span data-testid="state">
          {String(value.isAuthenticated)}/{String(value.bypassed)}
        </span>
      );
    }

    // Act.
    render(
      <AuthProvider>
        <Probe />
      </AuthProvider>,
    );
    await settled();

    // Assert.
    expect(screen.getByTestId("state").textContent).toBe("true/true");
    expect(new Set(handedOut).size).toBe(1);
    expect(transportsBuilt.length).toBe(1);
  });

  it("renders its children unauthenticated when the developer session answers nothing", async () => {
    // Arrange.
    // Every released build, and any developer build with the bypass turned off. What must
    // happen is nothing at all: today's behaviour, which is that whoever is inside decides to
    // show the sign-in form.
    developerSession.respond = () => ({ result: { case: undefined }, bypassed: false });

    function Probe() {
      const { isAuthenticated, bypassed } = useAuth();
      return (
        <span data-testid="state">
          {String(isAuthenticated)}/{String(bypassed)}
        </span>
      );
    }

    // Act.
    render(
      <AuthProvider>
        <Probe />
      </AuthProvider>,
    );
    await settled();

    // Assert.
    expect(screen.getByTestId("state").textContent).toBe("false/false");
  });

  it("renders its children unauthenticated when the call itself fails", async () => {
    // Arrange.
    // A released build has no handler for this method, so the call is refused rather than
    // answered. It must be as silent as an empty answer - a console error on every production
    // start would be a defect of its own.
    developerSession.respond = () => {
      throw new Error("unimplemented");
    };

    function Probe() {
      const { isAuthenticated } = useAuth();
      return <span data-testid="state">{String(isAuthenticated)}</span>;
    }

    // Act.
    render(
      <AuthProvider>
        <Probe />
      </AuthProvider>,
    );
    await settled();

    // Assert: rendered at all, and rendered unauthenticated. A provider that stayed blank
    // forever on a failed probe would take the sign-in form down with it.
    expect(screen.getByTestId("state").textContent).toBe("false");
  });
});
