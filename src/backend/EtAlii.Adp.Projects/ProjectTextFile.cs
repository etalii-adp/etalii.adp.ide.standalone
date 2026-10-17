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
    /// Resolves <paramref name="path"/> inside the user's project, answering false - and empty
    /// paths - when the project is unknown to the user, the path escapes its root, or no file is
    /// there.
    /// </summary>
    public static bool TryResolve(
        IProjectStore projectStore,
        Documents.Wire.ShortGuid projectId,
        Path path,
        ShortGuid userId,
        out string rootPath,
        out string fullPath)
    {
        fullPath = "";

        if (!ProjectRootResolver.TryResolve(projectStore, userId, projectId, out rootPath, out _))
        {
            return false;
        }

        var combined = System.IO.Path.Combine([rootPath, .. path.Segments]);
        var full = System.IO.Path.GetFullPath(combined);
        if (!PathContainment.IsInside(rootPath, full) || !File.Exists(full))
        {
            return false;
        }

        fullPath = full;
        return true;
    }
}
