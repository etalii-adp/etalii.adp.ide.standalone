using Serilog;

namespace EtAlii.Adp.Documents;

/// <summary>
/// One source of file events per project root, shared by everyone who listens there
/// (code-structure FR-050): one recursive <see cref="WatchedFolder"/> however many subscribe,
/// made by the first subscription and disposed with the last.
/// </summary>
/// <remarks>
/// <para>
/// <b>Normalized.</b> What a subscriber hears is what happened to the project, not what the
/// watcher saw. On Linux a move the watcher split into a Deleted and a Created is joined back into
/// the one Renamed it was (<see cref="SplitMoveJoiner"/>). A writer's own files are not project
/// content (<see cref="AdpFileWriter.IsTransient"/>): an event for one is dropped, a file renamed
/// onto one is only being moved aside for the instant of a replace, and the scratch file renamed
/// onto its destination is the publish that destination gets, so it is heard as a Created.
/// </para>
/// <para>
/// Measured with a watcher on a <c>File.Replace</c> save: the body is renamed away to
/// <c>&lt;body&gt;~RF&lt;hex&gt;.TMP</c>, the scratch file is renamed onto it, and the backup is
/// deleted. Normalized, that whole save is one Created of the body - never a Deleted, which a
/// subscriber would read as the body gone.
/// </para>
/// <para>
/// <b>Lost.</b> When the watcher stumbles, its subscribers hear <see cref="FileEventKind.Lost"/>
/// for the root and read again what they care about, and the source makes itself a new watcher.
/// KNOWN LIMIT: a root that cannot be watched again - deleted underneath, an inaccessible share -
/// stays unwatched until a later subscription to it tries again.
/// </para>
/// <para>
/// <b>Guarded.</b> Every subscriber is called on the watcher's thread, one at a time and in the
/// order the events happened; one that throws is logged and costs nobody else their event.
/// </para>
/// </remarks>
public sealed class ProjectFileEvents : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<ProjectFileEvents>();

    private readonly Dictionary<string, Source> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <summary>How many roots are watched now, for a test of the reference count.</summary>
    internal int Roots
    {
        get
        {
            lock (_gate)
            {
                return _sources.Count;
            }
        }
    }

    /// <summary>The watcher behind <paramref name="rootPath"/>, for a test that has to make it overflow; null while it has none.</summary>
    internal FileSystemWatcher? WatcherOf(string rootPath)
    {
        lock (_gate)
        {
            return _sources.TryGetValue(Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)), out var source) ? source.Watcher : null;
        }
    }

    /// <summary>
    /// Starts hearing everything that happens under <paramref name="rootPath"/>, until the returned
    /// subscription is disposed.
    /// </summary>
    /// <param name="rootPath">The project root; it must exist.</param>
    /// <param name="heard">What runs for each normalized event, on the watcher's thread.</param>
    /// <exception cref="ArgumentException">The root does not exist.</exception>
    /// <exception cref="IOException">The root cannot be watched.</exception>
    public IDisposable Subscribe(string rootPath, Action<FileEvent> heard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(heard);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_sources.TryGetValue(root, out var source))
            {
                source.WatchAgainIfLost();
            }
            else
            {
                source = new Source(root);
                _sources[root] = source;
            }

            var subscription = new Subscription(this, source, heard);
            source.Subscribers = [.. source.Subscribers, subscription];
            return subscription;
        }
    }

    /// <summary>
    /// Starts hearing what happens under a root already watched that holds <paramref name="folder"/>,
    /// if there is one; null when no watched root holds it, and nothing is subscribed then.
    /// </summary>
    /// <remarks>
    /// For an owner of a folder inside a project - a module's store - that would otherwise make a
    /// watcher of its own beside the project's. Everything under the root is heard; the owner picks
    /// out its folder. The subscription keeps the root watched while it lasts.
    /// </remarks>
    /// <returns>The subscription and the root it is to, or null.</returns>
    public (IDisposable Subscription, string Root)? SubscribeWithin(string folder, Action<FileEvent> heard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(heard);

        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var source = _sources.Values.FirstOrDefault(candidate => IsWithin(path, candidate.Root));
            if (source is null)
            {
                return null;
            }

            source.WatchAgainIfLost();
            var subscription = new Subscription(this, source, heard);
            source.Subscribers = [.. source.Subscribers, subscription];
            return (subscription, source.Root);
        }
    }

    /// <summary>
    /// Makes the watcher of <paramref name="rootPath"/> anew, if it is watched: for a root that went
    /// away and came back, whose old watcher - on Linux, without a word - hears nothing any more.
    /// </summary>
    public void WatchAgain(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        Source? source;
        lock (_gate)
        {
            _sources.TryGetValue(Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)), out source);
        }

        source?.WatchAgain();
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or lies somewhere under it.</summary>
    public static bool IsWithin(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || (path.Length > folder.Length
            && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
            && (path[folder.Length] == Path.DirectorySeparatorChar || path[folder.Length] == Path.AltDirectorySeparatorChar));

    /// <summary>
    /// What a subscriber hears of one event, or null for an event about a writer's own file.
    /// </summary>
    internal static FileEvent? Normalize(FileEvent heard)
    {
        if (heard.Kind == FileEventKind.Lost)
        {
            return heard;
        }

        if (AdpFileWriter.IsTransient(heard.Path))
        {
            return null;
        }

        return heard is { Kind: FileEventKind.Renamed, OldPath: { } oldPath } && AdpFileWriter.IsTransient(oldPath)
            ? new FileEvent(FileEventKind.Created, heard.Path)
            : heard;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var source in _sources.Values)
            {
                source.Dispose();
            }

            _sources.Clear();
        }
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_gate)
        {
            var source = subscription.Source;
            source.Subscribers = [.. source.Subscribers.Where(other => other != subscription)];
            if (source.Subscribers.Length == 0 && _sources.Remove(source.Root))
            {
                source.Dispose();
            }
        }
    }

    private sealed class Subscription(ProjectFileEvents owner, Source source, Action<FileEvent> heard) : IDisposable
    {
        private int _disposed;

        public Source Source { get; } = source;

        public Action<FileEvent> Heard { get; } = heard;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Unsubscribe(this);
            }
        }
    }

    private sealed class Source : IDisposable
    {
        private readonly SplitMoveJoiner? _joiner;
        private readonly Lock _watching = new();
        private WatchedFolder? _folder;
        private bool _disposed;

        public Source(string root)
        {
            Root = root;

            // Windows reports every move as one Renamed; only inotify splits one in two.
            _joiner = OperatingSystem.IsWindows() ? null : new SplitMoveJoiner(Publish);
            _folder = Watch();
        }

        public string Root { get; }

        public FileSystemWatcher? Watcher
        {
            get
            {
                lock (_watching)
                {
                    return _folder?.Watcher;
                }
            }
        }

        /// <summary>Replaced whole under the owner's gate, so a publish reads one consistent set.</summary>
        public Subscription[] Subscribers { get; set; } = [];

        public void WatchAgain()
        {
            lock (_watching)
            {
                if (_disposed)
                {
                    return;
                }

                _folder?.Dispose();
                TryWatch();
            }
        }

        public void WatchAgainIfLost()
        {
            lock (_watching)
            {
                if (!_disposed && _folder is null)
                {
                    TryWatch();
                }
            }
        }

        public void Dispose()
        {
            lock (_watching)
            {
                _disposed = true;
                _folder?.Dispose();
                _folder = null;
            }

            _joiner?.Dispose();
        }

        private WatchedFolder Watch() => new(Root, Heard, recursive: true);

        private void Heard(FileEvent heard)
        {
            if (heard.Kind == FileEventKind.Lost)
            {
                lock (_watching)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _folder?.Dispose();
                    TryWatch();
                }
            }

            // Through the joiner a Lost also delivers a deletion it still holds first, older as
            // that deletion is than whatever reading again recovers.
            if (_joiner is null)
            {
                Publish(heard);
            }
            else
            {
                _joiner.Post(heard);
            }
        }

        private void TryWatch()
        {
            try
            {
                _folder = Watch();
            }
            catch (Exception exception) when (exception is ArgumentException or IOException)
            {
                _folder = null;
                _logger.Warning(exception, "Cannot watch {Root} again; changes there are not heard until it is subscribed to anew", Root);
            }
        }

        private void Publish(FileEvent heard)
        {
            if (Normalize(heard) is not { } normalized)
            {
                return;
            }

            foreach (var subscriber in Subscribers)
            {
                try
                {
                    subscriber.Heard(normalized);
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "A subscriber failed on the {Kind} of {Path}; the others still hear it", normalized.Kind, normalized.Path);
                }
            }
        }
    }
}
