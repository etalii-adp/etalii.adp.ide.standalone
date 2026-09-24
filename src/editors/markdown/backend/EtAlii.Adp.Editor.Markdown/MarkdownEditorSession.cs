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
    private readonly FileSystemWatcher? _watcher;
    private TextFileBuffer? _buffer;

    public MarkdownEditorSession(string path)
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
                _logger.Warning(args.GetException(), "The watcher for {Path} stumbled; reading it again", _path);
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

    /// <summary>Why the file did not open, or empty. A refusal is an answer, not a fault.</summary>
    public string Refusal { get; private set; }

    public event EventHandler<EditorContentChangedEventArgs>? Changed;

    private void OnExternalChange()
    {
        var result = TextFileBuffer.Open(_path);
        if (result.Buffer is null)
        {
            _logger.Warning("The externally changed {Path} no longer opens: {Refusal}", _path, result.Refusal);
            Refusal = result.Refusal;
            return;
        }

        _buffer = result.Buffer;
        Refusal = "";
        Changed?.Invoke(this, new EditorContentChangedEventArgs(result.Buffer.Content));
    }

    public ValueTask DisposeAsync()
    {
        _watcher?.Dispose();
        return ValueTask.CompletedTask;
    }
}
