import { useEffect, useMemo, useState, type FormEvent } from "react";
import { ConnectError, createClient } from "@connectrpc/connect";
import { AdpLogo } from "../components/AdpLogo";
import { AppHeader } from "../components/AppHeader";
import { useAuth } from "../auth/AuthContext";
import { AuthenticationService } from "../generated/authentication_pb";

export function LoginPage() {
  const { login, transport } = useAuth();
  const [username, setUsername] = useState("");
  const [credential, setCredential] = useState("");
  const [loginError, setLoginError] = useState<string | null>(null);
  const [connectionError, setConnectionError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [version, setVersion] = useState("");

  // One shape for acquiring a service client, `useMemo` on the transport, the same as every
  // other site that needs one. What stood here was a client constructed inline inside the
  // effect - not wrong, since the effect runs once, but a third way of doing a single thing.
  const authClient = useMemo(() => createClient(AuthenticationService, transport), [transport]);

  useEffect(() => {
    // Fire-and-forget: the backend names its own NB.GV-stamped version - the one source of
    // truth (github-build-pipeline R3.2) - and a failed or empty answer renders nothing
    // rather than a stale or invented number (R3.3).
    let active = true;
    authClient
      .describeProduct({})
      .then((response) => {
        if (active) {
          setVersion(response.version);
        }
      })
      .catch(() => {
        // No line is the honest state; the connection error surfaces through login itself.
      });
    return () => {
      active = false;
    };
  }, [authClient]);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setLoginError(null);
    setConnectionError(null);
    setIsSubmitting(true);

    try {
      const result = await login(username, credential);
      if (!result.ok) {
        setLoginError(result.message);
      }
    } catch (error) {
      // Distinct from LoginError: the call never reached AuthenticationService.
      setConnectionError(
        error instanceof ConnectError
          ? `Could not reach the backend: ${error.message}`
          : "Could not reach the backend.",
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="auth-page">
      <AppHeader />
      <div className="auth-page-body">
        <div className="auth-stack">
          <AdpLogo />
          <form className="auth-card" onSubmit={handleSubmit}>
            <h1>Sign in</h1>

            <label className="field">
              Username
              <input
                value={username}
                onChange={(event) => setUsername(event.target.value)}
                autoComplete="username"
                required
              />
            </label>

            <label className="field">
              Credential
              <input
                type="password"
                value={credential}
                onChange={(event) => setCredential(event.target.value)}
                autoComplete="current-password"
                required
              />
            </label>

            <button className="primary-button" type="submit" disabled={isSubmitting}>
              {isSubmitting ? "Signing in…" : "Sign in"}
            </button>

            {loginError && <p className="error-text" role="alert">{loginError}</p>}
            {connectionError && <p className="error-text" role="alert">{connectionError}</p>}
          </form>
          {version !== "" && <p className="auth-version">{version}</p>}
        </div>
      </div>
    </div>
  );
}
