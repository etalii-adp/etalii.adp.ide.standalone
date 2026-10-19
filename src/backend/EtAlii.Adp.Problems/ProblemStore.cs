using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Problems;

/// <inheritdoc cref="IProblemStore" />
/// <remarks>
/// What is known lives in the <see cref="ProblemCache"/>, with its persistence; whether a
/// remembered verdict still holds is the <see cref="ProblemStalenessPolicy"/>'s to say. This
/// class answers the questions of <see cref="IProblemStore"/> from the two.
/// </remarks>
public sealed class ProblemStore : IProblemStore, IDisposable
{
    /// <summary>How many problems one answer carries at most; the counts still cover everything (Requirement 5.6).</summary>
    private const int DefaultMaxReported = 1000;

    private readonly ProblemStalenessPolicy _staleness;
    private readonly int _maxReported;

    public event Action<string>? Changed;

    public ProblemStore(
        string appDataRoot,
        DiagramFileRouter router,
        DiagramValidators validators,
        TimeProvider time,
        TimeSpan? writeDelay = null,
        int maxReported = DefaultMaxReported)
    {
        ArgumentNullException.ThrowIfNull(appDataRoot);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(validators);
        ArgumentNullException.ThrowIfNull(time);
        Cache = new ProblemCache(appDataRoot, time, writeDelay);
        _staleness = new ProblemStalenessPolicy(router, validators);
        _maxReported = maxReported;
    }

    /// <summary>
    /// What is known, and its persistence. Internal for the guards over its shutdown, which
    /// drive the debounced write through the same entry the timer would.
    /// </summary>
    internal ProblemCache Cache { get; }

    public ProjectProblemSet Get(string rootPath)
    {
        var entry = Cache.EntryFor(rootPath);
        lock (entry.Gate)
        {
            IReadOnlyList<StoredProblem> reported = entry.Problems.Count <= _maxReported
                ? entry.Problems
                : entry.Problems.Take(_maxReported).ToArray();
            var withStaleness = reported.Select(problem => problem with { Stale = _staleness.IsStale(entry.RootPath, problem) }).ToArray();
            return new ProjectProblemSet(
                entry.State,
                withStaleness,
                ErrorCount: entry.Problems.Count(problem => problem.Problem.Severity == DiagramProblemSeverity.Error),
                WarningCount: entry.Problems.Count(problem => problem.Problem.Severity == DiagramProblemSeverity.Warning),
                TruncatedAt: entry.Problems.Count > _maxReported ? _maxReported : 0,
                // Counted apart from the warnings, never added to them: an informational note
                // must not make a clean file report as having something wrong.
                InfoCount: entry.Problems.Count(problem => problem.Problem.Severity == DiagramProblemSeverity.Info));
        }
    }

    public void BeginValidating(string rootPath) =>
        Mutate(rootPath, entry => entry.State = ProjectProblemSetState.Validating);

    public void Replace(string rootPath, IReadOnlyList<StoredProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        Mutate(rootPath, entry =>
        {
            entry.Problems.Clear();
            entry.Problems.AddRange(problems);
            entry.State = ProjectProblemSetState.Validated;
        });
    }

    public void ReplaceFor(string rootPath, IReadOnlyList<string> relativePaths, IReadOnlyList<StoredProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        ArgumentNullException.ThrowIfNull(problems);
        Mutate(rootPath, entry =>
        {
            entry.Problems.RemoveAll(problem => relativePaths.Any(path => Covers(path, problem.RelativePath)));
            entry.Problems.AddRange(problems);
            entry.State = ProjectProblemSetState.Validated;
        });
    }

    public void Remove(string rootPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        Mutate(rootPath, entry => entry.Problems.RemoveAll(problem => Covers(relativePath, problem.RelativePath)));
    }

    public void Move(string rootPath, string fromRelativePath, string toRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromRelativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(toRelativePath);
        Mutate(rootPath, entry =>
        {
            for (var index = 0; index < entry.Problems.Count; index++)
            {
                var problem = entry.Problems[index];
                if (!Covers(fromRelativePath, problem.RelativePath))
                {
                    continue;
                }
                var tail = problem.RelativePath[fromRelativePath.Length..];
                entry.Problems[index] = problem with { RelativePath = toRelativePath + tail };
            }
        });
    }

    public IReadOnlyList<string> KnownRoots() => Cache.KnownRoots();

    public void Dispose() => Cache.Dispose();

    private void Mutate(string rootPath, Action<CachedProjectProblems> mutation)
    {
        var entry = Cache.Change(rootPath, mutation);
        Changed?.Invoke(entry.RootPath);
    }

    /// <summary>Whether <paramref name="covering"/> - a file, or a folder - covers <paramref name="relativePath"/>.</summary>
    private static bool Covers(string covering, string relativePath) =>
        relativePath.Length >= covering.Length
        && relativePath.StartsWith(covering, StringComparison.OrdinalIgnoreCase)
        && (relativePath.Length == covering.Length || relativePath[covering.Length] is '\\' or '/');
}
