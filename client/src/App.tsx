import { useState } from "react";
import { AuthProvider, useAuth } from "./auth/AuthContext";
import { LoginPage } from "./pages/LoginPage";
import { ProjectGridPage } from "./pages/ProjectGridPage";
import type { Project } from "./generated/projects_pb";

function Gate() {
  const { isAuthenticated } = useAuth();
  const [openProject, setOpenProject] = useState<Project | null>(null);

  if (!isAuthenticated) {
    return <LoginPage />;
  }

  if (openProject) {
    // The workspace shell itself belongs to the adp-diagram-ide spec; this
    // is the hand-off point Requirement 2.3 describes.
    return (
      <div>
        <button type="button" onClick={() => setOpenProject(null)}>
          &larr; Back to projects
        </button>
        <p>Workspace shell for "{openProject.name}" goes here (adp-diagram-ide).</p>
      </div>
    );
  }

  return <ProjectGridPage onProjectSelected={setOpenProject} />;
}

export function App() {
  return (
    <AuthProvider>
      <Gate />
    </AuthProvider>
  );
}
