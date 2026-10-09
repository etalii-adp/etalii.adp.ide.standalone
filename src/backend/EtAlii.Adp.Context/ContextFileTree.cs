using Serilog;

namespace EtAlii.Adp.Context;

/// <summary>
/// The workspace's files as the option tree of a file dialog: every file the asking provider
/// accepts, under the folders that lead to it, and nothing else. A folder holding no accepted
/// file is left out, so the tree shows where the choices are and not the whole project.
/// </summary>
/// <remarks>
/// An option's id is the file's project-relative path with <c>/</c> between its segments - the
/// dialog's answer, and what a submission is checked against. A folder is a group: shown,
/// expandable and never the answer.
/// <para>
/// Entries whose name starts with a dot are skipped, folders and files alike: a project's
/// <c>.git</c> is not somewhere a user picks a file from, and walking it would cost more than
/// everything else in the project together.
/// </para>
/// </remarks>
public static class ContextFileTree
{
    private static readonly ILogger _logger = Log.ForContext(typeof(ContextFileTree));

    /// <param name="rootPath">The project's root folder.</param>
    /// <param name="accepts">Whether the file at this full path may be chosen.</param>
    public static IReadOnlyList<ContextOptionNode> Build(string rootPath, Func<string, bool> accepts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(accepts);
        return Directory.Exists(rootPath) ? Children(rootPath, "", accepts) : [];
    }

    /// <summary>The project-relative path of <paramref name="fullPath"/> as an option id, or null when it is not inside the root.</summary>
    public static string? IdOf(string rootPath, string fullPath)
    {
        var relative = Path.GetRelativePath(rootPath, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? null
            : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static List<ContextOptionNode> Children(string folder, string prefix, Func<string, bool> accepts)
    {
        var nodes = new List<ContextOptionNode>();
        try
        {
            // Folders first, then files, each by name: the order the workspace tree shows.
            foreach (var directory in Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directory);
                if (name.StartsWith('.'))
                {
                    continue;
                }

                var children = Children(directory, prefix + name + "/", accepts);
                if (children.Count > 0)
                {
                    nodes.Add(new ContextOptionNode(prefix + name, name, Selectable: false, Children: children, Icon: "mdi-folder-outline"));
                }
            }

            foreach (var file in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file);
                if (!name.StartsWith('.') && accepts(file))
                {
                    nodes.Add(new ContextOptionNode(prefix + name, name, Selectable: true, Icon: "mdi-file-outline"));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed offers nothing; the rest of the tree still stands.
            _logger.Debug(exception, "Leaving {Folder} out of a file dialog: it cannot be listed", folder);
        }

        return nodes;
    }
}
