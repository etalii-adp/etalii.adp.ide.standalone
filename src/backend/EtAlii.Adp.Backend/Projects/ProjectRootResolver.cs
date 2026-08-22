using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Projects;

/// <summary>
/// Answers which folder on disk a project id stands for, for the user asking - the
/// one authorization-plus-existence check every per-project gRPC call starts with.
/// </summary>
public static class ProjectRootResolver
{
    public static bool TryResolve(IProjectStore projectStore, ShortGuid userId, ShortGuid projectId, out string rootPath, out string error)
    {
        var project = projectStore.List(userId).FirstOrDefault(p => p.Id == projectId);
        if (project is null)
        {
            rootPath = "";
            error = "Project not found.";
            return false;
        }

        var candidatePath = IoPath.Combine(project.Path.Segments.ToArray());
        if (!Directory.Exists(candidatePath))
        {
            rootPath = "";
            error = "The project's root folder is no longer accessible.";
            return false;
        }

        rootPath = candidatePath;
        error = "";
        return true;
    }
}
