using Serilog;

namespace EtAlii.Adp.Projects;

/// <summary>
/// Answers which folder on disk a project id stands for, for the user asking - the
/// one authorization-plus-existence check every per-project gRPC call starts with.
/// </summary>
/// <remarks>
/// It answers a <see cref="ProjectRoot"/>, normalized once here, so the services pass the root
/// down as a value rather than as a string each layer would have to normalize and compare again
/// (code-structure FR-059).
/// </remarks>
public static class ProjectRootResolver
{
    private static readonly ILogger _logger = Log.ForContext(typeof(ProjectRootResolver));

    public static bool TryResolve(IProjectStore projectStore, ShortGuid userId, ShortGuid projectId, out ProjectRoot root, out string error)
    {
        var project = projectStore.List(userId).FirstOrDefault(p => p.Id == projectId);
        if (project is null)
        {
            // Either the project was removed, or this user is asking about someone else's:
            // the same answer either way, and worth seeing, because every per-project call
            // starts here and this is where an unauthorized one stops.
            _logger.Warning("Project {ProjectId} is not one of {UserId}'s projects", projectId, userId);
            root = default;
            error = "Project not found.";
            return false;
        }

        var candidatePath = project.Path.Segments.AbsolutePath();
        if (!Directory.Exists(candidatePath))
        {
            _logger.Warning(
                "The root folder of project {ProjectId} is gone: {ProjectPath} no longer exists",
                projectId,
                candidatePath);
            root = default;
            error = "The project's root folder is no longer accessible.";
            return false;
        }

        root = ProjectRoot.From(candidatePath);
        error = "";
        return true;
    }
}
