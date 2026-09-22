using Serilog;

namespace EtAlii.Adp.Editor.Plain;

/// <summary>
/// One open plain-text file: <see cref="TextFileBuffer"/> for reading, saving and every
/// refusal rule (encoding, size, binary), plus a watcher so an external edit reaches the
/// session as a <see cref="IEditorSession.Changed"/> event rather than a stale buffer.
/// </summary>
public sealed class PlainEditorSession : IEditorSession
{
    private static readonly ILogger _logger = Log.ForContext<PlainEditorSession>();

    private readonly string _path;
    private readonly FileSystemWatcher? _watcher;
    private TextFileBuffer? _buffer;
    private bool _saving;

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
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(path)) { EnableRaisingEvents = true };

            // EVERY EVENT A PUBLISH ACTUALLY RAISES, not just Changed. A write in place raises
            // Changed; a temp-then-replace publish - which is what AdpFileWriter does, and now
            // what this session's own saves do - raises Renamed as the scratch file takes the
            // destination's name, and Created where there was nothing before. Subscribing to
            // Changed alone meant an external save through the central writer never reached the
            // open editor: measured as a 60-second gRPC deadline in
            // EditorResolutionTests.ADslOpenAsDiagramAndAsTextAtOnce_BothStayTrueToTheFile, which
            // waits for the text view to hear a save it made itself.
            _watcher.Changed += (_, _) => OnExternalChange();
            _watcher.Created += (_, _) => OnExternalChange();
            _watcher.Renamed += (_, _) => OnExternalChange();
        }
    }

    /// <summary>The file's text, or empty when the open was refused - <see cref="Refusal"/> says why.</summary>
    public string Content => _buffer?.Content ?? "";

    /// <summary>Why the file did not open, or empty. A refusal is an answer, not a fault (Requirement 7.1).</summary>
    public string Refusal { get; private set; }

    public event EventHandler<EditorContentChangedEventArgs>? Changed;

    public async Task<string> SaveAsync(string newContent, CancellationToken cancellationToken = default)
    {
        if (_buffer is null)
        {
            return Refusal.Length > 0 ? Refusal : "The file is not open.";
        }

        // The session's own save must not bounce back as an "external" change.
        _saving = true;
        try
        {
            return await _buffer.SaveAsync(newContent, cancellationToken);
        }
        finally
        {
            _saving = false;
        }
    }

    private void OnExternalChange()
    {
        if (_saving)
        {
            return;
        }

        var result = TextFileBuffer.Open(_path);
        if (result.Buffer is null)
        {
            // The file became unreadable underneath the session - worth a line, and the last
            // good content stays in place rather than being replaced by nothing.
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
