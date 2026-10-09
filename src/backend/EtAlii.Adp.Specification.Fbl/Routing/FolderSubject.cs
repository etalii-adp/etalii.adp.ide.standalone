using EtAlii.Adp.Specification.Fbl.Documents;

namespace EtAlii.Adp.Specification.Fbl.Routing;

/// <summary>A file of a folder subject (FBL §10.2): its <c>/</c>-separated path relative to the folder and the file rule that selected it.</summary>
public sealed record FolderFile(string RelativePath, string FullPath, FileRule Rule);

/// <summary>
/// Folder subjects (FBL §10): whether a folder qualifies by <c>recognise</c>, and which of its files
/// the file rules select, in ordinal order of relative path. A symbolic link or other reparse point
/// is neither followed nor read.
/// </summary>
public static class FolderSubject
{
    /// <summary>
    /// Whether <paramref name="folder"/> qualifies (FBL §10.1): every <c>all</c> glob matches an entry,
    /// at least one <c>any</c> glob does when there are any, and no <c>none</c> glob does.
    /// </summary>
    public static bool Recognise(FblBinding binding, string folder, bool? ignoreCase = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!binding.Body.IsFolder || !Directory.Exists(folder)) return false;
        var caseless = ignoreCase ?? Glob.PlatformIgnoresCase;
        var entries = Entries(folder, includeDirectories: true).Select(e => e.Relative).ToList();
        return binding.Body.RecogniseAll.All(Any)
               && (binding.Body.RecogniseAny.Count == 0 || binding.Body.RecogniseAny.Any(Any))
               && !binding.Body.RecogniseNone.Any(Any);
        bool Any(string glob) => entries.Any(e => Glob.IsMatch(glob, e, caseless));
    }

    /// <summary>The files the binding's file rules select (FBL §10.2), first matching rule each, <c>ignore</c> excluded.</summary>
    public static IReadOnlyList<FolderFile> Files(FblBinding binding, string folder, bool? ignoreCase = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var caseless = ignoreCase ?? Glob.PlatformIgnoresCase;
        var files = new List<FolderFile>();
        foreach ((string relative, string full) in Entries(folder, includeDirectories: false))
        {
            if (binding.Body.Ignore.Any(glob => Glob.IsMatch(glob, relative, caseless))) continue;
            var rule = binding.Body.Files.FirstOrDefault(r => Glob.IsMatch(r.Glob, relative, caseless));
            if (rule is not null) files.Add(new FolderFile(relative, full, rule));
        }
        return files;
    }

    /// <summary>Every entry under the folder, relative and <c>/</c>-separated, in ordinal order, never through a reparse point.</summary>
    private static IEnumerable<(string Relative, string Full)> Entries(string folder, bool includeDirectories)
    {
        var found = new List<(string, string)>();
        Walk(folder, "");
        return found.OrderBy(e => e.Item1, StringComparer.Ordinal);

        void Walk(string directory, string prefix)
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var relative = prefix + entry.Name;
                if (entry is DirectoryInfo)
                {
                    if (includeDirectories) found.Add((relative, entry.FullName));
                    Walk(entry.FullName, relative + "/");
                }
                else
                {
                    found.Add((relative, entry.FullName));
                }
            }
        }
    }
}
