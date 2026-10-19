using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Problems;

/// <summary>
/// What is known of each project's problems: in memory per root, persisted as one JSON file
/// per project at {appDataRoot}/EtAlii.Adp/problems/{hash-of-root}.json - outside the project
/// folder, which validation never writes into (Requirement 4.5). Written debounced after a
/// change, read on first touch; a missing, unreadable or version-mismatched cache yields an
/// empty <see cref="ProjectProblemSetState.NeverValidated"/> set rather than an error into
/// project opening (Requirement 4.6).
/// </summary>
internal sealed class ProblemCache : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<ProblemCache>();

    /// <summary>The on-disk format; a cache written by any other is ignored, not migrated.</summary>
    private const int CacheFormatVersion = 1;

    private static readonly TimeSpan DefaultWriteDelay = TimeSpan.FromSeconds(1);

    private readonly string _appDataRoot;
    private readonly Debouncer<CachedProjectProblems>? _writes;
    private readonly ConcurrentDictionary<ProjectRoot, CachedProjectProblems> _entries = new();

    /// <summary>
    /// Set before <see cref="Dispose"/> flushes, so a mutation arriving during or after the
    /// shutdown schedules nothing. Volatile because the mutation arrives on whichever thread
    /// the late work finished on, and this is the only thing that stops it.
    /// </summary>
    private volatile bool _disposed;

    public ProblemCache(string appDataRoot, TimeProvider time, TimeSpan? writeDelay = null)
    {
        _appDataRoot = appDataRoot;
        var delay = writeDelay ?? DefaultWriteDelay;
        _writes = delay > TimeSpan.Zero ? new Debouncer<CachedProjectProblems>(delay, OnDebounceElapsed, time) : null;
    }

    /// <summary>
    /// The entry a root's problems live in, loaded from its cache file on first touch. Read and
    /// changed under its <see cref="CachedProjectProblems.Gate"/>.
    /// </summary>
    public CachedProjectProblems EntryFor(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return _entries.GetOrAdd(ProjectRoot.From(rootPath), root => Load(root.FullPath));
    }

    /// <summary>Applies <paramref name="mutation"/> to a root's entry under its gate and schedules the write it owes.</summary>
    public CachedProjectProblems Change(string rootPath, Action<CachedProjectProblems> mutation)
    {
        var entry = EntryFor(rootPath);
        lock (entry.Gate)
        {
            mutation(entry);
            ScheduleWrite(entry);
        }
        return entry;
    }

    public IReadOnlyList<string> KnownRoots()
    {
        var roots = new HashSet<ProjectRoot>(_entries.Keys);

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
                        roots.Add(ProjectRoot.From(cache.RootPath));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    _logger.Warning(exception, "Skipping the problem cache at {CachePath}: unreadable", cachePath);
                }
            }
        }

        return roots.Select(root => root.FullPath).Order(StringComparer.OrdinalIgnoreCase).ToArray();
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
        _writes?.Dispose();
        foreach (var entry in _entries.Values)
        {
            lock (entry.Gate)
            {
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

        if (_writes is null)
        {
            entry.WritePending = true;
            Persist(entry);
            return;
        }

        // Debounced: rapid mutations cost one write, a little later. The debt is recorded on the
        // entry rather than implied by the timer, because the timer is cleared by whoever wakes up
        // first and the debt has to outlive that.
        entry.WritePending = true;
        _writes.Schedule(entry);
    }

    /// <summary>
    /// The debounced write, waking up. <b>A callback already in flight is not cancelled by
    /// disposing the debouncer</b>, so this can run after <see cref="Dispose"/> has
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
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - Reason: JsonSerializer does not honour the record's non-nullable annotations, so a cache file of another shape - or one holding "Problems": null - deserialises with Problems null, and this check turns that into an ignored cache rather than a NullReferenceException.
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
