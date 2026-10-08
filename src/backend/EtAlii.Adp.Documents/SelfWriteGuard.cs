using System.Collections.Concurrent;

namespace EtAlii.Adp.Documents;

/// <summary>
/// Tells a writable store which changes on disk are its own saves, so they do not come back
/// through its reload as somebody else's.
/// </summary>
/// <remarks>
/// <para>
/// <b>A mark held for the length of the write is not enough on its own, and was all there was
/// until now.</b> The watcher reports a write after it lands - asynchronously, on its own thread -
/// so the notification for a store's own save nearly always arrived after the mark had been
/// cleared in the save's <c>finally</c>. Every save was then followed by a reload of the file just
/// written and a second change notice to every session on it. Harmless since the saves stopped
/// reading the cache, but a reload per save, for nothing.
/// </para>
/// <para>
/// So the guard also remembers the text each path was last saved as, and a change whose file
/// still holds exactly that text is the store's own write, however late its notification comes.
/// The comparison is on content rather than on time: an external edit that lands in the same
/// instant has different text and is reloaded, where a grace period would swallow it.
/// </para>
/// </remarks>
public sealed class SelfWriteGuard
{
    private readonly ConcurrentDictionary<string, byte> _writing = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _written = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, string> _read;

    public SelfWriteGuard()
        : this(SharedDocumentReader.ReadAllText)
    {
    }

    /// <summary>With the read supplied, so a test can count or refuse it.</summary>
    private SelfWriteGuard(Func<string, string> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        _read = read;
    }

    /// <summary>Marks <paramref name="path"/> as being written with <paramref name="text"/>. Pair with <see cref="End"/> in a <c>finally</c>.</summary>
    public void Begin(string path, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
        _written[path] = text;
        _writing[path] = 1;
    }

    /// <summary>The write to <paramref name="path"/> is over, whether it landed or not.</summary>
    public void End(string path) => _writing.TryRemove(path, out _);

    /// <summary>Whether a save to <paramref name="path"/> is in flight right now.</summary>
    public bool IsWriting(string path) => _writing.ContainsKey(path);

    /// <summary>
    /// Whether the change the watcher reported for <paramref name="path"/> is this store's own:
    /// a save is in flight, or the file holds exactly the text it was last saved as.
    /// </summary>
    /// <remarks>
    /// The remembered text is dropped the first time the file is found to differ, so a later
    /// external edit that happens to restore it is not mistaken for a save of this store's.
    /// A file that cannot be read is not claimed: the reload decides what that means.
    /// </remarks>
    public bool IsOwnWrite(string path)
    {
        if (_writing.ContainsKey(path))
        {
            return true;
        }

        if (!_written.TryGetValue(path, out var text))
        {
            return false;
        }

        string onDisk;
        try
        {
            onDisk = _read(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (string.Equals(onDisk, text, StringComparison.Ordinal))
        {
            return true;
        }

        _written.TryRemove(new KeyValuePair<string, string>(path, text));
        return false;
    }

    /// <summary>Forgets what <paramref name="path"/> was last saved as, when its document is dropped.</summary>
    public void Forget(string path) => _written.TryRemove(path, out _);
}
