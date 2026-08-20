import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { createClient, type Transport } from "@connectrpc/connect";
import { createGrpcWebTransport } from "@connectrpc/connect-web";
import { AuthenticationService } from "../generated/auth_pb";
import { createAuthInterceptor } from "./grpcAuthInterceptor";

export type LoginResult = { ok: true } | { ok: false; message: string };

interface AuthContextValue {
  isAuthenticated: boolean;
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
  const tokenRef = useRef<string | null>(null);

  const clearSession = useCallback(() => {
    tokenRef.current = null;
    setIsAuthenticated(false);
  }, []);

  const transport = useMemo<Transport>(
    () =>
      createGrpcWebTransport({
        baseUrl: "/",
        interceptors: [createAuthInterceptor(() => tokenRef.current, clearSession)],
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

  const value = useMemo<AuthContextValue>(
    () => ({ isAuthenticated, transport, login, logout }),
    [isAuthenticated, transport, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
}
