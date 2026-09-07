using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Problems;

/// <summary>
/// The "has this changed since we judged it?" stamp of one path - which may be a file or a
/// folder.
/// </summary>
/// <remarks>
/// <para>
/// Both halves of the staleness mechanism used to reach for <see cref="FileInfo"/> alone, and
/// <see cref="FileInfo.Exists"/> is <c>false</c> for a directory. A problem located at a folder
/// was therefore stamped with <c>default</c> and immediately judged stale - every time, for
/// ever - so it always showed the reader "this verdict may be out of date" about a verdict
/// computed a second ago.
/// </para>
/// <para>
/// A folder is a legitimate thing for a rule to point at: <c>ansible.empty-role</c> blames a
/// role folder precisely because it has no file worth blaming. Found by the
/// ansible-structure-diagram manual pass, where six of seven problems read fresh and the one
/// folder-located warning read stale.
/// </para>
/// </remarks>
internal static class ProblemStamp
{
    /// <summary>
    /// When <paramref name="path"/> last changed and how big it is - zero length for a folder,
    /// which has no size worth comparing. A path that is not there stamps as default, which is
    /// what makes a vanished subject read as stale.
    /// </summary>
    public static (DateTime LastWriteTimeUtc, long Length) Of(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (file.Exists)
            {
                return (file.LastWriteTimeUtc, file.Length);
            }

            var folder = new DirectoryInfo(path);
            return folder.Exists ? (folder.LastWriteTimeUtc, 0) : (default, 0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (default, 0);
        }
    }

    /// <summary>Whether <paramref name="path"/> exists at all, as either a file or a folder.</summary>
    public static bool Exists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The full path, or null when it cannot be formed - used to route a located subject.</summary>
    public static string? FullPathOrNull(string root, string relativePath)
    {
        try
        {
            return IoPath.GetFullPath(IoPath.Combine(root, relativePath));
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            return null;
        }
    }
}
