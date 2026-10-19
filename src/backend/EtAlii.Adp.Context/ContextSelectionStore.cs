using System.Threading.Channels;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using Serilog;
using Path = EtAlii.Adp.Documents.Wire.Path;
namespace EtAlii.Adp.Context;

/// <inheritdoc cref="IContextSelectionStore" />
public sealed class ContextSelectionStore : IContextSelectionStore, IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    private static readonly ILogger _logger = Log.ForContext<ContextSelectionStore>();

    // A selection recorded before any stream opens must not outlive a client that never comes
    // back - the same eviction HierarchyModelStore applies to a model that ListEntries created
    // but no WatchHierarchy ever claimed. Registering the stream claims it.
    private readonly RetainedRegistry<ShortGuid, ContextSelectionStoreEntry> _entries;

    // The re-discoveries a moved or refreshed selection starts off the caller's thread, waited for
    // when the store closes (code-structure FR-070).
    private readonly RunningTasks _rediscoveries = new();

    public ContextSelectionStore(TimeSpan? idleTimeout = null, TimeProvider? time = null)
    {
        _entries = new RetainedRegistry<ShortGuid, ContextSelectionStoreEntry>(
            idleTimeout ?? DefaultIdleTimeout,
            (_, entry) => Drop(entry),
            time ?? TimeProvider.System,
            dropUnclaimed: true);
    }

    public IDisposable Register(
        ShortGuid watchId,
        ProjectRoot root,
        ChannelWriter<ContextMessage> writer,
        IReadOnlyList<ContextActionGroupDefinition> rootActions,
        IReadOnlyList<ContextActionGroupDefinition> projectActions,
        Func<ProjectProblems> problems)
    {
        var entry = _entries.Retain(watchId, CreateEntry);
        lock (entry.Gate)
        {
            entry.Writer = writer;
            entry.Root = root;
            entry.RootActions = rootActions;
            // The baseline: the selection (or the root's actions when nothing is selected), the
            // project's own actions, and the project's problems, so a connection is fully
            // current the moment it registers. The problems are read here, with the writer
            // already set and the gate held: a set that changes before this read is in the
            // baseline, and the push of one that changes after it waits for the gate and lands
            // behind it. Read before registering, a validation finishing in between pushed to a
            // connection that was not there yet, and the baseline went stale for good (T292).
            // A set that changes before the read is pushed after it all the same, and is not sent
            // twice: the push finds the connection already has it (T300).
            writer.TryWrite(ContextSelectionRecord.ToWire(entry.Record, entry.RootActions));
            writer.TryWrite(ContextActionGroups.ToProto(projectActions));
            entry.LastProblems = problems();
            writer.TryWrite(new ContextMessage { Problems = entry.LastProblems });
        }

        return Disposable.From(() => Remove(watchId));
    }

    public void PushProjectActions(ProjectRoot root, IReadOnlyList<ContextActionGroupDefinition> actions)
    {
        var message = ContextActionGroups.ToProto(actions);
        foreach (var entry in _entries.Values())
        {
            lock (entry.Gate)
            {
                if (entry.Writer is null || entry.Root != root)
                {
                    continue;
                }

                entry.Writer.TryWrite(message);
            }
        }
    }

    public void PushProblems(ProjectRoot root, ProjectProblems problems)
    {
        var message = new ContextMessage { Problems = problems };
        foreach (var entry in _entries.Values())
        {
            lock (entry.Gate)
            {
                if (entry.Writer is null || entry.Root != root || problems.Equals(entry.LastProblems))
                {
                    continue;
                }

                // The same message instance for everyone: what one connection reads, all read.
                entry.LastProblems = problems;
                entry.Writer.TryWrite(message);
            }
        }
    }

    public void PushNotice(ProjectRoot root, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var notice = new ContextMessage { Notice = new ContextNotice { Message = message } };
        foreach (var entry in _entries.Values())
        {
            lock (entry.Gate)
            {
                if (entry.Writer is null || entry.Root != root)
                {
                    continue;
                }

                entry.Writer.TryWrite(notice);
            }
        }
    }

    private void Remove(ShortGuid watchId) => _entries.Remove(watchId);

    public ContextSelectionRecord? Get(ShortGuid watchId) =>
        _entries.TryGet(watchId, out var entry) ? entry.Record : null;

    public void Set(ShortGuid watchId, ProjectRoot root, ContextSelectionRecord record, ContextRediscovery rediscover)
    {
        var entry = GetOrCreate(watchId);
        lock (entry.Gate)
        {
            DisposeTracks(entry.Record);

            var tracks = new List<IDisposable>(record.Levels.Count);
            for (var index = 0; index < record.Levels.Count; index++)
            {
                var levelIndex = index;
                var level = record.Levels[index];
                tracks.Add(level.Resolver.Track(watchId, root, level, path => UpdateFromTrack(watchId, levelIndex, path)));
            }

            entry.Record = record with { Tracks = tracks };
            entry.Rediscover = rediscover;
            entry.Writer?.TryWrite(ContextSelectionRecord.ToWire(entry.Record));
        }
    }

    public void Clear(ShortGuid watchId)
    {
        var entry = GetOrCreate(watchId);
        lock (entry.Gate)
        {
            DisposeTracks(entry.Record);
            entry.Record = null;
            entry.Writer?.TryWrite(ContextSelectionRecord.ToWire(null, entry.RootActions));
        }
    }

    public void Refresh(ShortGuid watchId)
    {
        if (!_entries.TryGet(watchId, out var entry))
        {
            return;
        }

        ContextSelectionRecord? record;
        ContextRediscovery? rediscover;
        lock (entry.Gate)
        {
            record = entry.Record;
            rediscover = entry.Rediscover;
        }

        if (record is null || rediscover is null)
        {
            return;
        }

        // The same off-thread re-derivation a moved selection gets; a selection replaced in
        // the meantime supersedes this push inside RediscoverAndPushAsync.
        _rediscoveries.Start(cancellationToken => RediscoverAndPushAsync(entry, record, rediscover, cancellationToken));
    }

    public void PushTransient(ShortGuid watchId, ContextSelectionRecord record)
    {
        if (!_entries.TryGet(watchId, out var entry))
        {
            return;
        }

        lock (entry.Gate)
        {
            entry.Writer?.TryWrite(ContextSelectionRecord.ToWire(record, transient: true));
        }
    }

    private void UpdateFromTrack(ShortGuid watchId, int levelIndex, IReadOnlyList<string>? newRelativePath)
    {
        if (newRelativePath is null)
        {
            Clear(watchId);
            return;
        }

        if (!_entries.TryGet(watchId, out var entry))
        {
            return;
        }

        ContextSelectionRecord rewritten;
        ContextRediscovery? rediscover;
        lock (entry.Gate)
        {
            if (entry.Record is null || levelIndex >= entry.Record.Levels.Count)
            {
                return;
            }

            rewritten = Rewrite(entry.Record, levelIndex, newRelativePath);
            entry.Record = rewritten;
            rediscover = entry.Rediscover;
        }

        // Actions may well differ at the new location; re-derive them off the event
        // thread, then push whichever record is current once that finishes.
        _rediscoveries.Start(cancellationToken => RediscoverAndPushAsync(entry, rewritten, rediscover, cancellationToken));
    }

    /// <summary>Cancels the re-discoveries under way without waiting for them, and drops every selection.</summary>
    public void Dispose()
    {
        _rediscoveries.Dispose();
        _entries.Dispose();
    }

    /// <summary>Cancels the re-discoveries under way and waits until they have returned, then drops every selection.</summary>
    public async ValueTask DisposeAsync()
    {
        await _rediscoveries.StopAsync();
        Dispose();
    }

    private static async Task RediscoverAndPushAsync(ContextSelectionStoreEntry entry, ContextSelectionRecord rewritten, ContextRediscovery? rediscover, CancellationToken cancellationToken)
    {
        var updated = rewritten;
        if (rediscover is not null)
        {
            try
            {
                updated = await rediscover(rewritten, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A provider failing to answer leaves the selection itself intact: it is
                // pushed with the actions it had, and the next change tries again. Swallowed
                // on purpose, so this line is the only trace it leaves.
                _logger.Warning(
                    exception,
                    "Re-discovering actions after a change failed; keeping the {GroupCount} groups already shown",
                    rewritten.Actions.Count);
            }
        }

        lock (entry.Gate)
        {
            if (!ReferenceEquals(entry.Record, rewritten))
            {
                return; // superseded while re-discovering
            }

            entry.Record = updated with { Tracks = rewritten.Tracks };
            entry.Writer?.TryWrite(ContextSelectionRecord.ToWire(entry.Record));
        }
    }

    private static ContextSelectionRecord Rewrite(ContextSelectionRecord record, int levelIndex, IReadOnlyList<string> newRelativePath)
    {
        var levels = record.Levels.ToList();
        var level = levels[levelIndex];
        // The target moves with the path: providers re-discovering actions must look at
        // where the thing is now, not where it was when selected. That holds for the levels
        // whose relative path IS a file path - a diagram element's relative path is display
        // text (a node's label, a variable's name), and its target's ResolvedFullPath is the
        // body file holding it, which a relabel does not move. Relocating it from display
        // segments turned the body path into "<folder>/<new label>", after which every
        // describe and re-resolution on the selection read a file that does not exist.
        var target = level.Target.Scope == ContextScope.DiagramElement
            ? level.Target
            : level.Target with { ResolvedFullPath = Relocate(level.Target.ResolvedFullPath, level.RelativePath.Count, newRelativePath) };
        levels[levelIndex] = level with { RelativePath = newRelativePath, Target = target };

        var chain = record.Chain.Clone();
        var cursor = chain;
        for (var index = 0; index < levelIndex; index++)
        {
            cursor = cursor.Child;
        }

        cursor.Path = new Path();
        cursor.Path.Segments.AddRange(newRelativePath);

        return record with { Chain = chain, Levels = levels };
    }

    /// <summary>
    /// The absolute location after a level's relative path changed: strip as many trailing
    /// segments as the old relative path had, then append the new ones.
    /// </summary>
    private static string Relocate(string fullPath, int oldSegmentCount, IReadOnlyList<string> newRelativePath)
    {
        var basePath = fullPath;
        for (var index = 0; index < oldSegmentCount; index++)
        {
            basePath = System.IO.Path.GetDirectoryName(basePath) ?? basePath;
        }

        return System.IO.Path.Combine([basePath, .. newRelativePath]);
    }

    private ContextSelectionStoreEntry GetOrCreate(ShortGuid watchId) => _entries.GetOrAdd(watchId, CreateEntry);

    private static ContextSelectionStoreEntry CreateEntry(ShortGuid watchId) => new();

    private static void Drop(ContextSelectionStoreEntry entry)
    {
        lock (entry.Gate)
        {
            DisposeTracks(entry.Record);
            entry.Writer = null;
            entry.Record = null;
        }
    }

    private static void DisposeTracks(ContextSelectionRecord? record)
    {
        if (record is null)
        {
            return;
        }

        foreach (var track in record.Tracks)
        {
            track.Dispose();
        }
    }
}
