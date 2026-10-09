using EtAlii.Adp.Designer.TableModel;
using Serilog;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// One open knowledge file on one connection: the table as that connection sees it - its own view
/// and its own window of lines - kept true to the file on disk.
/// </summary>
/// <remarks>
/// <para>
/// <b>The file is the truth, and the session only reads it.</b> Everything the table shows is in
/// the file; the session holds the last reading, and when the file changes - through ADP or
/// through any other program - it reads it again and pushes what the connection is looking at.
/// </para>
/// <para>
/// <b>It meets a text editor session's obligations towards its file</b>, under the same tests: it
/// subscribes to every event a publish raises before the watcher is enabled, reads again when the
/// watcher says it lost events, and retries a read that was refused, because a temp-then-replace
/// publish presents the name as absent for a moment and a single refused read would throw the
/// only notification for that write away.
/// </para>
/// </remarks>
internal sealed class KnowledgeSession : IDesignerSession
{
    private static readonly ILogger _logger = Log.ForContext<KnowledgeSession>();

    /// <summary>
    /// How many times a re-read is tried before the notification is given up on, and how long
    /// between attempts - the editor sessions' figures, so that whether they need changing is one question.
    /// </summary>
    private const int ReadAttempts = 3;

    private static readonly TimeSpan BetweenReadAttempts = TimeSpan.FromMilliseconds(20);

    private readonly string _bodyPath;
    private readonly Func<string, KnowledgeRead> _read;
    private readonly FileSystemWatcher? _watcher;
    private readonly Lock _gate = new();

    private KnowledgeBody? _body;
    private string _viewId = "";
    private (int First, int Count)? _window;

    public KnowledgeSession(string bodyPath)
        : this(bodyPath, KnowledgeDocumentStore.Read)
    {
    }

