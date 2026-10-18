using EtAlii.Adp.Files;
using Serilog;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// One <see cref="HelmBody"/> per registered chart folder, kept true as the folder changes (Requirement 1.4) -
/// the module's only reading, so two people looking at one chart cannot see different charts - and the claims
/// on it: two sessions on one diagram share one reading, and the folder is closed when the last of them goes
/// (helm-chart-disl-fbl, task 9).
/// </summary>
/// <remarks>
/// <para>
/// <b>The settling and the reading again are the library's</b> (<see cref="Specification.Fbl.Plugins.FolderBody"/>):
/// a helm dependency build rewriting <c>charts/</c> wholesale lands as one read after the binding's <c>settle</c>,
/// and the walk is milliseconds, so every settled burst reads the whole chart again.
/// </para>
/// <para>
/// <b>Reading and lifetime are separate on purpose.</b> <see cref="GetOrLoad"/> answers a question;
/// <see cref="Acquire"/> and <see cref="Release"/> are the pair a session holds across its own lifetime. Without
/// the split, a second connection closing would close the folder under the first, and its diagram would go
/// quietly stale.
/// </para>
/// <para>
/// <b>A folder is keyed by its registration</b>, the file that made it a diagram and the one a position is
/// stored in, so the folder body watches the registration each session writes to; a folder opened without one
/// is keyed by its own path.
/// </para>
/// </remarks>
/// <param name="reader">The reader the plugin reads every folder with.</param>
/// <param name="time">The clock a burst of changes settles on; the system's when none is given.</param>
/// <param name="events">
/// The project roots' event sources: a chart folder inside a watched project root is heard through that root's
/// one watcher rather than a watcher of its own (code-structure T280).
/// </param>
public sealed class HelmChartStore(HelmChartReader reader, TimeProvider? time = null, ProjectFileEvents? events = null) : IHelmChartStore, IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<HelmChartStore>();

    private readonly HelmChartReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    private readonly Dictionary<string, Entry> _open = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>Raised after an open folder's burst of changes settled; <see cref="Watch"/> hears one folder of it.</summary>
    private event Action<string, HelmBody>? Changed;

    /// <summary>A store over the module's own reader; the shape the host and most tests want.</summary>
    public HelmChartStore()
        : this(new HelmChartReader())
    {
    }

    /// <inheritdoc />
    public HelmBody GetOrLoad(string folder, string? registration = null)
    {
        lock (_gate)
        {
            return Opened(folder, registration).Body;
        }
    }

    /// <inheritdoc />
    public HelmBody Acquire(string folder, string? registration = null)
    {
        lock (_gate)
        {
            var entry = Opened(folder, registration);
            entry.Claims++;
            return entry.Body;
        }
    }

    /// <inheritdoc />
    public void Release(string folder, string? registration = null)
    {
        Entry? closed = null;
        lock (_gate)
        {
            var key = Key(folder, registration);
            if (!_open.TryGetValue(key, out var entry)) return;
            entry.Claims = Math.Max(0, entry.Claims - 1);
            if (entry.Claims == 0 && _open.Remove(key, out closed))
            {
                _logger.Debug("No longer watching {Folder}: its last viewer left", key);
            }
        }

        closed?.Body.Dispose();
    }

    /// <inheritdoc />
    /// <remarks>Once the subscription is disposed no call follows, and disposing it twice is harmless.</remarks>
    public IDisposable Watch(string folder, string? registration, Action<HelmBody> onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        var key = Key(folder, registration);
        var disposed = false;
        Changed += OnChanged;
        return new Subscription(() =>
        {
            disposed = true;
            Changed -= OnChanged;
        });

        void OnChanged(string changed, HelmBody opened)
        {
            // A raise already under way when the subscription went still reaches this handler; the flag
            // keeps the promise that no call follows a dispose.
            if (!disposed && string.Equals(changed, key, StringComparison.Ordinal)) onChanged(opened);
        }
    }

    public void Dispose()
    {
        List<Entry> open;
        lock (_gate)
        {
            open = [.. _open.Values];
            _open.Clear();
        }

        foreach (var entry in open)
        {
            entry.Body.Dispose();
        }
    }

    /// <summary>The open folder, opened now if it is not; the caller holds the gate.</summary>
    private Entry Opened(string folder, string? registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var key = Key(folder, registration);
        if (_open.TryGetValue(key, out var entry)) return entry;

        _logger.Debug("Reading and watching {Folder}", key);
        var opened = HelmBody.Open(folder, registration, time, _reader, events);
        opened.Changed += (_, _) => Changed?.Invoke(key, opened);
        entry = new Entry(opened);
        _open[key] = entry;
        return entry;
    }

    private static string Key(string folder, string? registration) => IoPath.GetFullPath(registration ?? folder);

    private sealed class Entry(HelmBody body)
    {
        public HelmBody Body { get; } = body;

        public int Claims { get; set; }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
