using EtAlii.Adp.Diagram;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Problems;

/// <summary>One run's growing answer: the problems, the counters and the two once-only guards.</summary>
internal sealed class ProblemCollector(string root)
{
    private readonly List<StoredProblem> _problems = [];
    private readonly HashSet<string> _considered = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _visitedFolders = new(StringComparer.OrdinalIgnoreCase);

    public string Root { get; } = root;
    public IReadOnlyList<StoredProblem> Problems => _problems;
    public int FilesConsidered { get; set; }
    public int Skipped { get; set; }

    public bool MarkConsidered(string path) => _considered.Add(IoPath.GetFullPath(path));

    public bool MarkFolderVisited(string folder)
    {
        var info = new DirectoryInfo(folder);
        var canonical = (info.Attributes & FileAttributes.ReparsePoint) != 0
            ? info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? folder
            : folder;
        return _visitedFolders.Add(IoPath.GetFullPath(canonical));
    }

    public void Add(DiagramProblem problem, string attributionPath, string statsPath, string rulesVersion)
    {
        DateTime lastWriteTimeUtc;
        long length;
        try
        {
            var info = new FileInfo(statsPath);
            lastWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : default;
            length = info.Exists ? info.Length : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            lastWriteTimeUtc = default;
            length = 0;
        }
        _problems.Add(new StoredProblem(problem, IoPath.GetRelativePath(Root, attributionPath), lastWriteTimeUtc, length, rulesVersion));
    }

    /// <summary>A problem core itself found - a rules version of its own would say nothing, so it stays empty.</summary>
    public void AddCore(string path, string message, string ruleId = "core.unreadable") =>
        Add(new DiagramProblem(DiagramProblemSeverity.Error, message, ruleId), path, path, rulesVersion: "");
}