    /// <summary>
    /// With the read supplied, so a test can refuse a re-read exactly as often as it chooses - the
    /// window the retry exists for is too narrow to land in on purpose with a real file.
    /// </summary>
    internal KnowledgeSession(string bodyPath, Func<string, KnowledgeRead> read)
    {
        ArgumentNullException.ThrowIfNull(bodyPath);
        ArgumentNullException.ThrowIfNull(read);
        _bodyPath = bodyPath;
        _read = read;

        var opened = _read(bodyPath);
        _body = opened.Body;
        Refusal = opened.Refusal;

        var directory = Path.GetDirectoryName(bodyPath);
        if (directory is not null && Directory.Exists(directory))
        {
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(bodyPath));

            // Every event a publish actually raises, not just Changed: a temp-then-replace publish
            // raises Renamed as the scratch file takes the destination's name, Created where there
            // was nothing, and may present the transition as a Deleted - which is only safe to
            // subscribe because the refused re-read below retries.
            _watcher.Changed += (_, _) => OnExternalChange();
            _watcher.Created += (_, _) => OnExternalChange();
            _watcher.Renamed += (_, _) => OnExternalChange();
            _watcher.Deleted += (_, _) => OnExternalChange();

            // The one signal a watcher gives when it has dropped events. Which were dropped is
            // unknowable, so the file is read again; logging alone would leave the table showing
            // what it held before.
            _watcher.Error += (_, args) =>
            {
                _logger.Warning(args.GetException(), "The watcher for {Path} stumbled; reading it again", _bodyPath);
                OnExternalChange();
            };

            // Subscribed BEFORE the watcher is enabled: enabling first leaves it live with no
            // handlers attached, and anything raised in that window is received by nobody.
            _watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>Why the file could not be read from disk the last time it was tried, or empty.</summary>
    public string Refusal { get; private set; }

    public event EventHandler<TableChangedEventArgs>? Changed;

    public TableBaseline Baseline()
    {
        lock (_gate)
        {
            if (_body is null)
            {
                // Nothing was ever read: the table is its refusal, and nothing else.
                return new TableBaseline("", [], [], new TableViewSettings(""), 0, [], Refusal.Length > 0 ? Refusal : "The file cannot be read.");
            }

            var structure = KnowledgeTableMapper.Structure(_body, _viewId, Lines(_body).Count);
            return new TableBaseline(structure.Title, structure.Columns, structure.Views, structure.Settings, structure.RowCount, KnowledgeTableMapper.Findings(_body.Model), structure.ReadOnlyReason);
        }
    }

    public void SetWindow(int first, int count)
    {
        TableChange? rows;
        lock (_gate)
        {
            _window = (Math.Max(0, first), Math.Max(0, count));
            rows = RowsOfWindow();
        }

        Raise(rows);
    }

    public void SetActiveView(string viewId)
    {
        ArgumentNullException.ThrowIfNull(viewId);

        List<TableChange> changes = [];
        lock (_gate)
        {
            if (_body is null || _body.Table.Views.All(view => view.Id != viewId))
            {
                // A view that is not there - deleted while this call was on its way - changes nothing.
                return;
            }

            _viewId = viewId;
            changes.Add(KnowledgeTableMapper.Structure(_body, _viewId, Lines(_body).Count));
            if (RowsOfWindow() is { } rows)
            {
                changes.Add(rows);
            }
        }

        Raise([.. changes]);
    }

    public string Edit(ShortGuid editId, TableGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        lock (_gate)
        {
            if (_body is null)
            {
                return Refusal.Length > 0 ? Refusal : "The file cannot be read.";
            }

            // A file that cannot be read, or is of another version, refuses every edit and is never written.
            if (_body.ReadOnlyReason is { Length: > 0 } reason)
            {
                return reason;
            }
        }

        return "This table cannot be changed here yet.";
    }

    /// <summary>Internal for the guards, which call it as the watcher would.</summary>
    internal void OnExternalChange()
    {
        var reads = 0;
        var read = ReadWithRetry(
            () =>
            {
                reads++;
                return _read(_bodyPath);
            },
            ReadAttempts,
            BetweenReadAttempts);
        if (read.Body is not null && reads > 1)
        {
            // Worded as the editor sessions and the diagram stores word theirs, so one search finds every retried read.
            _logger.Information("Read {Path} on attempt {Attempt} of {Attempts}", _bodyPath, reads, ReadAttempts);
        }

        List<TableChange> changes = [];
        lock (_gate)
        {
            if (read.Body is null)
            {
                // Out of attempts: the file is genuinely unreadable rather than mid-replace. The
                // last good reading stays, and the session stops claiming the file opens.
                _logger.Warning("The externally changed {Path} no longer opens after {Attempts} attempts: {Refusal}", _bodyPath, ReadAttempts, read.Refusal);
                Refusal = read.Refusal;
                return;
            }

            // The same bytes are the same table: a write that changed nothing pushes nothing.
            if (_body is not null && Refusal.Length == 0 && _body.Bytes.AsSpan().SequenceEqual(read.Body.Bytes))
            {
                return;
            }

            _body = read.Body;
            Refusal = "";
            changes.Add(KnowledgeTableMapper.Structure(_body, _viewId, Lines(_body).Count));
            changes.Add(new TableFindingsChanged(KnowledgeTableMapper.Findings(_body.Model)));
            if (RowsOfWindow() is { } rows)
            {
                changes.Add(rows);
            }
        }

        Raise([.. changes]);
    }

    public ValueTask DisposeAsync()
    {
        _watcher?.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The lines of the view, in order: the rows as the file has them, and - where the table can be
    /// edited - the line a new row is added at. Filtering, sorting and grouping are the view
    /// engine's, which arrives with its own task; until then a view shows every row in the file's order.
    /// </summary>
    private static List<TableRow> Lines(KnowledgeBody body)
    {
        var lines = body.Table.Rows.Select(KnowledgeTableMapper.Row).ToList();
        if (body.ReadOnlyReason.Length == 0)
        {
            lines.Add(new TableRow("", IsNewRow: true));
        }

        return lines;
    }

    /// <summary>The lines the connection has in sight, or null while it has not said which.</summary>
    private TableRowsChanged? RowsOfWindow()
    {
        if (_body is null || _window is not { } window)
        {
            return null;
        }

        var lines = Lines(_body);
        var first = Math.Min(window.First, lines.Count);
        return new TableRowsChanged(first, lines.GetRange(first, Math.Min(window.Count, lines.Count - first)), lines.Count);
    }

    private void Raise(params TableChange?[] changes)
    {
        var real = changes.OfType<TableChange>().ToList();
        if (real.Count > 0)
        {
            Changed?.Invoke(this, new TableChangedEventArgs(real));
        }
    }

    /// <summary>Reads until it succeeds or runs out of attempts.</summary>
    private static KnowledgeRead ReadWithRetry(Func<KnowledgeRead> read, int attempts, TimeSpan between)
    {
        for (var attempt = 1; ; attempt++)
        {
            var result = read();
            if (result.Body is not null || attempt >= attempts)
            {
                return result;
            }

            Thread.Sleep(between);
        }
    }
}
