import { describe, expect, it, vi } from "vitest";
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

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return {
    ...actual,
    createClient: () => ({
      login: async () => ({ result: { case: "session", value: { value: "a-session-token" } } }),
      logout: async () => ({}),
    }),
  };
});

describe("AuthProvider", () => {
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
});
