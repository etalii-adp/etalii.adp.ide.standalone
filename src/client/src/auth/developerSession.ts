import { createContext, useContext } from "react";

/**
 * Whether the current session was handed over without anyone signing in
 * (developer-sign-in-bypass Requirement 5.1).
 *
 * It lives in its own module, apart from `AuthContext`, for a reason worth keeping: several
 * suites mock `AuthContext` wholesale to render a component with a stubbed `useAuth`. A header
 * that reached into that module for the marker would break every one of them - nineteen, when
 * it was tried - and each would then need a mock entry for a flag its test does not care
 * about. Reading the flag from a context of its own, defaulting to `false`, means a component
 * rendered outside `AuthProvider` shows no marker and needs no arrangement to say so.
 */
export const DeveloperSessionContext = createContext(false);

export function useBypassedSession(): boolean {
  return useContext(DeveloperSessionContext);
}
