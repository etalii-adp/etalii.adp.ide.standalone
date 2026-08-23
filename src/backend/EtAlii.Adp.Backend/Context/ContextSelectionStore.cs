using System.Collections.Concurrent;
using System.Threading.Channels;
using Serilog;

namespace EtAlii.Adp.Backend.Context;

/// <inheritdoc cref="IContextSelectionStore" />
public sealed class ContextSelectionStore : IContextSelectionStore, IDisposable
{
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    private static readonly ILogger _logger = Log.ForContext<ContextSelectionStore>();

    private readonly TimeSpan _idleTimeout;
    private readonly ConcurrentDictionary<ShortGuid, Entry> _entries = new();

    public ContextSelectionStore(TimeSpan? idleTimeout = null)
    {
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
    }

    public void Register(
        ShortGuid watchId,
        string rootPath,
        ChannelWriter<ContextMessage> writer,
        IReadOnlyList<ContextActionGroupDefinition> rootActions,
        IReadOnlyList<ContextActionGroupDefinition> projectActions)
    {
        var entry = GetOrCreate(watchId);
        lock (entry.Gate)
        {
            entry.IdleTimer?.Dispose();
            entry.IdleTimer = null;
            entry.Writer = writer;
            entry.RootPath = rootPath;
            entry.RootActions = rootActions;
            entry.ProjectActions = projectActions;
            // The baseline: the selection (or the root's actions when nothing is selected), and
            // the project's own actions, so a connection is fully current the moment it registers.
            writer.TryWrite(ContextMessageMapper.ToMessage(entry.Record, entry.RootActions));
            writer.TryWrite(ContextMessageMapper.ToProjectActionsMessage(projectActions));
        }
    }

    public void PushProjectActions(string rootPath, IReadOnlyList<ContextActionGroupDefinition> actions)
    {
        var message = ContextMessageMapper.ToProjectActionsMessage(actions);
        foreach (var entry in _entries.Values)
        {
            lock (entry.Gate)
            {
                if (entry.Writer is null || !string.Equals(entry.RootPath, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entry.ProjectActions = actions;
                entry.Writer.TryWrite(message);
            }
        }
    }

    public void Remove(ShortGuid watchId)
    {
        if (!_entries.TryRemove(watchId, out var entry))
        {
            return;
        }

        lock (entry.Gate)
        {
            DisposeTracks(entry.Record);
            entry.IdleTimer?.Dispose();
            entry.Writer = null;
            entry.Record = null;
        }
    }

    public ContextSelectionRecord? Get(ShortGuid watchId) =>
        _entries.TryGetValue(watchId, out var entry) ? entry.Record : null;

    public void Set(ShortGuid watchId, string rootPath, ContextSelectionRecord record, ContextRediscovery rediscover)
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
                tracks.Add(level.Resolver.Track(watchId, rootPath, level, path => UpdateFromTrack(watchId, levelIndex, path)));
            }

            entry.Record = record with { Tracks = tracks };
            entry.Rediscover = rediscover;
            entry.Writer?.TryWrite(ContextMessageMapper.ToMessage(entry.Record));
        }
    }

    public void Clear(ShortGuid watchId)
    {
        var entry = GetOrCreate(watchId);
        lock (entry.Gate)
        {
            DisposeTracks(entry.Record);
            entry.Record = null;
            entry.Writer?.TryWrite(ContextMessageMapper.ToMessage(null, entry.RootActions));
        }
    }

    public void PushTransient(ShortGuid watchId, ContextSelectionRecord record)
    {
        if (!_entries.TryGetValue(watchId, out var entry))
        {
            return;
        }

        lock (entry.Gate)
        {
            entry.Writer?.TryWrite(ContextMessageMapper.ToMessage(record, transient: true));
        }
    }

    public void UpdateFromTrack(ShortGuid watchId, int levelIndex, IReadOnlyList<string>? newRelativePath)
    {
        if (newRelativePath is null)
        {
            Clear(watchId);
            return;
        }

        if (!_entries.TryGetValue(watchId, out var entry))
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
        _ = RediscoverAndPushAsync(entry, rewritten, rediscover);
    }

    public void Dispose()
    {
        foreach (var watchId in _entries.Keys.ToList())
        {
            Remove(watchId);
        }
    }

    private static async Task RediscoverAndPushAsync(Entry entry, ContextSelectionRecord rewritten, ContextRediscovery? rediscover)
    {
        var updated = rewritten;
        if (rediscover is not null)
        {
            try
            {
                updated = await rediscover(rewritten, CancellationToken.None);
            }
            catch (Exception exception)
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
            entry.Writer?.TryWrite(ContextMessageMapper.ToMessage(entry.Record));
        }
    }

    private static ContextSelectionRecord Rewrite(ContextSelectionRecord record, int levelIndex, IReadOnlyList<string> newRelativePath)
    {
        var levels = record.Levels.ToList();
        var level = levels[levelIndex];
        // The target moves with the path: providers re-discovering actions must look at
        // where the thing is now, not where it was when selected.
        var target = level.Target with { ResolvedFullPath = Relocate(level.Target.ResolvedFullPath, level.RelativePath.Count, newRelativePath) };
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

    private Entry GetOrCreate(ShortGuid watchId) => _entries.GetOrAdd(watchId, CreateEntry);

    private Entry CreateEntry(ShortGuid watchId)
    {
        // A selection recorded before any stream opens must not outlive a client that
        // never comes back - the same eviction HierarchyModelStore applies to a model
        // that ListEntries created but no WatchHierarchy ever claimed.
        return new Entry
        {
            IdleTimer = new Timer(_ => EvictIfIdle(watchId), null, _idleTimeout, Timeout.InfiniteTimeSpan),
        };
    }

    private void EvictIfIdle(ShortGuid watchId)
    {
        if (_entries.TryGetValue(watchId, out var entry) && entry.Writer is null)
        {
            Remove(watchId);
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

    private sealed class Entry
    {
        public object Gate { get; } = new();
        public ChannelWriter<ContextMessage>? Writer { get; set; }
        public ContextSelectionRecord? Record { get; set; }
        public ContextRediscovery? Rediscover { get; set; }
        public Timer? IdleTimer { get; set; }

        /// <summary>
        /// The project root's actions, given on Register and carried on every "nothing
        /// selected" message. Computed once per Watch: they depend only on the root existing
        /// and on the discovered diagram types, both stable for a connection's lifetime.
        /// </summary>
        public IReadOnlyList<ContextActionGroupDefinition>? RootActions { get; set; }

        /// <summary>The project this connection belongs to; a project-actions push reaches only matching entries.</summary>
        public string RootPath { get; set; } = "";

        /// <summary>The project actions last sent, held so a re-registration re-sends them.</summary>
        public IReadOnlyList<ContextActionGroupDefinition>? ProjectActions { get; set; }
    }
}
