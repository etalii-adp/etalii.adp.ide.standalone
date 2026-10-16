using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// The directories of a chart folder the plugin needs and no file rule can select, handed to the folder body
/// as supplementary entries (<see cref="EtAlii.Adp.Specification.Fbl.FblOptions.SupplementaryEntries"/>): each a
/// path relative to the folder ending in <c>/</c>, with no bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>A stand-in for gap 3 of the definition's companion</b> (<c>definition/helm-chart.md</c>; findings B4 and
/// B5, Requirement 3.5). File rules select files, so a <c>crds/</c> with no file in it, a directory under
/// <c>charts/</c> with no <c>Chart.yaml</c>, and a directory counted among a subchart's deeper entries would
/// all be lost, and today's answer for each would change. It goes when FBL lets a file rule select entries
/// that are directories.
/// </para>
/// <para>
/// It lists directory names only: the <c>crds</c> folder itself, every directory directly under
/// <c>charts/</c>, and every directory directly under each of those subcharts' own <c>charts/</c>. It never
/// goes through a reparse point and reads no file, so the plugin still opens nothing.
/// </para>
/// </remarks>
internal static class HelmFolderEntries
{
    /// <summary>The directory entries of the chart folder <paramref name="folder"/>, in ordinal order.</summary>
    public static IReadOnlyList<string> Of(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        var entries = new List<string>();
        try
        {
            if (Folder(IoPath.Combine(folder, "crds")))
            {
                entries.Add("crds/");
            }

            foreach (var subchart in Directories(IoPath.Combine(folder, "charts")))
            {
                entries.Add($"charts/{subchart.Name}/");
                foreach (var deeper in Directories(IoPath.Combine(subchart.FullName, "charts")))
                {
                    entries.Add($"charts/{subchart.Name}/charts/{deeper.Name}/");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Removed or closed off while listed: the change that did it is notified, and read after it settles.
        }

        return [.. entries.Order(StringComparer.Ordinal)];
    }

    /// <summary>Whether <paramref name="path"/> is a directory that is not a reparse point.</summary>
    private static bool Folder(string path)
    {
        var directory = new DirectoryInfo(path);
        return directory.Exists && !directory.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    /// <summary>The directories directly in <paramref name="path"/>, never a reparse point; none when it is not a directory one may enter.</summary>
    private static IEnumerable<DirectoryInfo> Directories(string path) =>
        Folder(path)
            ? new DirectoryInfo(path).EnumerateDirectories().Where(directory => !directory.Attributes.HasFlag(FileAttributes.ReparsePoint)).OrderBy(directory => directory.Name, StringComparer.Ordinal).ToList()
            : [];
}
