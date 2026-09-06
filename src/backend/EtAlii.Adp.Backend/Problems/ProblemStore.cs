using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Problems;

/// <inheritdoc cref="IProblemStore" />
/// <remarks>
/// In memory per root, persisted as one JSON file per project at
/// {appDataRoot}/EtAlii.Adp/problems/{hash-of-root}.json - outside the project folder,
/// which validation never writes into (Requirement 4.5). Written debounced after a change,
/// read on first touch; a missing, unreadable or version-mismatched cache yields an empty
/// <see cref="ProjectProblemSetState.NeverValidated"/> set rather than an error into project
/// opening (Requirement 4.6).
/// </remarks>
public sealed class ProblemStore : IProblemStore, IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<ProblemStore>();

    /// <summary>The on-disk format; a cache written by any other is ignored, not migrated.</summary>
    private const int CacheFormatVersion = 1;

    /// <summary>How many problems one answer carries at most; the counts still cover everything (Requirement 5.6).</summary>
    private const int DefaultMaxReported = 1000;

    private static readonly TimeSpan DefaultWriteDelay = TimeSpan.FromSeconds(1);

    private readonly string _appDataRoot;
    private readonly DiagramFileRouter _router;
    private readonly DiagramValidators _validators;
    private readonly TimeSpan _writeDelay;
    private readonly int _maxReported;
    private readonly ConcurrentDictionary<string, CachedProjectProblems> _entries = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? Changed;

    public ProblemStore(
        string appDataRoot,
        DiagramFileRouter router,
        DiagramValidators validators,
        TimeSpan? writeDelay = null,
        int maxReported = DefaultMaxReported)
    {
        ArgumentNullException.ThrowIfNull(appDataRoot);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(validators);
        _appDataRoot = appDataRoot;
        _router = router;
        _validators = validators;
        _writeDelay = writeDelay ?? DefaultWriteDelay;
        _maxReported = maxReported;
    }

    public ProjectProblemSet Get(string rootPath)
    {
        var entry = GetOrLoad(rootPath);
        lock (entry.Gate)
        {
            IReadOnlyList<StoredProblem> reported = entry.Problems.Count <= _maxReported
                ? entry.Problems
                : entry.Problems.Take(_maxReported).ToArray();
            var withStaleness = reported.Select(problem => problem with { Stale = IsStale(entry.RootPath, problem) }).ToArray();
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

    public IReadOnlyList<string> KnownRoots()
    {
        var roots = new HashSet<string>(_entries.Keys, StringComparer.OrdinalIgnoreCase);

        var cacheFolder = IoPath.Combine(_appDataRoot, "EtAlii.Adp", "problems");
        if (Directory.Exists(cacheFolder))
        {
            foreach (var cachePath in Directory.GetFiles(cacheFolder, "*.json"))
            {
                try
                {
                    var cache = JsonSerializer.Deserialize<ProblemCacheFile>(File.ReadAllText(cachePath));
                    if (cache is { Version: CacheFormatVersion, RootPath.Length: > 0 })
                    {
                        roots.Add(IoPath.GetFullPath(cache.RootPath));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    _logger.Warning(exception, "Skipping the problem cache at {CachePath}: unreadable", cachePath);
                }
            }
        }

        return roots.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void Dispose()
    {
        // A pending debounced write is a promise; keep it on the way out.
        foreach (var entry in _entries.Values)
        {
            lock (entry.Gate)
            {
                if (entry.WriteTimer is null)
                {
                    continue;
                }
                entry.WriteTimer.Dispose();
                entry.WriteTimer = null;
                Persist(entry);
            }
        }
    }

    // ---- the mechanics -----------------------------------------------------------------

    private void Mutate(string rootPath, Action<CachedProjectProblems> mutation)
    {
        var entry = GetOrLoad(rootPath);
        lock (entry.Gate)
        {
            mutation(entry);
            ScheduleWrite(entry);
        }
        Changed?.Invoke(entry.RootPath);
    }

    /// <summary>Whether <paramref name="covering"/> - a file, or a folder - covers <paramref name="relativePath"/>.</summary>
    private static bool Covers(string covering, string relativePath) =>
        relativePath.Length >= covering.Length
        && relativePath.StartsWith(covering, StringComparison.OrdinalIgnoreCase)
        && (relativePath.Length == covering.Length || relativePath[covering.Length] is '\\' or '/');

    private bool IsStale(string rootPath, StoredProblem problem)
    {
        // The subject may be a folder as well as a file - a rule that blames a role folder has
        // no file to blame - and FileInfo.Exists is false for one, which made every
        // folder-located problem permanently stale (found by the ansible-structure-diagram
        // manual pass).
        var fullPath = ProblemStamp.FullPathOrNull(rootPath, problem.RelativePath);
        if (fullPath is null || !ProblemStamp.Exists(fullPath))
        {
            return true;
        }

        var (lastWriteTimeUtc, length) = ProblemStamp.Of(fullPath);
        if (lastWriteTimeUtc != problem.LastWriteTimeUtc || length != problem.Length)
        {
            return true;
        }

        if (problem.RulesVersion.Length == 0)
        {
            return false; // A core verdict: core is always its own current version.
        }

        // A module release invalidates its own verdicts (Requirement 4.7): compare against
        // the rules that would judge the file today.
        return _router.Route(fullPath) is DiagramRouted routed
               && !string.Equals(_validators.RulesVersion(routed.Definition.Origin), problem.RulesVersion, StringComparison.Ordinal);
    }

    private CachedProjectProblems GetOrLoad(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var key = IoPath.GetFullPath(rootPath);
        return _entries.GetOrAdd(key, Load);
    }

    private void ScheduleWrite(CachedProjectProblems entry)
    {
        if (_writeDelay <= TimeSpan.Zero)
        {
            Persist(entry);
            return;
        }

        // Debounced: rapid mutations cost one write, a little later.
        entry.WriteTimer?.Dispose();
        entry.WriteTimer = new Timer(_ =>
        {
            lock (entry.Gate)
            {
                entry.WriteTimer?.Dispose();
                entry.WriteTimer = null;
                Persist(entry);
            }
        }, null, _writeDelay, Timeout.InfiniteTimeSpan);
    }

    // ---- persistence -------------------------------------------------------------------

    private string CacheFilePath(string rootPath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rootPath.ToUpperInvariant())))[..32];
        return IoPath.Combine(_appDataRoot, "EtAlii.Adp", "problems", hash + ".json");
    }

    private CachedProjectProblems Load(string rootPath)
    {
        var entry = new CachedProjectProblems(rootPath);
        var cachePath = CacheFilePath(rootPath);
        if (!File.Exists(cachePath))
        {
            return entry; // Never validated - ordinary for a fresh project.
        }

        try
        {
            var cache = JsonSerializer.Deserialize<ProblemCacheFile>(File.ReadAllText(cachePath));
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cache is null || cache.Version != CacheFormatVersion || cache.Problems is null)
            {
                _logger.Warning("Ignoring the problem cache at {CachePath}: not this format", cachePath);
                return entry;
            }
            entry.Problems.AddRange(cache.Problems.Select(CachedProblem.ToStored));
            entry.State = ProjectProblemSetState.Validated;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken cache must never throw into project opening (Requirement 4.6).
            _logger.Warning(exception, "Ignoring the problem cache at {CachePath}: unreadable", cachePath);
        }
        return entry;
    }

    private void Persist(CachedProjectProblems entry)
    {
        var cachePath = CacheFilePath(entry.RootPath);
        try
        {
            Directory.CreateDirectory(IoPath.GetDirectoryName(cachePath)!);
            var cache = new ProblemCacheFile(CacheFormatVersion, entry.RootPath, entry.Problems.Select(CachedProblem.From).ToArray());
            File.WriteAllText(cachePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
            _logger.Debug("Wrote {Count} problems for {RootPath} to {CachePath}", entry.Problems.Count, entry.RootPath, cachePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The memory copy stands; the next mutation tries again.
            _logger.Warning(exception, "Could not write the problem cache at {CachePath}", cachePath);
        }
    }

}
