using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Fbl.Routing;
using Serilog;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// What the plugin learns from the registered folder itself, because FBL's contract hands a plugin files
/// and nothing else (ansible-structure-disl-fbl, Requirement 4.3): whether a folder is there, which
/// folders a folder holds, how many files a folder holds, and the files that are links.
/// </summary>
/// <remarks>
/// <para>
/// <b>A stand-in for two language gaps and one behaviour kept on purpose</b> (gaps 1 and 2 of the
/// definition's companion, <c>definition/ansible-structure.md</c>; findings B2, B3 and B11). A role is a
/// folder that may hold no file, a role's <c>meta/</c> counts by being there, and a role's templates and
/// files are counted and never opened; none of that reaches a plugin that is handed files only. A file
/// that is a link is read today and is never selected by the folder subject, which follows no link.
/// </para>
/// <para>
/// <b>It counts without reading and never opens a file for writing.</b> The only bytes it reads are a
/// linked file's, through <see cref="SharedDocumentReader"/>, at the sharing every reader in the
/// repository uses.
/// </para>
/// <para>
/// <b>It follows a link where the reader always did and nowhere else.</b> A folder named by the layout
/// (<c>playbooks</c>, <c>roles</c>, a role's <c>tasks</c>) is entered whether or not it is a link; a folder
/// found by listing (a role, an environment) is skipped when it is one, so a link loop ends the listing
/// rather than the process. It holds the folder's path and nothing it read: every answer is the folder's
/// as it is when asked.
/// </para>
/// </remarks>
public sealed class AnsibleFolderListing
{
    private static readonly ILogger _logger = Log.ForContext<AnsibleFolderListing>();

    /// <summary>A listing of <paramref name="folder"/>, which need not exist.</summary>
    public AnsibleFolderListing(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Root = IoPath.GetFullPath(folder);
    }

    /// <summary>The registered folder's full path.</summary>
    public string Root { get; }

    /// <summary>Whether the folder at <paramref name="relative"/> (<c>/</c>-separated, empty for the root) is there.</summary>
    public bool FolderExists(string relative) => Directory.Exists(Full(relative));

    /// <summary>Whether a file is at <paramref name="relative"/>, a linked one included. Nothing is opened.</summary>
    public bool FileExists(string relative) => File.Exists(Full(relative));

    /// <summary>
    /// The names of the folders directly in the folder at <paramref name="relative"/>, in ordinal order,
    /// without those that are links; none when it is not there or cannot be listed.
    /// </summary>
    public IReadOnlyList<string> Folders(string relative)
    {
        var folder = Full(relative);
        if (!Directory.Exists(folder)) return [];
        try
        {
            return
            [
                .. new DirectoryInfo(folder).EnumerateDirectories()
                    .Where(child => (child.Attributes & FileAttributes.ReparsePoint) == 0)
                    .Select(child => child.Name)
                    .Order(StringComparer.Ordinal),
            ];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Debug(exception, "Skipping {Folder}: it could not be listed", folder);
            return [];
        }
    }

    /// <summary>How many files the folder at <paramref name="relative"/> holds directly, linked ones included; 0 when it is not there or cannot be listed.</summary>
    public int FileCount(string relative)
    {
        var folder = Full(relative);
        if (!Directory.Exists(folder)) return 0;
        try
        {
            return Directory.EnumerateFiles(folder).Count();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Debug(exception, "Skipping {Folder}: it could not be listed", folder);
            return 0;
        }
    }

    /// <summary>
    /// The files <paramref name="binding"/>'s file rules select that the folder subject does not, because
    /// the file or a folder on its way is a link, with their bytes, in ordinal order of their relative paths.
    /// A link is followed where a rule names a folder and not where a rule's <c>*</c> stands for one.
    /// </summary>
    public IReadOnlyList<PluginFile> LinkedFiles(FblBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!Directory.Exists(Root)) return [];

        var caseless = Glob.PlatformIgnoresCase;
        var found = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var rule in binding.Body.Files)
        {
            foreach ((string relative, string full, bool linked) in Matches(rule.Glob.Split('/'), 0, Root, "", false))
            {
                if (!linked || binding.Body.Ignore.Any(glob => Glob.IsMatch(glob, relative, caseless))) continue;
                found.TryAdd(relative, full);
            }
        }

        var files = new List<PluginFile>();
        foreach ((string relative, string full) in found)
        {
            try
            {
                files.Add(new PluginFile(relative, SharedDocumentReader.ReadAllBytes(full)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Debug(exception, "Skipping {Path}: the link could not be read", relative);
            }
        }
        return files;
    }

    /// <summary>The files below <paramref name="directory"/> that the glob segments from <paramref name="at"/> on match, and whether a link lies on the way.</summary>
    private static IEnumerable<(string Relative, string Full, bool Linked)> Matches(string[] segments, int at, string directory, string prefix, bool linked)
    {
        var segment = segments[at];
        var last = at == segments.Length - 1;
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = IsLiteral(segment)
                ? Named(directory, segment, last)
                : [.. new DirectoryInfo(directory).EnumerateFileSystemInfos().Where(entry => Glob.IsMatch(segment, entry.Name, Glob.PlatformIgnoresCase))];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Debug(exception, "Skipping {Folder}: it could not be listed", directory);
            yield break;
        }

        foreach (var entry in entries.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            var isLink = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
            var relative = prefix + entry.Name;
            if (last)
            {
                // A file as a listing of its folder reports it: a link to a folder is a folder.
                if (!Directory.Exists(entry.FullName)) yield return (relative, entry.FullName, linked || isLink);
                continue;
            }

            if (!Directory.Exists(entry.FullName) || (isLink && !IsLiteral(segment))) continue;
            foreach (var match in Matches(segments, at + 1, entry.FullName, relative + "/", linked || isLink))
            {
                yield return match;
            }
        }
    }

    /// <summary>The entry a literal segment names, when it is there as what the rule needs at that place.</summary>
    private static IEnumerable<FileSystemInfo> Named(string directory, string name, bool last)
    {
        var path = IoPath.Combine(directory, name);
        if (last && File.Exists(path)) return [new FileInfo(path)];
        if (!last && Directory.Exists(path)) return [new DirectoryInfo(path)];
        return [];
    }

    private static bool IsLiteral(string segment) => segment.IndexOfAny(['*', '?', '[']) < 0;

    private string Full(string relative) =>
        relative.Length == 0 ? Root : IoPath.Combine(Root, relative.Replace('/', IoPath.DirectorySeparatorChar));
}
