using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Projects;

/// <summary>
/// Where a project-relative path lands on disk, for every call that names a file of a project:
/// the project root, the containment check, and the file's existence - nothing about which tool.
/// It belongs with the projects because it is the project's answer, read once per request
/// (code-structure FR-062): the service of every kind of tool asks it, and none
/// reads the project store a second time for the same request.
/// </summary>
public static class ProjectTextFile
{
    /// <summary>
    /// Resolves <paramref name="path"/> inside the user's project, answering false - and the
    /// default path - when the project is unknown to the user, the path escapes its root, or no
    /// file is there. The answer carries its root, so a caller needs no second resolution.
    /// </summary>
    public static bool TryResolve(
        IProjectStore projectStore,
        Documents.Wire.ShortGuid projectId,
        Path path,
        ShortGuid userId,
        out ProjectPath file)
    {
        file = default;

        if (!ProjectRootResolver.TryResolve(projectStore, userId, projectId, out var root, out _))
        {
            return false;
        }

        // Within refuses a path that leads out of the root, through .. or a link; the root folder
        // itself is within, but it is not a file.
        if (root.Resolve(System.IO.Path.Combine([root.FullPath, .. path.Segments])) is not { } resolved || !File.Exists(resolved.FullPath))
        {
            return false;
        }

        file = resolved;
        return true;
    }
}
