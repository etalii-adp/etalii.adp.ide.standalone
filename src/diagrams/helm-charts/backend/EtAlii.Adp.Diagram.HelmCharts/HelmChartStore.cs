using System.Collections.Concurrent;

using Serilog;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// One <see cref="HelmChart"/> per registered folder, kept true as the folder changes
/// (Requirement 1.4) - the module's only reader, so two people looking at one chart cannot see
/// different charts.
/// </summary>
/// <remarks>
/// <para>
/// <b>A settled burst re-reads the whole folder</b>, rather than surgically re-reading the files
/// that changed. That is a deliberate simplification and worth naming: an incremental path would
/// be a second way to build a model, able to disagree with the first, and the failure would be a
/// diagram that is subtly wrong rather than one that is obviously broken. A registered folder is
/// one chart, the burst timer - a helm dependency build rewriting charts/ wholesale lands as one re-read (the design's burst case) - already collapses a multi-file save into a single
/// re-read, and the walk is milliseconds. If a chart ever appears where that is not true, the
/// place to fix it is <see cref="HelmChartReader"/> - memoizing per-file parses behind the
/// same walk - rather than a divergent code path here.
/// </para>
/// <para>
/// There is no save method, and no code path in this file opens a file for writing.
/// </para>
/// </remarks>
public sealed class HelmChartStore : IHelmChartStore, IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<HelmChartStore>();

    /// <summary>How long a burst of changes may keep growing before one re-read answers it all.</summary>
    private static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromMilliseconds(400);

    private readonly HelmChartReader _reader;
    private readonly TimeSpan _settleDelay;
    private readonly ConcurrentDictionary<string, HelmWatchedFolder> _folders = new(StringComparer.OrdinalIgnoreCase);

    public HelmChartStore(HelmChartReader reader, TimeSpan? settleDelay = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _settleDelay = settleDelay ?? DefaultSettleDelay;
    }

    /// <summary>A store over the module's own reader; the shape the host and most tests want.</summary>
    public HelmChartStore()
        : this(new HelmChartReader())
    {
    }

    public event EventHandler<HelmChartChangedEventArgs>? Changed;

    public HelmChart GetOrLoad(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Watched(folder).Chart;
    }

    public HelmChart? Get(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return _folders.TryGetValue(Key(folder), out var watched) ? watched.Chart : null;
    }

    public HelmChart Acquire(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var watched = Watched(folder);
        watched.Claim();
        return watched.Chart;
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

    private HelmWatchedFolder Watched(string folder)
    {
        var key = Key(folder);
        return _folders.GetOrAdd(key, path =>
        {
            _logger.Debug("Reading and watching {Folder}", path);
            return new HelmWatchedFolder(path, _reader.Read(path), _settleDelay, OnSettled);
        });
    }

    private void OnSettled(HelmWatchedFolder watched)
    {
        // Re-read before announcing: a listener that asked the store during the event would
        // otherwise be handed the old chart.
        watched.Chart = _reader.Read(watched.Path);
        Changed?.Invoke(this, new HelmChartChangedEventArgs(watched.Path, watched.Chart));
    }

    private static string Key(string folder) => IoPath.GetFullPath(folder);
}
