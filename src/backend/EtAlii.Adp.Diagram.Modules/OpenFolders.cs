using Serilog;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// The open folders of one host for a diagram drawn from a whole folder, one per registration, and the claims
/// on them: two sessions on one diagram share one reading, and the folder is closed when the last of them goes.
/// Ansible's and Helm's registries each wrote this as their own copy (code-structure T294).
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
/// <b>The claims are a <see cref="RetainedRegistry{TKey, TValue}"/>'s</b>, with a window of zero: the last
/// release closes the folder at once, and the next ask reads it afresh. A folder that was asked for but never
/// claimed stays open until a release or the disposal of the whole.
/// </para>
/// </remarks>
/// <typeparam name="TFolder">One open folder: what reads it, settles its changes and watches it.</typeparam>
public sealed class OpenFolders<TFolder> : IDisposable
    where TFolder : class, IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<OpenFolders<TFolder>>();

    private readonly Func<string, string?, TFolder> _open;
    private readonly Action<TFolder, Action> _onSettled;
    private readonly RetainedRegistry<string, TFolder> _folders;

    /// <summary>Set while the whole is disposed, when closing a folder is no viewer leaving it.</summary>
    private volatile bool _closingAll;

    /// <param name="open">Opens a folder with its registration, or with none.</param>
    /// <param name="onSettled">Has an opened folder call the action it is given each time a burst of its changes settled.</param>
    public OpenFolders(Func<string, string?, TFolder> open, Action<TFolder, Action> onSettled)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(onSettled);
        _open = open;
        _onSettled = onSettled;
        _folders = new RetainedRegistry<string, TFolder>(TimeSpan.Zero, Closed, TimeProvider.System, comparer: StringComparer.Ordinal);
    }

    /// <summary>Raised after an open folder's burst of changes settled; <see cref="Watch"/> hears one folder of it.</summary>
    private event Action<string, TFolder>? Changed;

    /// <summary>The open folder <paramref name="folder"/> with <paramref name="registration"/>, opening it if this is the first ask.</summary>
    public TFolder GetOrLoad(string folder, string? registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return _folders.GetOrAdd(Key(folder, registration), key => Open(key, folder, registration));
    }

    /// <summary><see cref="GetOrLoad"/>, and a claim on the folder's lifetime. Every call must be matched by a <see cref="Release"/>.</summary>
    public TFolder Acquire(string folder, string? registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return _folders.Retain(Key(folder, registration), key => Open(key, folder, registration));
    }

    /// <summary>Gives up one claim. When the last one goes, the folder is closed; the next ask reads it afresh.</summary>
    public void Release(string folder, string? registration) => _folders.Release(Key(folder, registration));

    /// <summary>
    /// Calls <paramref name="onChanged"/> with the folder each time a burst of changes to <paramref name="folder"/>
    /// with <paramref name="registration"/> settles, until the subscription it returns is disposed; once it is
    /// disposed no call follows, and disposing it twice is harmless.
    /// </summary>
    public IDisposable Watch(string folder, string? registration, Action<TFolder> onChanged)
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

        void OnChanged(string changed, TFolder opened)
        {
            // A raise already under way when the subscription went still reaches this handler; the flag
            // keeps the promise that no call follows a dispose.
            if (!disposed && string.Equals(changed, key, StringComparison.Ordinal)) onChanged(opened);
        }
    }

    /// <summary>Closes every open folder. What is asked for afterwards is opened afresh.</summary>
    public void Dispose()
    {
        _closingAll = true;
        try
        {
            _folders.Dispose();
        }
        finally
        {
            _closingAll = false;
        }
    }

    /// <summary>Opens the folder of <paramref name="key"/>; the registry calls it under its lock, so once per key.</summary>
    private TFolder Open(string key, string folder, string? registration)
    {
        _logger.Debug("Reading and watching {Folder}", key);
        var opened = _open(folder, registration);
        _onSettled(opened, () => Changed?.Invoke(key, opened));
        return opened;
    }

    private void Closed(string key, TFolder folder)
    {
        if (!_closingAll)
        {
            _logger.Debug("No longer watching {Folder}: its last viewer left", key);
        }

        folder.Dispose();
    }

    private static string Key(string folder, string? registration) => IoPath.GetFullPath(registration ?? folder);

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
