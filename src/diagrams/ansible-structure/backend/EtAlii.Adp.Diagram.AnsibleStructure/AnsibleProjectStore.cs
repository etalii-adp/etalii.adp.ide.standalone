using System.Collections.Concurrent;

using Serilog;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One <see cref="AnsibleProject"/> per registered folder, kept true as the tree changes
/// (Requirement 3.5) - the module's only reader, so two people looking at one project cannot see
/// different structures.
/// </summary>
/// <remarks>
/// <para>
/// <b>A settled burst re-reads the whole folder</b>, rather than surgically re-reading the files
/// that changed. That is a deliberate simplification and worth naming: an incremental path would
/// be a second way to build a model, able to disagree with the first, and the failure would be a
/// diagram that is subtly wrong rather than one that is obviously broken. A registered folder is
/// one Ansible project, the burst timer already collapses a multi-file save into a single
/// re-read, and the walk is milliseconds. If a project ever appears where that is not true, the
/// place to fix it is <see cref="AnsibleProjectReader"/> - memoizing per-file parses behind the
/// same walk - rather than a divergent code path here.
/// </para>
/// <para>
/// There is no save method, and no code path in this file opens a file for writing.
/// </para>
/// </remarks>
public sealed class AnsibleProjectStore : IAnsibleProjectStore, IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<AnsibleProjectStore>();

    /// <summary>How long a burst of changes may keep growing before one re-read answers it all.</summary>
    private static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromMilliseconds(400);

    private readonly AnsibleProjectReader _reader;
    private readonly TimeSpan _settleDelay;
    private readonly ConcurrentDictionary<string, AnsibleWatchedFolder> _folders = new(StringComparer.OrdinalIgnoreCase);

    public AnsibleProjectStore(AnsibleProjectReader reader, TimeSpan? settleDelay = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _settleDelay = settleDelay ?? DefaultSettleDelay;
    }

    /// <summary>A store over the module's own reader; the shape the host and most tests want.</summary>
    public AnsibleProjectStore()
        : this(new AnsibleProjectReader())
    {
    }

    public event EventHandler<AnsibleProjectChangedEventArgs>? Changed;

    public AnsibleProject GetOrLoad(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Watched(folder).Project;
    }

    public AnsibleProject? Get(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return _folders.TryGetValue(Key(folder), out var watched) ? watched.Project : null;
    }

    public AnsibleProject Acquire(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var watched = Watched(folder);
        watched.Claim();
        return watched.Project;
    }

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

    public void Dispose()
    {
        foreach (var watched in _folders.Values)
        {
            watched.Dispose();
        }
        _folders.Clear();
    }

    private AnsibleWatchedFolder Watched(string folder)
    {
        var key = Key(folder);
        return _folders.GetOrAdd(key, path =>
        {
            _logger.Debug("Reading and watching {Folder}", path);
            return new AnsibleWatchedFolder(path, _reader.Read(path), _settleDelay, OnSettled);
        });
    }

    private void OnSettled(AnsibleWatchedFolder watched)
    {
        // Re-read before announcing: a listener that asked the store during the event would
        // otherwise be handed the old project.
        watched.Project = _reader.Read(watched.Path);
        Changed?.Invoke(this, new AnsibleProjectChangedEventArgs(watched.Path, watched.Project));
    }

    private static string Key(string folder) => IoPath.GetFullPath(folder);
}
