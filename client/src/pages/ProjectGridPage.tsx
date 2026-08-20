import { useCallback, useEffect, useMemo, useState } from "react";
import { createClient } from "@connectrpc/connect";
import { useAuth } from "../auth/AuthContext";
import { ProjectService } from "../generated/projects_pb";
import type { Project } from "../generated/projects_pb";

/** Splits a plain-text OS folder path into Path.segments (Requirement 5.2's first-iteration entry). */
function toPathSegments(rawPath: string): string[] {
  return rawPath.split(/[/\\]+/).filter((segment) => segment.length > 0);
}

interface ProjectGridPageProps {
  /** Hands the selected project off to the workspace shell (adp-diagram-ide); this component has no dependency on that shell itself. */
  onProjectSelected: (project: Project) => void;
}

export function ProjectGridPage({ onProjectSelected }: ProjectGridPageProps) {
  const { transport } = useAuth();
  const projectClient = useMemo(() => createClient(ProjectService, transport), [transport]);

  const [projects, setProjects] = useState<Project[]>([]);
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
    const response = await projectClient.addProject({ path: { segments } });

    if (response.result.case === "error") {
      setAddError(response.result.value.message);
      return;
    }

    setNewPath("");
    await refresh();
  };

  const handleRemove = async (projectId: string) => {
    await projectClient.removeProject({ projectId });
    await refresh();
  };

  if (isLoading) {
    return <p>Loading projects…</p>;
  }

  return (
    <div>
      <h1>Your projects</h1>

      {projects.length === 0 ? (
        <p>You don't have any projects yet. Add one below to get started.</p>
      ) : (
        <ul>
          {projects.map((project) => (
            <li key={project.id}>
              <button type="button" onClick={() => onProjectSelected(project)}>
                {project.name}
              </button>
              <button type="button" onClick={() => void handleRemove(project.id)} aria-label={`Remove ${project.name}`}>
                Remove
              </button>
            </li>
          ))}
        </ul>
      )}

      <form onSubmit={handleAdd}>
        <label>
          Folder path
          <input
            value={newPath}
            onChange={(event) => setNewPath(event.target.value)}
            placeholder="C:\path\to\project"
            required
          />
        </label>
        <button type="submit">Add project</button>
        {addError && <p role="alert">{addError}</p>}
      </form>
    </div>
  );
}
