import { useState, type FormEvent } from "react";
import { ConnectError } from "@connectrpc/connect";
import { useAuth } from "../auth/AuthContext";

export function LoginPage() {
  const { login } = useAuth();
  const [username, setUsername] = useState("");
  const [credential, setCredential] = useState("");
  const [loginError, setLoginError] = useState<string | null>(null);
  const [connectionError, setConnectionError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

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
    <form onSubmit={handleSubmit}>
      <h1>Sign in to EtAlii.Adp</h1>

      <label>
        Username
        <input
          value={username}
          onChange={(event) => setUsername(event.target.value)}
          autoComplete="username"
          required
        />
      </label>

      <label>
        Credential
        <input
          type="password"
          value={credential}
          onChange={(event) => setCredential(event.target.value)}
          autoComplete="current-password"
          required
        />
      </label>

      <button type="submit" disabled={isSubmitting}>
        {isSubmitting ? "Signing in…" : "Sign in"}
      </button>

      {loginError && <p role="alert">{loginError}</p>}
      {connectionError && <p role="alert">{connectionError}</p>}
    </form>
  );
}
