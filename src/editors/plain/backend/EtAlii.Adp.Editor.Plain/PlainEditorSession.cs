using Serilog;

namespace EtAlii.Adp.Editor.Plain;

/// <summary>
/// One open plain-text file: <see cref="TextFileBuffer"/> for reading and every
/// refusal rule (encoding, size, binary), plus a watcher so an external edit reaches the
/// session as a <see cref="IEditorSession.Changed"/> event rather than a stale buffer.
/// </summary>
public sealed class PlainEditorSession : IEditorSession
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
    /// <b>The tree-wide conversion stays deferred by the user, not declined.</b> 92 classes hold the
    /// cached-static shape and 90 still do; the pattern remains house style and no session should
    /// convert files on its own initiative. This is one class, changed because its silence was
    /// standing in the way of a specific diagnosis.
    /// </para>
    /// </remarks>
    private static ILogger Logger => Log.ForContext<PlainEditorSession>();

    private readonly string _path;
    private readonly FileSystemWatcher? _watcher;
    private TextFileBuffer? _buffer;

    public PlainEditorSession(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _path = path;

        var result = TextFileBuffer.Open(path);
        _buffer = result.Buffer;
        Refusal = result.Refusal;

        var directory = Path.GetDirectoryName(path);
        if (_buffer is not null && directory is not null)
        {
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(path));

            // EVERY EVENT A PUBLISH ACTUALLY RAISES, not just Changed. A write in place raises
            // Changed; a temp-then-replace publish - which is what AdpFileWriter does, and now
            // what the shared save command does - raises Renamed as the scratch file takes the
            // destination's name, and Created where there was nothing before. Subscribing to
            // Changed alone meant an external save through the central writer never reached the
            // open editor: measured as a 60-second gRPC deadline in
            // EditorResolutionTests.ADslOpenAsDiagramAndAsTextAtOnce_BothStayTrueToTheFile, which
            // waits for the text view to hear a save it made itself.
            _watcher.Changed += (_, _) => OnExternalChange();
            _watcher.Created += (_, _) => OnExternalChange();
            _watcher.Renamed += (_, _) => OnExternalChange();

            // Deleted and Error complete the set, and neither is the fix for anything.
            //
            // Deleted: File.Replace and File.Move can present the destination name's transition as
            // a Deleted, and RootFolderWatcher takes all five where this took three. Subscribing it
            // is only safe BECAUSE the refused re-read now retries - a Deleted callback finds the
            // name absent by construction, and before the retry landed its only possible fate was
            // to be swallowed. That ordering is a hard dependency rather than a preference.
            //
            // Error: the one signal FileSystemWatcher gives when its internal buffer overflows and
            // it has silently dropped events. Unsubscribed, an overflow is invisible - and LOGGED
            // ONLY, it is still unlearned: the log hears of it while the editor keeps showing what it
            // held before. Which events were dropped is unknowable, so the file is read again
            // (backend-centralization R2.9). Subscribing alone satisfied the wiring guard and not
            // the obligation, which is what the obligation tests caught.
            _watcher.Deleted += (_, _) => OnExternalChange();
            _watcher.Error += (_, args) =>
            {
                Logger.Warning(args.GetException(), "The watcher for {Path} stumbled; reading it again", _path);
                OnExternalChange();
            };

            // Subscribed BEFORE the watcher is enabled. Four other watchers in this tree do it
            // this way - RootFolderWatcher, TrackedProblemRoot, AnsibleWatchedFolder and
            // HelmWatchedFolder - and enabling first leaves the watcher live with no handlers
            // attached, so anything raised in that window is received by nobody. The window is
            // small and nothing has been shown to fall through it; a hole is worth closing on
            // its own terms.
            _watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>The file's text, or empty when the open was refused - <see cref="Refusal"/> says why.</summary>
    public string Content => _buffer?.Content ?? "";

    /// <summary>Why the file did not open, or empty. A refusal is an answer, not a fault (Requirement 7.1).</summary>
    public string Refusal { get; private set; }

    public event EventHandler<EditorContentChangedEventArgs>? Changed;

    /// <summary>
    /// How many times a re-read is retried before the notification is given up on, and how long
    /// between attempts.
    /// </summary>
    /// <remarks>
    /// <b>A refusal must not consume the change.</b> A publish through <c>AdpFileWriter</c> is a
    /// temp-then-replace, and <see cref="TextFileBuffer.Open"/> refuses on
    /// <c>!info.Exists</c> - <b>before any exception handling</b>, so it is a refusal rather than
    /// something a catch would see. A callback landing while the destination name is in transit
    /// therefore got "does not exist", and the old code logged it and RETURNED: the notification
    /// was consumed and nothing ever re-read, so a waiting reader waited for an event that had
    /// already been delivered and thrown away.
    /// <para>
    /// Retrying is the whole fix. The window is microseconds wide by design, so three attempts a
    /// short hop apart is generous rather than hopeful - and if all three refuse, the file really
    /// is unreadable and the last good content stays in place, which is the behaviour the original
    /// comment described and intended.
    /// </para>
    /// </remarks>
    private const int ReadAttempts = 3;

    private static readonly TimeSpan BetweenReadAttempts = TimeSpan.FromMilliseconds(20);

    private void OnExternalChange()
    {
        var reads = 0;
        var result = ReadWithRetry(
            () =>
            {
                reads++;
                return TextFileBuffer.Open(_path);
            },
            ReadAttempts,
            BetweenReadAttempts);
        if (result.Buffer is not null && reads > 1)
        {
            // How long a real refusal lasts is not known, and three attempts 20 ms apart is a choice.
            // This line is how it gets measured, worded as the c4 and causal-loop stores word theirs
            // so that one search finds every retried read in the tree.
            Logger.Information("Read {Path} on attempt {Attempt} of {Attempts}", _path, reads, ReadAttempts);
        }

        if (result.Buffer is null)
        {
            // Out of attempts: the file is genuinely unreadable rather than mid-replace. Worth a
            // line, and the last good content stays in place rather than being replaced by nothing.
            Logger.Warning(
                "The externally changed {Path} no longer opens after {Attempts} attempts: {Refusal}",
                _path,
                ReadAttempts,
                result.Refusal);
            Refusal = result.Refusal;
            return;
        }

        _buffer = result.Buffer;
        Refusal = "";
        Changed?.Invoke(this, new EditorContentChangedEventArgs(result.Buffer.Content));
    }

    /// <summary>
    /// Reads until it succeeds or runs out of attempts.
    /// </summary>
    /// <remarks>
    /// <b>The reader is a function, and that is for the guard rather than for the production path -
    /// which always passes the same one.</b> The window this retry exists for is microseconds wide
    /// by design, so a test deleting and recreating a file could only land inside it by luck, and a
    /// guard that passes by luck is not a guard. Taking the read as a parameter lets
    /// <c>PlainEditorSession.ReadRetry.Tests</c> supply a reader that refuses exactly as often as it
    /// chooses, which makes the retry deterministic instead of making the test patient.
    /// <para>
    /// <b>Internal rather than private for the same reason</b>, via <c>InternalsVisibleTo</c> in this
    /// project's <c>.csproj</c>. That is this tree's house shape rather than a concession: 21 other
    /// projects already open their internals to their own test assembly, including
    /// <c>EtAlii.Adp.Documents</c>, <c>EtAlii.Adp.Hierarchy</c>, <c>EtAlii.Adp.Context</c> and every
    /// diagram module.
    /// </para>
    /// </remarks>
    internal static TextFileBufferOpenResult ReadWithRetry(
        Func<TextFileBufferOpenResult> read,
        int attempts,
        TimeSpan between)
    {
        ArgumentNullException.ThrowIfNull(read);

        for (var attempt = 1; ; attempt++)
        {
            var result = read();
            if (result.Buffer is not null || attempt >= attempts)
            {
                return result;
            }

            Thread.Sleep(between);
        }
    }

    public ValueTask DisposeAsync()
    {
        _watcher?.Dispose();
        return ValueTask.CompletedTask;
    }
}
