import { useState } from "react";
import { AuthProvider, useAuth } from "./auth/AuthContext";
import { LoginPage } from "./pages/LoginPage";
import { ProjectGridPage } from "./pages/ProjectGridPage";
import { WorkspaceShell } from "./shell/WorkspaceShell";
import type { Project } from "./generated/projects_pb";

function Gate() {
  const { isAuthenticated } = useAuth();
  const [openProject, setOpenProject] = useState<Project | null>(null);

  if (!isAuthenticated) {
    return <LoginPage />;
  }

  if (openProject) {
    return (
      <WorkspaceShell
        projectId={openProject.id?.value ?? new Uint8Array(0)}
        projectName={openProject.name}
        onBack={() => setOpenProject(null)}
      />
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
