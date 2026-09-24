import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { createClient, type Transport } from "@connectrpc/connect";
import { createGrpcWebTransport } from "@connectrpc/connect-web";
// authentication_pb, not the retired auth_pb: the stub regenerated from the proto that
// actually exists (src/api/authentication.proto), which is where DescribeProduct lives.
import { AuthenticationService } from "../generated/authentication_pb";
import { createAuthInterceptor } from "./grpcAuthInterceptor";
import { createSlowRequestInterceptor } from "./grpcSlowRequestInterceptor";
import { raiseLocalNotice } from "../shell/context/localNotices";
import { DeveloperSessionContext } from "./developerSession";

export type LoginResult = { ok: true } | { ok: false; message: string };

interface AuthContextValue {
  isAuthenticated: boolean;
  /**
   * Whether this session was handed over without anyone signing in
   * (developer-sign-in-bypass Requirement 5.1).
   *
   * Read from the session rather than from a build constant, deliberately: a released build
   * cannot render the marker because it never receives this, and a bypassed session cannot
   * fail to render it because it always does.
   */
  bypassed: boolean;
  /** Shared transport carrying the auth interceptor; reuse for every gRPC-web client. */
  transport: Transport;
  login: (username: string, credential: string) => Promise<LoginResult>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  // In-memory only, per Requirement 4.2 (session end discards client state) -
  // never persisted to localStorage/cookies.
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [bypassed, setBypassed] = useState(false);
  const tokenRef = useRef<string | null>(null);

  // Whether the one question below has been answered yet. Children wait for it; see the
  // effect for why that is the requirement rather than an optimisation.
  const [probing, setProbing] = useState(true);

  const clearSession = useCallback(() => {
    tokenRef.current = null;
    setIsAuthenticated(false);
  }, []);

  const transport = useMemo<Transport>(
    () =>
      createGrpcWebTransport({
        baseUrl: "/",
        // Order is not arbitrary: the slow-request bound wraps the auth interceptor, so the
        // interval it measures is the whole round trip the user is waiting on rather than
        // only the part after the token is attached.
        interceptors: [
          createSlowRequestInterceptor(raiseLocalNotice),
          createAuthInterceptor(() => tokenRef.current, clearSession),
        ],
      }),
    [clearSession],
  );

  const authClient = useMemo(() => createClient(AuthenticationService, transport), [transport]);

  const login = useCallback(
    async (username: string, credential: string): Promise<LoginResult> => {
      const response = await authClient.login({ username, credential });
      if (response.result.case === "session") {
        tokenRef.current = response.result.value.value;
        setIsAuthenticated(true);
        return { ok: true };
      }
      return { ok: false, message: response.result.value?.message ?? "Login failed." };
    },
    [authClient],
  );

  const logout = useCallback(async () => {
    if (tokenRef.current) {
      await authClient.logout({ session: { value: tokenRef.current } });
    }
    clearSession();
  }, [authClient, clearSession]);

  // Ask once, on mount, whether this build will hand out a session without a credential
  // (developer-sign-in-bypass Requirement 1.1). It sets the two values the provider already
  // has, on the transport it already has - the bypass supplies an identity earlier, it does
  // not add a second of anything.
  useEffect(() => {
    let abandoned = false;
    void (async () => {
      try {
        const response = await authClient.developerSession({});
        if (!abandoned && response.result.case === "session") {
          tokenRef.current = response.result.value.value;
          setBypassed(response.bypassed);
          setIsAuthenticated(true);
        }
      } catch {
        // The ordinary path in every released build: there is no handler for this call, so it
        // is refused for want of a session. Silence is deliberate - a console error on every
        // production start would be a defect of its own - and the sign-in form then renders
        // exactly as it does today.
      } finally {
        if (!abandoned) {
          setProbing(false);
        }
      }
    })();
    return () => {
      abandoned = true;
    };
  }, [authClient]);

  const value = useMemo<AuthContextValue>(
    () => ({ isAuthenticated, bypassed, transport, login, logout }),
    [isAuthenticated, bypassed, transport, login, logout],
  );

  if (probing) {
    // Nothing, for the one round trip this takes.
    //
    // This is the whole reason the question is asked here rather than somewhere that could
    // react to the answer later: an effect runs *after* the first render, so rendering
    // children before the answer arrives would show the sign-in form for a frame. The
    // requirement is that the form does not appear, not that it goes away quickly - a form
    // that flashes has still been rendered, and a screenshot taken a moment too early still
    // contains it.
    //
    // The cost is borne by a released build too, which now waits one refused call before
    // showing the form. It is the same blank the page already shows while the bundle boots.
    return null;
  }

  return (
    <AuthContext.Provider value={value}>
      <DeveloperSessionContext.Provider value={bypassed}>{children}</DeveloperSessionContext.Provider>
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
}
