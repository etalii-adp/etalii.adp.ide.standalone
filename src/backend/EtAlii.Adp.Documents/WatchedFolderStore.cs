using System.Collections.Concurrent;
using Serilog;

namespace EtAlii.Adp.Documents;

/// <summary>
/// One snapshot per registered folder, kept true as the tree changes, written once (code-structure
/// FR-037): the claim count, the watcher, the settle debounce and the announcement. A module says
/// how a folder is read.
/// </summary>
/// <remarks>
/// <para>
/// <b>A settled burst re-reads the whole folder</b>, rather than surgically re-reading the files
/// that changed. That is a deliberate simplification: an incremental path would be a second way
/// to build a snapshot, able to disagree with the first, and the failure would be a diagram that
/// is subtly wrong rather than one that is obviously broken. The burst timer already collapses a
/// multi-file save into a single re-read. Where a folder ever appears for which a whole read is
/// too slow, the place to fix it is the module's reader - memoizing per-file parses behind the
/// same walk - rather than a divergent path here.
/// </para>
/// <para>
/// There is no save method, and no code path in this file opens a file for writing.
/// </para>
/// </remarks>
/// <typeparam name="TSnapshot">What one read of a folder makes; immutable, replaced wholesale.</typeparam>
/// <param name="settleDelay">How long a burst of changes may keep growing before one re-read answers it all.</param>
public abstract class WatchedFolderStore<TSnapshot>(TimeSpan settleDelay) : IWatchedFolderStore<TSnapshot>, IDisposable
    where TSnapshot : class
{
    private static readonly ILogger _logger = Log.ForContext<WatchedFolderStore<TSnapshot>>();

    private readonly ConcurrentDictionary<string, WatchedFolder<TSnapshot>> _folders = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public event EventHandler<WatchedFolderChangedEventArgs<TSnapshot>>? Changed;

    /// <summary>Reads <paramref name="folder"/>, a full path, into a snapshot.</summary>
    protected abstract TSnapshot Read(string folder);

    /// <inheritdoc />
    public TSnapshot GetOrLoad(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Watched(folder).Snapshot;
    }

    /// <inheritdoc />
    public TSnapshot? Get(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return _folders.TryGetValue(Key(folder), out var watched) ? watched.Snapshot : null;
    }

    /// <inheritdoc />
    public TSnapshot Acquire(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var watched = Watched(folder);
        watched.Claim();
        return watched.Snapshot;
    }

    /// <inheritdoc />
    public void Release(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var key = Key(folder);
        if (!_folders.TryGetValue(key, out var watched) || !watched.Unclaim())
        {
            return; // Somebody else is still looking at it.
        }

        if (_folders.TryRemove(key, out var removed))
        {
            _logger.Debug("No longer watching {Folder}: its last viewer left", key);
            removed.Dispose();
        }
    }

    /// <inheritdoc />
    public IDisposable Watch(string folder, Action<TSnapshot> onChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(onChanged);

        var key = Key(folder);
        var disposed = false;
        Changed += OnChanged;
        return new WatchedFolderSubscription(() =>
        {
            disposed = true;
            Changed -= OnChanged;
        });

        void OnChanged(object? sender, WatchedFolderChangedEventArgs<TSnapshot> args)
        {
            // A raise already under way when the subscription went still reaches this handler;
            // the flag keeps the promise that no call follows a dispose.
            if (!disposed && string.Equals(args.FolderPath, key, StringComparison.OrdinalIgnoreCase))
            {
                onChanged(args.Snapshot);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var watched in _folders.Values)
        {
            watched.Dispose();
        }
        _folders.Clear();
        GC.SuppressFinalize(this);
    }

    private WatchedFolder<TSnapshot> Watched(string folder) =>
        _folders.GetOrAdd(Key(folder), path =>
        {
            _logger.Debug("Reading and watching {Folder}", path);
            return new WatchedFolder<TSnapshot>(path, Read(path), settleDelay, OnSettled);
        });

    private void OnSettled(WatchedFolder<TSnapshot> watched)
    {
        // Re-read before announcing: a listener that asked the store during the event would
        // otherwise be handed the old snapshot.
        watched.Snapshot = Read(watched.Path);
        Changed?.Invoke(this, new WatchedFolderChangedEventArgs<TSnapshot>(watched.Path, watched.Snapshot));
    }

    private static string Key(string folder) => Path.GetFullPath(folder);
}
