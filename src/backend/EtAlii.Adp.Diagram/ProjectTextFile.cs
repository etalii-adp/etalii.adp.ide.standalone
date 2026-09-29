using EtAlii.Adp.Projects;
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// Where a project-relative path lands on disk, for the calls that work on a file's text: the
/// project root, the containment check, and the file's existence - nothing about which editor.
/// Shared by <see cref="DiagramService"/>, whose stream opens a text file through the editor
/// family, and <see cref="EditorService"/>, which saves one.
/// </summary>
internal static class ProjectTextFile
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
        if (!IsInside(rootPath, full) || !File.Exists(full))
        {
            return false;
        }

        fullPath = full;
        return true;
    }

    /// <summary>Whether <paramref name="fullPath"/> lies under <paramref name="rootPath"/>.</summary>
    public static bool IsInside(string rootPath, string fullPath)
    {
        var root = System.IO.Path.GetFullPath(rootPath)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
