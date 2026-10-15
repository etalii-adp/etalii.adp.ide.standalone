using Serilog;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// The open <see cref="AnsibleFolder"/>s of one host, one per registration, and the claims on them: two
/// sessions on one diagram share one reading, and the folder is closed when the last of them goes.
/// </summary>
/// <remarks>
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
/// <para>
/// There is no method that takes anything to write: the one write of this tool is a position, through core's
/// <c>SetRegistrationLayoutCommand</c>.
/// </para>
/// </remarks>
public sealed class AnsibleFolders : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<AnsibleFolders>();

    private readonly Dictionary<string, Entry> _open = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>Raised after an open folder's burst of changes settled; <see cref="Watch"/> hears one folder of it.</summary>
    private event Action<string, AnsibleFolder>? Changed;

    /// <summary>The open folder <paramref name="folder"/> with <paramref name="registration"/>, opening it if this is the first ask.</summary>
    public AnsibleFolder GetOrLoad(string folder, string? registration = null)
    {
        lock (_gate)
        {
            return Opened(folder, registration).Folder;
        }
    }

    /// <summary><see cref="GetOrLoad"/>, and a claim on the folder's lifetime. Every call must be matched by a <see cref="Release"/>.</summary>
    public AnsibleFolder Acquire(string folder, string? registration = null)
    {
        lock (_gate)
        {
            var entry = Opened(folder, registration);
            entry.Claims++;
            return entry.Folder;
        }
    }

    /// <summary>Gives up one claim. When the last one goes, the folder is closed; the next ask reads it afresh.</summary>
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

        closed?.Folder.Dispose();
    }

    /// <summary>
    /// Calls <paramref name="onChanged"/> with the folder each time a burst of changes to <paramref name="folder"/>
    /// with <paramref name="registration"/> settles, until the subscription it returns is disposed; once it is
    /// disposed no call follows, and disposing it twice is harmless.
    /// </summary>
    public IDisposable Watch(string folder, string? registration, Action<AnsibleFolder> onChanged)
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

        void OnChanged(string changed, AnsibleFolder opened)
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
            entry.Folder.Dispose();
        }
    }

    /// <summary>The open folder, opened now if it is not; the caller holds the gate.</summary>
    private Entry Opened(string folder, string? registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var key = Key(folder, registration);
        if (_open.TryGetValue(key, out var entry)) return entry;

        _logger.Debug("Reading and watching {Folder}", key);
        var opened = new AnsibleFolder(folder, registration);
        opened.Changed += (_, _) => Changed?.Invoke(key, opened);
        entry = new Entry(opened);
        _open[key] = entry;
        return entry;
    }

    private static string Key(string folder, string? registration) => IoPath.GetFullPath(registration ?? folder);

    private sealed class Entry(AnsibleFolder folder)
    {
        public AnsibleFolder Folder { get; } = folder;

        public int Claims { get; set; }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
