import { useCallback, useEffect, useMemo, useState } from "react";
import { createClient } from "@connectrpc/connect";
import { AppHeader } from "../components/AppHeader";
import { useAuth } from "../auth/AuthContext";
import { ProjectService } from "../generated/projects_pb";
import type { Project } from "../generated/projects_pb";

/** Splits a plain-text OS folder path into Path.segments (Requirement 5.2's first-iteration entry). */
function toPathSegments(rawPath: string): string[] {
  return rawPath.split(/[/\\]+/).filter((segment) => segment.length > 0);
}

/** Hex-encodes a ShortGuid's raw bytes for use as a React list key. */
function toKey(bytes: Uint8Array): string {
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
}

interface ProjectGridPageProps {
  /** Hands the selected project off to the workspace shell (adp-diagram-ide); this component has no dependency on that shell itself. */
  onProjectSelected: (project: Project) => void;
}

export function ProjectGridPage({ onProjectSelected }: ProjectGridPageProps) {
  const { transport } = useAuth();
  const projectClient = useMemo(() => createClient(ProjectService, transport), [transport]);

  const [projects, setProjects] = useState<Project[]>([]);
  const [newName, setNewName] = useState("");
  const [newPath, setNewPath] = useState("");
  const [addError, setAddError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const refresh = useCallback(async () => {
    const response = await projectClient.listProjects({});
    setProjects(response.projects);
    setIsLoading(false);
  }, [projectClient]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const handleAdd = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setAddError(null);

    const segments = toPathSegments(newPath);
    const response = await projectClient.addProject({ name: newName, path: { segments } });

    if (response.result.case === "error") {
      setAddError(response.result.value.message);
      return;
    }

    setNewName("");
    setNewPath("");
    await refresh();
  };

  const handleRemove = async (projectId: Uint8Array) => {
    await projectClient.removeProject({ projectId: { value: projectId } });
    await refresh();
  };

  if (isLoading) {
    return (
      <div className="projects-page">
        <AppHeader />
        <p className="projects-empty">Loading projects…</p>
      </div>
    );
  }

  return (
    <div className="projects-page">
      <AppHeader />
      <h1>Your projects</h1>

      {projects.length === 0 && <p className="projects-empty">You don't have any projects yet. Add one below to get started.</p>}

      <ul className="project-grid">
        {projects.map((project) => (
          <li className="project-card" key={project.id ? toKey(project.id.value) : project.name}>
            <button className="project-card-open" type="button" onClick={() => onProjectSelected(project)}>
              <span className="project-card-name">{project.name}</span>
              <span className="project-card-path">{project.displayPath}</span>
            </button>
            <button
              className="remove-button"
              type="button"
              onClick={() => project.id && void handleRemove(project.id.value)}
              aria-label={`Remove ${project.name}`}
            >
              <span className="mdi mdi-trash-can-outline" aria-hidden="true" />
            </button>
          </li>
        ))}

        <li className="project-card project-card-add">
          <form onSubmit={handleAdd}>
            <label className="field">
              Name
              <input
                value={newName}
                onChange={(event) => setNewName(event.target.value)}
                placeholder="My project"
                required
              />
            </label>
            <label className="field">
              Folder path
              <input
                value={newPath}
                onChange={(event) => setNewPath(event.target.value)}
                placeholder="C:\path\to\project"
                required
              />
            </label>
            <button className="primary-button" type="submit">Add project</button>
            {addError && <p className="error-text" role="alert">{addError}</p>}
          </form>
        </li>
      </ul>
    </div>
  );
}
