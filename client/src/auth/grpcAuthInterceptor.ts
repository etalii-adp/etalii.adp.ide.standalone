import { Code, ConnectError, type Interceptor } from "@connectrpc/connect";

export const SESSION_TOKEN_HEADER = "session-token";

/**
 * Attaches the current session token to every outgoing call, and reacts to
 * an UNAUTHENTICATED response (session interceptor rejection on the
 * backend, Requirement 1.5) by notifying the caller so it can clear auth
 * state and redirect to login.
 */
export function createAuthInterceptor(
  getToken: () => string | null,
  onUnauthenticated: () => void,
): Interceptor {
  return (next) => async (request) => {
    const token = getToken();
    if (token) {
      request.header.set(SESSION_TOKEN_HEADER, token);
    }

    try {
      return await next(request);
    } catch (error) {
      if (error instanceof ConnectError && error.code === Code.Unauthenticated) {
        onUnauthenticated();
      }
      throw error;
    }
  };
}
