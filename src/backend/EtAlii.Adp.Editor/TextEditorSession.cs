using EtAlii.Adp.Documents;
using Serilog;

namespace EtAlii.Adp.Editor;

/// <summary>
/// One open text file: <see cref="TextFileBuffer"/> for reading and every refusal rule
/// (encoding, size, binary), plus a watcher so an external edit reaches the session as a
/// <see cref="IEditorSession.Changed"/> event rather than a stale buffer (code-structure
/// FR-053). The plain editor opens it as it is; the markdown editor is a thin variant, because
/// what makes markdown worth its own module - preview, heading navigation - is client-side.
/// </summary>
/// <remarks>
/// One class where the plain and markdown editors each had a copy, and the copies drifted: the
/// markdown one re-read once while a comment beside its watcher said the re-read retried, a
/// sentence copied from the plain one, where it was true. A premise copied between siblings has
/// to be checked in the sibling it lands in, and one class has no sibling.
/// </remarks>
public class TextEditorSession : IEditorSession
{
    /// <summary>
    /// Resolved AT THE CALL SITE rather than cached in a static field, following
    /// <c>AdpFileWriter</c>, which carries the measurement behind this shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Serilog's unset <c>Log.Logger</c> is a <c>SilentLogger</c>, and a
    /// <c>private static readonly ILogger</c> evaluated at that moment IS that silent logger for the
    /// life of the process - configuring <c>Log.Logger</c> afterwards changes nothing for it. Type
    /// initialisation runs in FIRST-USE order, so whether a class is mute depends on when it happened
    /// to be touched, and nothing visible distinguishes a muted class from a quiet one.
    /// </para>
    /// <para>
    /// <b>Why this class rather than the tree.</b> This session's warnings are the only record that a
    /// re-read was refused - the path that discards a notification if the retry above runs out. A
    /// warning that may never be written is not a weak signal but an absent one, and its silence was
    /// load-bearing during the deadline-flake investigation: "no evidence of a drop" and "no channel
    /// for evidence of a drop" were indistinguishable for two days.
    /// </para>
    /// <para>
    /// <b>The tree-wide conversion stays deferred by the user, not declined.</b> Most classes still
    /// hold the cached-static shape; the pattern remains house style and no session should convert
    /// files on its own initiative. This is one class, changed because its silence was standing in
    /// the way of a specific diagnosis.
    /// </para>
    /// <para>
    /// Of the concrete type, so a markdown session's records say markdown.
    /// </para>
    /// </remarks>
    private ILogger Logger => Log.ForContext(GetType());

    private readonly string _path;
    private readonly Func<string, TextFileBufferOpenResult> _open;
    private TextFileBuffer? _buffer;

    /// <param name="path">The file.</param>
    /// <param name="events">The project roots' event sources, through which a file inside a watched root is heard.</param>
    public TextEditorSession(string path, ProjectFileEvents? events = null)
        : this(path, TextFileBuffer.Open, TimeProvider.System, events)
    {
    }

    /// <summary>
    /// With the open supplied, so a test can refuse a re-read exactly as often as it chooses - the
    /// window the retry exists for is too narrow to land in on purpose with a real file - and with
    /// the clock the retry waits on.
    /// </summary>
    internal TextEditorSession(string path, Func<string, TextFileBufferOpenResult> open, TimeProvider time, ProjectFileEvents? events = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(time);
        _path = path;
        _open = open;

        var result = _open(path);
        _buffer = result.Buffer;
        Refusal = result.Refusal;

        // Watched only when it opened: a refused file has nothing to keep up to date. Every event a
        // publish raises reaches OnExternalChangeAsync, an overflow included, one re-read at a time.
        if (_buffer is not null)
        {
            Watched = new WatchedFile(path, OnExternalChangeAsync, time, events);
        }
    }

    /// <summary>The file's text, or empty when the open was refused - <see cref="Refusal"/> says why.</summary>
    public string Content => _buffer?.Content ?? "";

    /// <summary>Why the file did not open, or empty. A refusal is an answer, not a fault (Requirement 7.1).</summary>
    public string Refusal { get; private set; }

    public event EventHandler<EditorContentChangedEventArgs>? Changed;

    /// <summary>The watched file, or null when the open was refused; for the guards, which reach its watcher.</summary>
    internal WatchedFile? Watched { get; }

    /// <summary>
    /// Reads the file again after another program changed it. Internal for the guard, which calls it
    /// as the watched file would.
    /// </summary>
    /// <remarks>
    /// <b>A refusal must not consume the change.</b> A publish through <c>AdpFileWriter</c> is a
    /// temp-then-replace, and <see cref="TextFileBuffer.Open"/> refuses on <c>!info.Exists</c> -
    /// <b>before any exception handling</b>, so it is a refusal rather than something a catch would
    /// see. A read landing while the destination name is in transit gets "does not exist", so the
    /// read is retried as <see cref="WatchedFile.ReadAsync{T}"/> does; if every attempt refuses, the
    /// file really is unreadable and the last good content stays in place.
    /// </remarks>
    internal async Task OnExternalChangeAsync()
    {
        var file = Watched ?? throw new InvalidOperationException("A session whose open was refused watches nothing.");
        (TextFileBufferOpenResult result, int reads) = await file.ReadAsync(_open, read => read.Buffer is not null).ConfigureAwait(false);
        if (result.Buffer is not null && reads > 1)
        {
            // How long a real refusal lasts is not known, and three attempts 20 ms apart is a choice.
            // This line is how it gets measured, worded as the c4 and causal-loop stores word theirs
            // so that one search finds every retried read in the tree.
            Logger.Information("Read {Path} on attempt {Attempt} of {Attempts}", _path, reads, WatchedFile.ReadAttempts);
        }

        if (result.Buffer is null)
        {
            // Out of attempts: the file is genuinely unreadable rather than mid-replace. Worth a
            // line, and the last good content stays in place rather than being replaced by nothing.
            Logger.Warning(
                "The externally changed {Path} no longer opens after {Attempts} attempts: {Refusal}",
                _path,
                WatchedFile.ReadAttempts,
                result.Refusal);
            Refusal = result.Refusal;
            return;
        }

        _buffer = result.Buffer;
        Refusal = "";
        Changed?.Invoke(this, new EditorContentChangedEventArgs(result.Buffer.Content));
    }

    public ValueTask DisposeAsync()
    {
        Watched?.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
