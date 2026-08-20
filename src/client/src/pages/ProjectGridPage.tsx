import { useCallback, useEffect, useMemo, useState } from "react";
import { createClient } from "@connectrpc/connect";
import { useAuth } from "../auth/AuthContext";
import { ProjectService } from "../generated/projects_pb";
import type { Project } from "../generated/projects_pb";

/** Splits a plain-text OS folder path into Path.segments (Requirement 5.2's first-iteration entry). */
function toPathSegments(rawPath: string): string[] {
  return rawPath.split(/[/\\]+/).filter((segment) => segment.length > 0);
}

function TrashIcon() {
  return (
    <svg viewBox="0 0 20 20" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M4 6h12" />
      <path d="M7.5 6V4.5a1 1 0 0 1 1-1h3a1 1 0 0 1 1 1V6" />
      <path d="M15 6l-.75 10a1 1 0 0 1-1 .9H6.75a1 1 0 0 1-1-.9L5 6" />
      <path d="M8.5 9v5" />
      <path d="M11.5 9v5" />
    </svg>
  );
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

  const handleRemove = async (projectId: string) => {
    await projectClient.removeProject({ projectId });
    await refresh();
  };

  if (isLoading) {
    return (
      <div className="projects-page">
        <p className="projects-empty">Loading projects…</p>
      </div>
    );
  }

  return (
    <div className="projects-page">
      <h1>Your projects</h1>

      {projects.length === 0 && <p className="projects-empty">You don't have any projects yet. Add one below to get started.</p>}

      <ul className="project-grid">
        {projects.map((project) => (
          <li className="project-card" key={project.id}>
            <button className="project-card-open" type="button" onClick={() => onProjectSelected(project)}>
              <span className="project-card-name">{project.name}</span>
              <span className="project-card-path">{project.displayPath}</span>
            </button>
            <button
              className="remove-button"
              type="button"
              onClick={() => void handleRemove(project.id)}
              aria-label={`Remove ${project.name}`}
            >
              <TrashIcon />
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
