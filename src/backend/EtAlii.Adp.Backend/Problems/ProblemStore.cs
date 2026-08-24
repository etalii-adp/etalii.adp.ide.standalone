using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;

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
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

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
                TruncatedAt: entry.Problems.Count > _maxReported ? _maxReported : 0);
        }
    }

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
                    var cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(cachePath));
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

    private void Mutate(string rootPath, Action<Entry> mutation)
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
        FileInfo info;
        try
        {
            info = new FileInfo(IoPath.Combine(rootPath, problem.RelativePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }

        if (!info.Exists || info.LastWriteTimeUtc != problem.LastWriteTimeUtc || info.Length != problem.Length)
        {
            return true;
        }

        if (problem.RulesVersion.Length == 0)
        {
            return false; // A core verdict: core is always its own current version.
        }

        // A module release invalidates its own verdicts (Requirement 4.7): compare against
        // the rules that would judge the file today.
        return _router.Route(info.FullName) is DiagramRouting.Routed routed
               && !string.Equals(_validators.RulesVersion(routed.Definition.Origin), problem.RulesVersion, StringComparison.Ordinal);
    }

    private Entry GetOrLoad(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var key = IoPath.GetFullPath(rootPath);
        return _entries.GetOrAdd(key, Load);
    }

    private void ScheduleWrite(Entry entry)
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

    private Entry Load(string rootPath)
    {
        var entry = new Entry(rootPath);
        var cachePath = CacheFilePath(rootPath);
        if (!File.Exists(cachePath))
        {
            return entry; // Never validated - ordinary for a fresh project.
        }

        try
        {
            var cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(cachePath));
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

    private void Persist(Entry entry)
    {
        var cachePath = CacheFilePath(entry.RootPath);
        try
        {
            Directory.CreateDirectory(IoPath.GetDirectoryName(cachePath)!);
            var cache = new CacheFile(CacheFormatVersion, entry.RootPath, entry.Problems.Select(CachedProblem.From).ToArray());
            File.WriteAllText(cachePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
            _logger.Debug("Wrote {Count} problems for {RootPath} to {CachePath}", entry.Problems.Count, entry.RootPath, cachePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The memory copy stands; the next mutation tries again.
            _logger.Warning(exception, "Could not write the problem cache at {CachePath}", cachePath);
        }
    }

    private sealed class Entry(string rootPath)
    {
        public object Gate { get; } = new();
        public string RootPath { get; } = rootPath;
        public List<StoredProblem> Problems { get; } = [];
        public ProjectProblemSetState State { get; set; } = ProjectProblemSetState.NeverValidated;
        public Timer? WriteTimer { get; set; }
    }

    /// <summary>The cache's own shape - flat, so the abstract location needs no JSON polymorphism.</summary>
    private sealed record CacheFile(int Version, string RootPath, IReadOnlyList<CachedProblem> Problems);

    private sealed record CachedProblem(
        DiagramProblemSeverity Severity,
        string Message,
        string RuleId,
        string? ElementId,
        uint? Line,
        string RelativePath,
        DateTime LastWriteTimeUtc,
        long Length,
        string RulesVersion)
    {
        public static CachedProblem From(StoredProblem stored) => new(
            stored.Problem.Severity,
            stored.Problem.Message,
            stored.Problem.RuleId,
            (stored.Problem.Location as DiagramProblemLocation.ElementId)?.Id,
            (stored.Problem.Location as DiagramProblemLocation.Line)?.Number,
            stored.RelativePath,
            stored.LastWriteTimeUtc,
            stored.Length,
            stored.RulesVersion);

        public static StoredProblem ToStored(CachedProblem cached)
        {
            DiagramProblemLocation? location = cached.ElementId is not null
                ? new DiagramProblemLocation.ElementId(cached.ElementId)
                : cached.Line is { } line ? new DiagramProblemLocation.Line(line) : null;
            return new StoredProblem(
                new DiagramProblem(cached.Severity, cached.Message, cached.RuleId, location),
                cached.RelativePath,
                cached.LastWriteTimeUtc,
                cached.Length,
                cached.RulesVersion);
        }
    }
}
