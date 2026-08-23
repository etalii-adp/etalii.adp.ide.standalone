using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// The three forms a node's link takes and the conversions between them (Requirement 12.3):
/// <b>stored</b> map-relative in the node's <c>LINK</c>, because that is what Freeplane
/// defines the attribute to be; <b>sent</b> project-relative, because the client is never
/// given a location outside the project; and, on the way out, paired with the receiving
/// connection's entry id. None is collapsed into another.
/// </summary>
public static class MindmapLinks
{
    /// <summary>True for a link that names something outside the project - a URL, say - which is shown but never resolved to a file.</summary>
    public static bool IsExternal(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && !uri.IsFile;

    /// <summary>
    /// The absolute path a map-relative <paramref name="link"/> in the map at
    /// <paramref name="bodyPath"/> points to, or null when it leaves the project root -
    /// which a link must never do, however it got into the file (Requirement 12.9).
    /// </summary>
    public static string? ResolveWithinProject(string bodyPath, string link, string rootPath)
    {
        if (IsExternal(link))
        {
            return null;
        }

        var mapFolder = IoPath.GetDirectoryName(bodyPath) ?? rootPath;
        string candidate;
        try
        {
            candidate = IoPath.GetFullPath(IoPath.Combine(mapFolder, link.Replace('/', IoPath.DirectorySeparatorChar)));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return IsInside(rootPath, candidate) ? candidate : null;
    }

    /// <summary>The project-relative segments of an absolute path inside the project, for the wire (Requirement 12.2).</summary>
    public static IReadOnlyList<string> ProjectRelative(string fullPath, string rootPath) =>
        IoPath.GetRelativePath(rootPath, fullPath)
            .Split([IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// The map-relative form Freeplane stores, for a target the user picked by project-relative
    /// path. Forward slashes, as Freeplane writes them, whatever the platform.
    /// </summary>
    public static string ToMapRelative(string bodyPath, IReadOnlyList<string> projectRelativeTarget, string rootPath)
    {
        var mapFolder = IoPath.GetDirectoryName(bodyPath) ?? rootPath;
        var target = IoPath.Combine([rootPath, .. projectRelativeTarget]);
        return IoPath.GetRelativePath(mapFolder, target).Replace(IoPath.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Rewrites every file link in <paramref name="document"/> so it still resolves after the
    /// map moves from <paramref name="oldBodyPath"/> to <paramref name="newBodyPath"/> - the
    /// one bookkeeping cost of storing links map-relative (Requirement 12.14). Returns how
    /// many it changed; external links and links that did not resolve are left alone.
    /// </summary>
    public static int Rebase(MindmapDocument document, string oldBodyPath, string newBodyPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        var oldFolder = IoPath.GetDirectoryName(oldBodyPath) ?? "";
        var newFolder = IoPath.GetDirectoryName(newBodyPath) ?? "";
        if (string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
        {
            return 0; // a rename within the folder changes no relative path
        }

        var rewritten = 0;
        foreach (var node in document.Nodes)
        {
            if (node.Link is not { } link || IsExternal(link))
            {
                continue;
            }

            var absolute = IoPath.GetFullPath(IoPath.Combine(oldFolder, link.Replace('/', IoPath.DirectorySeparatorChar)));
            document.SetLink(node, IoPath.GetRelativePath(newFolder, absolute).Replace(IoPath.DirectorySeparatorChar, '/'));
            rewritten++;
        }

        return rewritten;
    }

    private static bool IsInside(string rootPath, string fullPath)
    {
        var root = IoPath.GetFullPath(rootPath).TrimEnd(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar) + IoPath.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
