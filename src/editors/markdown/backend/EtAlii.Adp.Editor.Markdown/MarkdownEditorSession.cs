using Serilog;

namespace EtAlii.Adp.Editor.Markdown;

/// <summary>
/// One open markdown file: <see cref="TextFileBuffer"/> for reading and every refusal
/// rule, plus a watcher surfacing external edits - at least everything the plain editor does
/// (Requirement 10.2). What makes markdown worth its own module - preview, heading
/// navigation - is client-side; this session deliberately adds nothing to the family's
/// backend contract, which is Requirement 10.3's whole point.
/// </summary>
public sealed class MarkdownEditorSession : IEditorSession
{
    private static readonly ILogger _logger = Log.ForContext<MarkdownEditorSession>();

    private readonly string _path;
    private readonly Func<string, TextFileBufferOpenResult> _open;
    private readonly FileSystemWatcher? _watcher;
    private TextFileBuffer? _buffer;

    public MarkdownEditorSession(string path)
        : this(path, TextFileBuffer.Open)
    {
    }

    /// <summary>
    /// With the open supplied, so a test can refuse a re-read exactly as often as it chooses - the
    /// window the retry exists for is too narrow to land in on purpose with a real file.
    /// </summary>
    internal MarkdownEditorSession(string path, Func<string, TextFileBufferOpenResult> open)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(open);
        _path = path;
        _open = open;

        var result = _open(path);
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
            // is only safe BECAUSE the refused re-read retries (OnExternalChange) - a Deleted
            // callback finds the name absent by construction, and without the retry its only
            // possible fate is to be swallowed. That ordering is a hard dependency rather than a
            // preference. THIS PARAGRAPH WAS FALSE HERE FOR A WHILE: it arrived with Plain's in
            // a2318531, where the retry already existed, while this session still re-read once. A
            // premise copied between siblings has to be checked in the sibling it lands in.
            //
            // Error: the one signal FileSystemWatcher gives when its internal buffer overflows and
            // it has silently dropped events. Unsubscribed, an overflow is invisible. Four other
            // watchers in this tree log it; these two did not.
            _watcher.Deleted += (_, _) => OnExternalChange();
            _watcher.Error += (_, args) =>
                _logger.Warning(args.GetException(), "The watcher for {Path} stumbled", _path);

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

    /// <summary>Why the file did not open, or empty. A refusal is an answer, not a fault.</summary>
    public string Refusal { get; private set; }

    public event EventHandler<EditorContentChangedEventArgs>? Changed;

    /// <summary>
    /// How many times a re-read is tried before the notification is given up on, and how long
    /// between attempts - <c>PlainEditorSession</c>'s figures, mirrored exactly.
    /// </summary>
    /// <remarks>
    /// <b>A refusal must not consume the change.</b> A publish is a temp-then-replace, and
    /// <see cref="TextFileBuffer.Open"/> refuses a name in transit before any exception handling, so
    /// a single read that lands in the window logs, returns, and throws the only notification for
    /// that write away. So does a read refused by another holder on a write's last event - the
    /// defect traced in c4's store as the EditorResolution 60-second flake. The two editor sessions
    /// agree on the figures on purpose, so that whether they need changing is one question, and the
    /// attempt line below is what answers it. <b>A retry narrows the window and does not close
    /// it</b>: a hold longer than the attempts still loses the change.
    /// </remarks>
    private const int ReadAttempts = 3;

    private static readonly TimeSpan BetweenReadAttempts = TimeSpan.FromMilliseconds(20);

    /// <summary>Internal for the guard, which calls it as the watcher would.</summary>
    internal void OnExternalChange()
    {
        var reads = 0;
        var result = ReadWithRetry(
            () =>
            {
                reads++;
                return _open(_path);
            },
            ReadAttempts,
            BetweenReadAttempts);
        if (result.Buffer is not null && reads > 1)
        {
            // Worded as the c4 and causal-loop stores and the plain editor word theirs, so that one
            // search finds every retried read in the tree.
            _logger.Information("Read {Path} on attempt {Attempt} of {Attempts}", _path, reads, ReadAttempts);
        }

        if (result.Buffer is null)
        {
            // Out of attempts: the file is genuinely unreadable rather than mid-replace. Worth a
            // line, and the last good content stays in place rather than being replaced by nothing.
            _logger.Warning(
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
    /// Reads until it succeeds or runs out of attempts - <c>PlainEditorSession.ReadWithRetry</c>,
    /// copied rather than shared because the two modules reference nothing of each other's.
    /// </summary>
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
