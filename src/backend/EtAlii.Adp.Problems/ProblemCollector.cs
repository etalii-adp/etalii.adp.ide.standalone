using EtAlii.Adp.Common;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Problems;

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
        // Through ProblemStamp rather than FileInfo directly: a rule may locate a problem at a
        // folder - ansible.empty-role blames a role folder because it has no file worth
        // blaming - and FileInfo.Exists is false for one, which stamped such a problem as
        // default and made it read stale from the moment it was found.
        var (lastWriteTimeUtc, length) = ProblemStamp.Of(statsPath);
        _problems.Add(new StoredProblem(problem, IoPath.GetRelativePath(Root, attributionPath), lastWriteTimeUtc, length, rulesVersion));
    }

    /// <summary>
    /// Adds a core problem to the problem collector. Core problems are identified
    /// by their lack of a specific rules version, using a default rule ID that
    /// indicates the issue stems from core validation logic.
    /// </summary>
    /// <param name="path">
    /// The file or resource path associated with the problem. This is the target
    /// location for which the problem should be recorded.
    /// </param>
    /// <param name="message">
    /// The description of the problem, detailing what the issue is.
    /// </param>
    /// <param name="ruleId">
    /// The identifier for the rule that was violated. Defaults to a core rule ID
    /// that represents unreadable issues.
    /// </param>
    /// <param name="rulesVersion">
    /// The module's rules version involved in the problem. Core problems typically
    /// leave this empty because they are not tied to module rules.
    /// </param>
    public void AddCore(string path, string message, string ruleId = CoreRuleIds.Unreadable, string rulesVersion = "") =>
        Add(new DiagramProblem(DiagramProblemSeverity.Error, message, ruleId), path, path, rulesVersion);
}
