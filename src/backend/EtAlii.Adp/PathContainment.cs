using IoPath = System.IO.Path;

namespace EtAlii.Adp;

/// <summary>
/// Whether a path lies inside a root folder: the one check standing between a project-relative
/// path a client sent and the rest of the disk.
/// </summary>
/// <remarks>
/// <para>
/// Two things make it more than a prefix test (D6 of the 2026-10-10 analysis; behaviour changes
/// B-10 and B-11). Names compare as the file system compares them: without case on Windows and
/// macOS, and with case on Linux, where <c>/data/Project</c> is another folder than
/// <c>/data/project</c>. And the path compared is where it leads rather than how it is written:
/// every symbolic link along it, junctions included, is followed, so a link inside the root that
/// points out of it does not make what lies beyond it inside.
/// </para>
/// <para>
/// The part of a path that does not exist yet is compared as written, so a file about to be
/// created is judged by the folder it will be created in.
/// </para>
/// </remarks>
public static class PathContainment
{
    // As many links as one path may pass through before it is refused rather than followed; the
    // operating systems stop at about the same number.
    private const int MaximumLinks = 40;

    private static readonly char[] Separators = [IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar];

    private static readonly StringComparison NameComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// How two path names compare on this operating system: without case on Windows and macOS, with
    /// case on Linux. The rule <see cref="IsInside"/> compares by, for code that keys on a path.
    /// </summary>
    public static StringComparer NameComparer { get; } = StringComparer.FromComparison(NameComparison);

    /// <summary>
    /// Whether <paramref name="path"/> lies under <paramref name="rootPath"/>, once both are made
    /// absolute and every link along them is followed. The root itself is not under itself. A
    /// path whose links cannot be followed - a loop, one too many, or an entry this process may
    /// not read - is not inside.
    /// </summary>
    public static bool IsInside(string rootPath, string path) => Compare(rootPath, path, orAt: false);

    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="rootPath"/> itself or lies under it,
    /// judged as <see cref="IsInside"/> judges.
    /// </summary>
    public static bool IsAtOrInside(string rootPath, string path) => Compare(rootPath, path, orAt: true);

    private static bool Compare(string rootPath, string path, bool orAt)
    {
        ArgumentNullException.ThrowIfNull(rootPath);
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            var root = Resolve(IoPath.GetFullPath(rootPath), 0).TrimEnd(Separators);
            var full = Resolve(IoPath.GetFullPath(path), 0).TrimEnd(Separators);
            return full.StartsWith(root + IoPath.DirectorySeparatorChar, NameComparison)
                   || (orAt && string.Equals(full, root, NameComparison));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The absolute <paramref name="fullPath"/> with every link along it replaced by where it
    /// leads, segment by segment from the root of the volume, and the part that does not exist
    /// kept as written.
    /// </summary>
    private static string Resolve(string fullPath, int links)
    {
        var current = IoPath.GetPathRoot(fullPath) ?? "";
        var segments = fullPath[current.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < segments.Length; index++)
        {
            var next = IoPath.Combine(current, segments[index]);

            // LinkTarget reads the entry itself, so a link whose target is gone still counts.
            var target = new FileInfo(next).LinkTarget;
            if (target is not null)
            {
                if (++links > MaximumLinks)
                {
                    throw new IOException($"Following {fullPath} passes through more than {MaximumLinks} links.");
                }

                next = Resolve(IoPath.GetFullPath(target, current), links);
            }
            else if (!IoPath.Exists(next))
            {
                return IoPath.Combine([next, .. segments[(index + 1)..]]);
            }

            current = next;
        }

        return current;
    }
}
