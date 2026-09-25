using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Problems;

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

    /// <summary>
    /// Set before <see cref="Dispose"/> flushes, so a mutation arriving during or after the
    /// shutdown schedules nothing. Volatile because the mutation arrives on whichever thread
    /// the late work finished on, and this is the only thing that stops it.
    /// </summary>
    private volatile bool _disposed;

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

    /// <summary>
    /// Runs inside <see cref="Dispose"/>, after the store has closed to new schedules and before
    /// the flush loop. <b>The seam exists because the interleaving that loses a write cannot be
    /// provoked by timing</b>: a debounce callback has to wake in exactly that gap, which a real
    /// race obliges rarely enough to read as a flake - once in a full 5,098-test run, and never in
    /// five runs of the project alone. Production never sets it.
    /// </summary>
    internal Action? BetweenClosingAndFlushing { get; set; }

    public void Dispose()
    {
        // Closed to new schedules FIRST, then flushed: set afterwards, a mutation racing this
        // loop could arm a timer on an entry the loop had already passed, and that timer would
        // outlive the store with nothing to stop it.
        _disposed = true;
        BetweenClosingAndFlushing?.Invoke();

        // A pending debounced write is a promise; keep it on the way out.
        foreach (var entry in _entries.Values)
        {
            lock (entry.Gate)
            {
                entry.WriteTimer?.Dispose();
                entry.WriteTimer = null;

                // By the debt rather than by the timer: a debounce callback that woke a moment
                // before this loop has already cleared the timer, and reading that as "nothing
                // owed" is how the last write went missing.
                if (!entry.WritePending)
                {
                    continue;
                }

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

    /// <summary>
    /// Whether a remembered verdict may no longer hold.
    /// </summary>
    /// <remarks>
    /// The obvious half is the file: it changed, or it is gone. The half that cost this
    /// repository three defects in one night is the other one - <b>a verdict whose authority
    /// moved out from under it while its file stood still.</b> Three ways that happens, and
    /// no file stamp can see any of them, by construction: the registry GAINS a type and a
    /// core "not a known diagram type" goes on denying it; a module is released and its own
    /// verdicts describe rules that no longer judge the file; a module is REMOVED and its
    /// verdicts, and core's verdicts about it, have nothing left standing behind them. Each
    /// check below is one of those, and each was found by fixing the one before it. Adding a
    /// new kind of verdict means asking which authority it rests on and whether that
    /// authority can move - because if it can and nothing here asks, the panel will present
    /// the verdict as a claim about now.
    /// </remarks>
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
            // A core verdict, so there is no module version to compare - core is its own
            // current version. But two of them are claims about the diagram-type REGISTRY
            // rather than about the file, and the registry moves when a module is discovered:
            // "'dotnet/dependency-graph' is not a known diagram type" stops being true the
            // moment that module is built, while the .adp it names never changes. Neither the
            // file stamp nor the rules version can carry that news, so such a verdict outlived
            // a rebuild and a restart and was shown with the authority of a current one - it
            // cost three sessions an evening diagnosing a module that was working. The router
            // is the authority the verdict was drawn from, so ask it again.
            return CoreRuleIds.IsAboutRouting(problem.Problem.RuleId) && _router.Route(fullPath) is DiagramRouted;
        }

        // Nothing routes the file any more, so the module that judged it is gone -
        // unregistered, uninstalled, or dropped from a build. There is no version left to
        // compare and nothing standing behind the verdict, which is precisely what stale
        // means. Gating the comparison on a successful route instead left such a verdict
        // reading fresh for ever, the mirror of the case above: there the registry GAINED a
        // type and a core verdict went on denying it; here it LOSES one and a module's
        // verdict goes on asserting it. The entry is marked rather than dropped so the
        // record of a problem that was once real survives, and a module removed by accident
        // does not silently take its findings with it.
        if (_router.Route(fullPath) is not DiagramRouted routed)
        {
            return true;
        }

        // A module release invalidates its own verdicts (Requirement 4.7): compare against
        // the rules that would judge the file today.
        return !string.Equals(_validators.RulesVersion(routed.Definition.Origin), problem.RulesVersion, StringComparison.Ordinal);
    }

    /// <summary>
    /// The entry a root's problems live in. Internal for one reason: the guard over
    /// <see cref="OnDebounceElapsed"/> has to hand it the same entry the timer would, and the
    /// race it stands in for cannot be provoked in process.
    /// </summary>
    internal CachedProjectProblems EntryFor(string rootPath) => GetOrLoad(rootPath);

    private CachedProjectProblems GetOrLoad(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var key = IoPath.GetFullPath(rootPath);
        return _entries.GetOrAdd(key, Load);
    }

    private void ScheduleWrite(CachedProjectProblems entry)
    {
        // AFTER SHUTDOWN, NOTHING IS SCHEDULED. Dispose keeps the write that was already
        // pending - that promise is the point of the flush - but work finishing later has
        // nobody left to keep a promise to, and Persist creates the cache directory before
        // writing. A timer armed here after Dispose fired into a folder whose owner had
        // already taken it away: 3,692 directories under %TEMP%/EtAlii.Adp.IntegrationTests,
        // each recreated to hold one cache file, and in the running application a cache write
        // after the host has gone.
        if (_disposed)
        {
            _logger.Debug("Not scheduling a problem-cache write for {RootPath}: the store is disposed", entry.RootPath);
            return;
        }

        if (_writeDelay <= TimeSpan.Zero)
        {
            entry.WritePending = true;
            Persist(entry);
            return;
        }

        // Debounced: rapid mutations cost one write, a little later. The debt is recorded on the
        // entry rather than implied by the timer, because the timer is cleared by whoever wakes up
        // first and the debt has to outlive that.
        entry.WritePending = true;
        entry.WriteTimer?.Dispose();
        entry.WriteTimer = new Timer(_ => OnDebounceElapsed(entry), null, _writeDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// The debounced write, waking up. <b>A callback already in flight is not cancelled by
    /// <see cref="Timer.Dispose()"/></b>, so this can run after <see cref="Dispose"/> has
    /// returned - and it did: measured in a full run as <c>PERSIST disposed=True</c> landing
    /// between a test's <c>Directory.Delete</c> beginning and finishing, which leaves the folder
    /// behind. Dispose has already kept the promise for anything pending, so a callback that
    /// wakes to a disposed store writes nothing.
    /// </summary>
    /// <remarks>
    /// Its own method, and internal, because the window cannot be provoked reliably in process:
    /// a synthetic probe of 200 dispose-then-write races reproduced it 0 times while a real run
    /// hit it 14 times in 870 teardowns. A guard therefore calls this directly, which is the
    /// same code the timer calls, rather than waiting for a race to oblige.
    /// </remarks>
    internal void OnDebounceElapsed(CachedProjectProblems entry)
    {
        lock (entry.Gate)
        {
            entry.WriteTimer?.Dispose();
            entry.WriteTimer = null;
            if (_disposed && !entry.WritePending)
            {
                // SAYS WHAT WAS CHECKED, NOT WHAT IS HOPED. This line used to claim "the flush
                // has already written it" on the strength of _disposed alone - which was false in
                // exactly the interleaving that lost the write, so the one record of the defect
                // asserted the opposite of what happened. It now reports the settled debt, which is
                // the thing actually tested one line above.
                _logger.Debug(
                    "A debounced problem-cache write for {RootPath} woke after the store was disposed, with nothing left owed",
                    entry.RootPath);
                return;
            }

            // STILL OWED, EVEN IF THE STORE IS DISPOSED. Dispose sets _disposed BEFORE its flush
            // loop reaches this entry, so "disposed" on its own never meant "already written" - and
            // this callback used to clear the timer and return, after which the flush saw no timer
            // and skipped the entry too. Neither wrote, and the last change was lost. Persist still
            // refuses to recreate a root that has gone, which is what the disposed check was
            // protecting.
            Persist(entry);
        }
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

        // THE CACHE LIVES INSIDE SOMEBODY ELSE'S FOLDER, AND NEVER BRINGS IT BACK. Creating the
        // `EtAlii.Adp/problems` directories inside an existing root is an ordinary first write -
        // in the running application %APPDATA% is always there. Recreating the ROOT is a
        // different act: it resurrects a folder whose owner has deliberately removed it. A test's
        // host is disposed AFTER its temp root is deleted, so the flush on Dispose - keeping a
        // promise, correctly - wrote into folders that had already gone, 3,694 of them. Measured,
        // not deduced: `PERSIST disposed=True dirExists=False` in a five-second run of one
        // integration class, after 17761814 had closed the post-dispose schedule and the litter
        // carried on regardless.
        if (!Directory.Exists(_appDataRoot))
        {
            _logger.Debug(
                "Not writing the problem cache for {RootPath}: its app-data root {AppDataRoot} is gone",
                entry.RootPath,
                _appDataRoot);
            return;
        }

        try
        {
            Directory.CreateDirectory(IoPath.GetDirectoryName(cachePath)!);
            var cache = new ProblemCacheFile(CacheFormatVersion, entry.RootPath, entry.Problems.Select(CachedProblem.From).ToArray());
            File.WriteAllText(cachePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
            entry.WritePending = false;
            _logger.Debug("Wrote {Count} problems for {RootPath} to {CachePath}", entry.Problems.Count, entry.RootPath, cachePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The memory copy stands; the next mutation tries again.
            _logger.Warning(exception, "Could not write the problem cache at {CachePath}", cachePath);
        }
    }

}
