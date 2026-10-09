using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using Serilog;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// One open knowledge file on one connection: the table as that connection sees it - its own view
/// and its own window of lines - kept true to the file on disk, and the author's edits of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The file is the truth.</b> Everything the table shows is in the file; the session holds the
/// last reading, and when the file changes - through ADP or through any other program - it reads
/// it again and pushes what the connection is looking at.
/// </para>
/// <para>
/// <b>An edit is shown at once and written behind</b> (the user's ruling of 2026-10-09). A gesture
/// is turned into the changes it means for the file; those are made at once to what the table
/// shows (<see cref="KnowledgeProjection"/>) and queued for the file, where they are written one
/// edit at a time, in the order they were made. When a write lands the session reads the file
/// again and says the edit is written. When a write is refused the edit is taken back, and every
/// later edit still waiting is taken back with it: each was made on top of the one that failed.
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

    /// <summary>What an edit is refused with where nothing can write it: a session opened without a way to.</summary>
    private const string NoWriter = "This table cannot be changed here.";

    private readonly string _bodyPath;
    private readonly bool _besideItsRule;
    private readonly Func<string, KnowledgeRead> _read;
    private readonly Func<KnowledgeEditCommand, Task<CommandResult>>? _write;
    private readonly IKnowledgeDocumentStore? _documents;
    private readonly FileSystemWatcher? _watcher;
    private readonly Lock _gate = new();

    /// <summary>The edits that are shown and not written yet, in the order they were made.</summary>
    private readonly List<PendingEdit> _pending = [];

    private KnowledgeBody? _body;

    /// <summary>What the table shows: the body's own table, or that with the pending edits made to it.</summary>
    private FblModel _shownModel = new([], [], Unreadable: false);
    private KnowledgeTable _shown = KnowledgeTable.Empty;

    private string _viewId;

    /// <summary>The rows added while this view was open: they stay in sight until it is next opened, whatever its filter says.</summary>
    private readonly HashSet<string> _kept = new(StringComparer.Ordinal);
    private (int First, int Count)? _window;

    /// <summary>The writes still to come, each waiting for the one before it.</summary>
    private Task _writes = Task.CompletedTask;

    /// <summary>Raised by one when the pending edits are taken back, so the writes already queued for them do nothing.</summary>
    private int _generation;

    /// <summary>True while an edit of this session is being written: the file is then read when that write ends.</summary>
    private bool _writing;

    public KnowledgeSession(string bodyPath)
        : this(bodyPath, KnowledgeDocumentStore.Read)
    {
    }

    /// <summary>
    /// With the read supplied, so a test can refuse a re-read exactly as often as it chooses - the
    /// window the retry exists for is too narrow to land in on purpose with a real file.
    /// </summary>
    /// <param name="bodyPath">The knowledge file.</param>
    /// <param name="read">How the file is read.</param>
    /// <param name="write">How an edit is written: through the project's history, so it can be undone. Null for a session that only shows.</param>
    /// <param name="documents">Where the module's commands say a file was written, or null.</param>
    internal KnowledgeSession(string bodyPath, Func<string, KnowledgeRead> read, Func<KnowledgeEditCommand, Task<CommandResult>>? write = null, IKnowledgeDocumentStore? documents = null)
    {
        ArgumentNullException.ThrowIfNull(bodyPath);
        ArgumentNullException.ThrowIfNull(read);
        _bodyPath = bodyPath;
        _besideItsRule = KnowledgeDefinition.AddsBesideItsRule(Path.GetExtension(bodyPath));
        _read = read;
        _write = write;
        _documents = documents;

        var opened = _read(bodyPath);
        Refusal = opened.Refusal;
        Show(opened.Body);

        // The view the file was left in, by name from here on: another connection leaving the file
        // in another view does not move this one.
        _viewId = _shown.ViewOrDefault("")?.Id ?? "";

        _documents?.Reloaded += OnReloaded;

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

    /// <summary>The table as it is shown now, edits not yet written included. Internal for the guards.</summary>
    internal KnowledgeTable Shown
    {
        get
        {
            lock (_gate)
            {
                return _shown;
            }
        }
    }

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

            var structure = Structure();
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
            if (_body is null || _shown.Views.All(view => view.Id != viewId))
            {
                // A view that is not there - deleted while this call was on its way - changes nothing.
                return;
            }

            _viewId = viewId;
            _kept.Clear();

            // The view a file is left in is in the file: switching is an edit, written and undone as any other.
            if (_write is not null && _body.ReadOnlyReason.Length == 0 && _shown.ActiveViewId != viewId)
            {
                Accept(ShortGuid.NewShortGuid(), [new ModelChange.Set(KnowledgeEdits.TableId, new Dictionary<string, object?> { ["activeView"] = viewId })], null);
            }

            changes.Add(Structure());
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

        List<TableChange> changes = [];
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

            if (_write is null)
            {
                return NoWriter;
            }

            var edit = KnowledgeEdits.Plan(_shown, _viewId, gesture, () => ShortGuid.NewShortGuid().ToString(), _besideItsRule);
            if (edit.IsRefused)
            {
                return edit.Refusal;
            }

            if (edit.Changes.Count == 0)
            {
                // Nothing to write: the edit is settled as it is made.
                changes.Add(new TableEditSettled(editId, Written: true));
            }
            else
            {
                if (edit.NewViewId.Length > 0)
                {
                    // A view just made is the view its maker looks at.
                    _viewId = edit.NewViewId;
                    _kept.Clear();
                }

                if (edit.NewRowId.Length > 0)
                {
                    _kept.Add(edit.NewRowId);
                }

                Accept(editId, edit.Changes, gesture.Kind == "setCell" ? (gesture.RowId, gesture.ColumnId) : null);
                changes.AddRange(Everything());
            }
        }

        Raise([.. changes]);
        return "";
    }

    /// <summary>Takes an edit as made: shown from now on, and queued to be written. Called with the lock held.</summary>
    private void Accept(ShortGuid editId, IReadOnlyList<ModelChange> edit, (string RowId, string ColumnId)? cell)
    {
        _pending.Add(new PendingEdit(editId, edit, cell));
        Project(edit);
        Queue(editId, edit);
    }

    /// <summary>Queues one edit's write behind those already waiting. Called with the lock held.</summary>
    private void Queue(ShortGuid editId, IReadOnlyList<ModelChange> edit)
    {
        var generation = _generation;
        var command = new KnowledgeEditCommand(_bodyPath, edit);
        _writes = _writes.ContinueWith(
            async _ =>
            {
                lock (_gate)
                {
                    if (generation != _generation)
                    {
                        // Taken back with an earlier edit that was refused: there is nothing to write.
                        return;
                    }

                    _writing = true;
                }

                CommandResult result;
                try
                {
                    result = await _write!(command).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    result = CommandResult.Failure($"{Path.GetFileName(_bodyPath)} could not be written: {exception.Message}");
                }

                Settle(editId, generation, result);
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default).Unwrap();
    }

    /// <summary>What became of one edit's write: the file is read again, and the connection is told.</summary>
    private void Settle(ShortGuid editId, int generation, CommandResult result)
    {
        // Read outside the lock, as every re-read is: a refused read waits before it is tried again.
        var read = ReadWithRetry(() => _read(_bodyPath), ReadAttempts, BetweenReadAttempts);

        List<TableChange> changes = [];
        lock (_gate)
        {
            _writing = false;
            if (generation != _generation)
            {
                return;
            }

            IReadOnlyList<ShortGuid> takenBack = [];
            _pending.RemoveAll(pending => pending.EditId == editId);
            if (!result.IsSuccess)
            {
                // Every edit still waiting was made on top of this one, so it goes with it.
                takenBack = [.. _pending.Select(pending => pending.EditId)];
                _pending.Clear();
                _generation++;
            }

            if (read.Body is not null)
            {
                Refusal = "";
                Show(read.Body);
            }
            else
            {
                Refusal = read.Refusal;
                Show(_body);
            }

            changes.Add(new TableEditSettled(editId, result.IsSuccess, result.IsSuccess ? "" : result.Error, takenBack));
            changes.AddRange(Everything());
        }

        Raise([.. changes]);
    }

    private void OnReloaded(string path)
    {
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_bodyPath), StringComparison.OrdinalIgnoreCase))
        {
            OnExternalChange();
        }
    }

    /// <summary>Internal for the guards, which call it as the watcher would.</summary>
    internal void OnExternalChange()
    {
        lock (_gate)
        {
            if (_writing)
            {
                // This session's own write is landing: the file is read when it has, once.
                return;
            }
        }

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

            Refusal = "";
            Show(read.Body);
            changes.AddRange(Everything());
        }

        Raise([.. changes]);
    }

    /// <summary>
    /// Ends the session once the edits still waiting have been written: closing a table does not
    /// lose what its author just typed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Task writes;
        lock (_gate)
        {
            writes = _writes;
        }

        await writes.ConfigureAwait(false);

        _documents?.Reloaded -= OnReloaded;

        _watcher?.Dispose();
    }

    /// <summary>Takes a reading as what the table shows, with the edits still waiting made to it again. Called with the lock held, or before anything else can call.</summary>
    private void Show(KnowledgeBody? body)
    {
        _body = body;
        _shownModel = body?.Model ?? new FblModel([], [], Unreadable: false);
        _shown = body?.Table ?? KnowledgeTable.Empty;
        foreach (var pending in _pending)
        {
            Project(pending.Changes);
        }
    }

    private void Project(IReadOnlyList<ModelChange> changes)
    {
        _shownModel = KnowledgeProjection.Apply(_shownModel, changes, _besideItsRule);
        _shown = KnowledgeModelReader.Read(_shownModel);
    }

    /// <summary>The structure, the findings and the window's lines: what a connection is sent when the table changed.</summary>
    private List<TableChange> Everything()
    {
        List<TableChange> changes = [Structure()];
        if (_body is not null)
        {
            changes.Add(new TableFindingsChanged(KnowledgeTableMapper.Findings(_body.Model)));
        }

        if (RowsOfWindow() is { } rows)
        {
            changes.Add(rows);
        }

        return changes;
    }

    private TableStructureChanged Structure() => KnowledgeTableMapper.Structure(_shown, _body?.ReadOnlyReason ?? "", _viewId, Lines().Count);

    /// <summary>
    /// The lines of the view this connection shows, in order: its rows filtered, sorted and grouped
    /// or nested as the view says, and - where the table can be edited - the lines a new row is added at.
    /// </summary>
    private List<TableRow> Lines()
    {
        var unwritten = _pending.Where(pending => pending.Cell is not null).Select(pending => pending.Cell!.Value).ToHashSet();
        return KnowledgeViewEngine.Lines(_shown, _shown.ViewOrDefault(_viewId), _kept, unwritten, editable: _body is { ReadOnlyReason.Length: 0 });
    }

    /// <summary>The lines the connection has in sight, or null while it has not said which.</summary>
    private TableRowsChanged? RowsOfWindow()
    {
        if (_body is null || _window is not { } window)
        {
            return null;
        }

        var lines = Lines();
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

    /// <summary>An edit that is shown and not written yet.</summary>
    /// <param name="EditId">The id its gesture arrived under.</param>
    /// <param name="Changes">What it changes in the file.</param>
    /// <param name="Cell">The cell it sets, which is marked as not written yet; null for any other edit.</param>
    private sealed record PendingEdit(ShortGuid EditId, IReadOnlyList<ModelChange> Changes, (string RowId, string ColumnId)? Cell);
}
