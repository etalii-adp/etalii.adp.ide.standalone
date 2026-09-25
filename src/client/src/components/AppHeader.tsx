import { AdpMark } from "./AdpMark";
import { useBypassedSession } from "../auth/developerSession";

const REPO_URL = "https://github.com/etalii-adp/etalii.adp.ide.standalone";

/** Shared credit line shown atop the login, projects, and diagram/workspace views. */
export function AppHeader() {
  const bypassed = useBypassedSession();

  return (
    <div className="app-header">
      <AdpMark size={16} className="app-header-mark" />
      ADP - Made with{" "}
      <span className="app-header-heart" aria-hidden="true">
        ❤️
      </span>{" "}
      by{" "}
      <a className="app-header-link" href={REPO_URL} target="_blank" rel="noopener noreferrer">
        Peter Vrenken
      </a>
      {/* A session nobody signed in for says so, wherever this header is shown - which is the
          login, projects and workspace views alike, so it is in every screenshot taken under
          the bypass (developer-sign-in-bypass Requirement 5.2). That is its purpose: it stops
          a picture from a developer build being read later as evidence about a released one.
          It comes from the session, so a released build cannot render it. */}
      {bypassed && (
        <span className="app-header-developer-session" title="This session was issued to a developer build without a sign-in">
          developer session
        </span>
      )}
    </div>
  );
}
