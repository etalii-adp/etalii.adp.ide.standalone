const REPO_URL = "https://github.com/vrenken/EtAlii.Adp";

/** Shared credit line shown atop the login, projects, and diagram/workspace views. */
export function AppHeader() {
  return (
    <div className="app-header">
      ADP - Made with{" "}
      <span className="app-header-heart" aria-hidden="true">
        ❤️
      </span>{" "}
      by{" "}
      <a className="app-header-link" href={REPO_URL} target="_blank" rel="noopener noreferrer">
        Peter Vrenken
      </a>
    </div>
  );
}
