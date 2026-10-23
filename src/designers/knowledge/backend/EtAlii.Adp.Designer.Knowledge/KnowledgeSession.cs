using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
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
/// <para>
/// <b>Its state is kept by job</b>: the edits not written yet and the writes that are to make them
/// in a <see cref="PendingEditQueue"/>, what the connection looks at in a <see cref="KnowledgeViewState"/>,
/// and the file in a <see cref="WatchedFile"/>. The first two are not thread-safe: they are touched
/// with the session's one lock held, together with what is shown.
/// </para>
/// </remarks>
internal sealed class KnowledgeSession : ITableDesignerSession
{
    private static readonly ILogger _logger = Log.ForContext<KnowledgeSession>();

    /// <summary>What an edit is refused with where nothing can write it: a session opened without a way to.</summary>
    private const string NoWriter = "This table cannot be changed here.";

    private readonly string _bodyPath;
    private readonly bool _besideItsRule;
    private readonly Func<string, KnowledgeRead> _read;
    private readonly Func<KnowledgeEditCommand, Task<CommandResult>>? _write;
    private readonly IKnowledgeDocumentStore? _documents;
    private readonly Lock _gate = new();

    /// <summary>The edits that are shown and not written yet, and the writes that are to make them.</summary>
    private readonly PendingEditQueue _edits = new();

    private KnowledgeBody? _body;

    /// <summary>What the table shows: the body's own table, or that with the pending edits made to it.</summary>
    private FblModel _shownModel = new([], [], Unreadable: false);
    private KnowledgeTable _shown = KnowledgeTable.Empty;

    /// <summary>What is shown with the computed sides of its relations filled in: what the view is made of.</summary>
    private KnowledgeTable _view = KnowledgeTable.Empty;

    /// <summary>The tables this file's relations point at, by their paths, as they were last read. A target that was written through ADP is read again.</summary>
    private readonly Dictionary<string, KnowledgeTarget> _targets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What this connection looks at: its view, the rows it keeps in sight, its search and its window.</summary>
    private readonly KnowledgeViewState _viewState;

    private readonly ProjectRoot _root;

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
    /// <param name="root">The project the file is in: how far is looked for rows that relate to a row of this file. The default, nowhere.</param>
    /// <param name="time">The clock a refused read waits on before it is tried again; the system's when null.</param>
    internal KnowledgeSession(string bodyPath, Func<string, KnowledgeRead> read, Func<KnowledgeEditCommand, Task<CommandResult>>? write = null, IKnowledgeDocumentStore? documents = null, ProjectRoot root = default, TimeProvider? time = null, ProjectFileEvents? events = null)
    {
        _root = root;
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
        _viewState = new KnowledgeViewState(_shown.ViewOrDefault("")?.Id ?? "");

        // Every event a publish raises reaches OnExternalChangeAsync, an overflow included, one
        // re-read at a time; a refused read is tried again, so a Deleted presented by a
        // temp-then-replace is safe to hear.
        Watched = new WatchedFile(bodyPath, OnExternalChangeAsync, time ?? TimeProvider.System, events);

        // After the watched file exists: a reload goes through it.
        _documents?.Reloaded += OnReloaded;
    }

    /// <summary>Why the file could not be read from disk the last time it was tried, or empty.</summary>
    public string Refusal { get; private set; }

    /// <summary>The watched file; for the guards, which reach its watcher.</summary>
    internal WatchedFile Watched { get; }

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
            return new TableBaseline(structure.Title, structure.Columns, structure.Views, structure.Settings, structure.RowCount, Findings(_body), structure.ReadOnlyReason);
        }
    }

    public void SetWindow(int first, int count)
    {
        TableChange? rows;
        lock (_gate)
        {
            _viewState.SetWindow(first, count);
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

            _viewState.Open(viewId);

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

            // A search is this connection's own and changes no file: a table that cannot be edited is searched all the same.
            if (gesture.Kind == "search")
            {
                _viewState.SearchFor(gesture.Values.Count > 0 ? gesture.Values[0] : "");
                changes.Add(new TableEditSettled(editId, Written: true));
                changes.AddRange(Everything());
            }
        }

        if (changes.Count > 0)
        {
            Raise([.. changes]);
            return "";
        }

        lock (_gate)
        {
            var refusal = Unplanned(_body, gesture) ?? Make(editId, gesture, changes);
            if (refusal.Length > 0)
            {
                return refusal;
            }
        }

        Raise([.. changes]);
        return "";
    }

    /// <summary>Why a gesture is answered before it is planned: a table that is not to be written, or a question for its author first. Null when it is planned. Called with the lock held.</summary>
    private string? Unplanned(KnowledgeBody body, TableGesture gesture)
    {
        // A file that cannot be read, or is of another version, refuses every edit and is never written.
        if (body.ReadOnlyReason is { Length: > 0 } reason)
        {
            return reason;
        }

        if (_write is null)
        {
            return NoWriter;
        }

        // Rows of other tables may relate to a row that is about to go: its author is told how many first.
        if (gesture.Kind == "deleteRows" && _root.FullPath.Length > 0 && gesture.Settings.GetValueOrDefault("confirmed") != "true")
        {
            IReadOnlyList<string> ids = gesture.Values.Count > 0 ? gesture.Values : [gesture.RowId];
            if (KnowledgeProject.RowsRelatingTo(_root, _bodyPath, ids) is > 0 and var relating)
            {
                return KnowledgeCellEdits.AskFirst(relating, ids.Count);
            }
        }

        // A relation asked for without the table it relates to is answered with the tables there are to choose from.
        if (gesture.Kind == "addRelation" && string.IsNullOrWhiteSpace(gesture.Settings.GetValueOrDefault("target")))
        {
            return KnowledgeColumnEdits.ChooseTarget(KnowledgeProject.Targets(_root, _bodyPath));
        }

        return null;
    }

    /// <summary>Plans a gesture and takes what it changes as made, adding what the connection is to be sent to <paramref name="changes"/>. Called with the lock held.</summary>
    /// <returns>Why it is refused, or empty when it is made.</returns>
    private string Make(ShortGuid editId, TableGesture gesture, List<TableChange> changes)
    {
        var files = new KnowledgeSurroundings(_bodyPath, TargetOf, TargetOf);
        var edit = KnowledgeEdits.Plan(_shown, _viewState.ViewId, gesture, () => ShortGuid.NewShortGuid().ToString(), _besideItsRule, files);
        if (edit.IsRefused)
        {
            return edit.Refusal;
        }

        if (edit.Changes.Count == 0)
        {
            // Nothing to write: the edit is settled as it is made.
            changes.Add(new TableEditSettled(editId, Written: true));
            return "";
        }

        if (edit.NewViewId.Length > 0)
        {
            // A view just made is the view its maker looks at.
            _viewState.Open(edit.NewViewId);
        }

        if (edit.NewRowId.Length > 0)
        {
            _viewState.Keep(edit.NewRowId);
        }

        Accept(editId, edit.Changes, gesture.Kind == "setCell" ? (gesture.RowId, gesture.ColumnId) : null, edit.Others);
        changes.AddRange(Everything());
        return "";
    }

    /// <summary>Takes an edit as made: shown from now on, and queued to be written. Called with the lock held.</summary>
    private void Accept(ShortGuid editId, IReadOnlyList<ModelChange> edit, (string RowId, string ColumnId)? cell, IReadOnlyList<KnowledgeOtherFile>? others = null)
    {
        var generation = _edits.Add(editId, edit, cell);
        Project(edit);

        // Its write is queued behind those already waiting.
        var command = new KnowledgeEditCommand(_bodyPath, edit, others ?? []);
        _edits.Enqueue(() => WriteAsync(editId, generation, command));
    }

    /// <summary>Writes one edit when its turn has come, unless it was taken back meanwhile, and settles it.</summary>
    private async Task WriteAsync(ShortGuid editId, int generation, KnowledgeEditCommand command)
    {
        lock (_gate)
        {
            if (!_edits.TryStart(generation))
            {
                // Taken back with an earlier edit that was refused: there is nothing to write.
                return;
            }
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

        await SettleAsync(editId, generation, result).ConfigureAwait(false);
    }

    /// <summary>What became of one edit's write: the file is read again, and the connection is told.</summary>
    private async Task SettleAsync(ShortGuid editId, int generation, CommandResult result)
    {
        // Read outside the lock, as every re-read is: a refused read waits before it is tried again.
        (KnowledgeRead read, _) = await Watched.ReadAsync(_read, reading => reading.Body is not null).ConfigureAwait(false);

        List<TableChange> changes = [];
        lock (_gate)
        {
            if (_edits.Settle(editId, generation, result.IsSuccess) is not { } takenBack)
            {
                return;
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
            // Through the watched file, so it queues behind a re-read already running rather than racing it.
            _ = Watched.Notify();
            return;
        }

        // A table one of this file's relations points at was written: what it calls its rows, and
        // which of them name rows here, is read again and shown.
        List<TableChange> changes = [];
        lock (_gate)
        {
            if (!_targets.Remove(Path.GetFullPath(path)) || _body is null)
            {
                return;
            }

            Relate();
            changes.AddRange(Everything());
        }

        Raise([.. changes]);
    }

    /// <summary>What a relation property of this file points at, read once and kept until the target is written again. Called with the lock held.</summary>
    private KnowledgeTarget? TargetOf(KnowledgeProperty property) => property.Type is KnowledgeRelationType ? TargetOf(property.TargetFile) : null;

    /// <summary>What a target named from this file is: a relative path, or <c>.</c> for the file itself. Called with the lock held.</summary>
    private KnowledgeTarget TargetOf(string targetFile)
    {
        var path = KnowledgeRelations.TargetPath(_bodyPath, targetFile);
        if (string.Equals(path, Path.GetFullPath(_bodyPath), StringComparison.OrdinalIgnoreCase))
        {
            return new KnowledgeTarget(path, _shown);
        }

        if (!_targets.TryGetValue(path, out var target))
        {
            var read = _read(path);
            target = read.Body is null
                ? new KnowledgeTarget(path, null, $"'{targetFile}' is not there.")
                : read.Body.Unreadable.Length > 0
                    ? new KnowledgeTarget(path, null, $"'{targetFile}' cannot be read as a knowledge file.")
                    : new KnowledgeTarget(path, read.Body.Table);
            _targets[path] = target;
        }

        return target;
    }

    /// <summary>What a related row is called, for the cells and the groups of a relation.</summary>
    private string LabelOf(string propertyId, string rowId) =>
        _view.Properties.FirstOrDefault(candidate => candidate.Id == propertyId) is { } property ? KnowledgeRelations.LabelOf(TargetOf(property), rowId) : "";

    /// <summary>Fills in the computed sides of the relations of what is shown. Called with the lock held, whenever what is shown changed.</summary>
    private void Relate() => _view = KnowledgeRelations.WithComputed(_shown, TargetOf);

    /// <summary>Internal for the guards, which call it as the watcher would.</summary>
    internal async Task OnExternalChangeAsync()
    {
        int writesStarted;
        lock (_gate)
        {
            if (_edits.Writing)
            {
                // This session's own write is landing: the file is read when it has, once.
                return;
            }

            writesStarted = _edits.WritesStarted;
        }

        (KnowledgeRead read, int reads) = await Watched.ReadAsync(_read, reading => reading.Body is not null).ConfigureAwait(false);
        if (read.Body is not null && reads > 1)
        {
            // Worded as the editor sessions and the diagram stores word theirs, so one search finds every retried read.
            _logger.Information("Read {Path} on attempt {Attempt} of {Attempts}", _bodyPath, reads, WatchedFile.ReadAttempts);
        }

        List<TableChange> changes = [];
        lock (_gate)
        {
            if (_edits.Writing || writesStarted != _edits.WritesStarted)
            {
                // A write of this session started while this was reading, so what was read may be the
                // file from before it; the write's own re-read, made after it, is the newer (T282).
                return;
            }

            if (read.Body is null)
            {
                // Out of attempts: the file is genuinely unreadable rather than mid-replace. The
                // last good reading stays, and the session stops claiming the file opens.
                _logger.Warning("The externally changed {Path} no longer opens after {Attempts} attempts: {Refusal}", _bodyPath, WatchedFile.ReadAttempts, read.Refusal);
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
            writes = _edits.Writes;
        }

        await writes.ConfigureAwait(false);

        _documents?.Reloaded -= OnReloaded;

        Watched.Dispose();
    }

    /// <summary>Takes a reading as what the table shows, with the edits still waiting made to it again. Called with the lock held, or before anything else can call.</summary>
    private void Show(KnowledgeBody? body)
    {
        _body = body;
        _shownModel = body?.Model ?? new FblModel([], [], Unreadable: false);
        _shown = body?.Table ?? KnowledgeTable.Empty;
        foreach (var pending in _edits.Pending)
        {
            Project(pending.Changes);
        }

        Relate();
    }

    private void Project(IReadOnlyList<ModelChange> changes)
    {
        _shownModel = KnowledgeProjection.Apply(_shownModel, changes, _besideItsRule);
        _shown = KnowledgeModelReader.Read(_shownModel);
        Relate();
    }

    /// <summary>The structure, the findings and the window's lines: what a connection is sent when the table changed.</summary>
    private List<TableChange> Everything()
    {
        List<TableChange> changes = [Structure()];
        if (_body is not null)
        {
            changes.Add(new TableFindingsChanged(Findings(_body)));
        }

        if (RowsOfWindow() is { } rows)
        {
            changes.Add(rows);
        }

        return changes;
    }

    /// <summary>
    /// What the table has to report: what the reading of the file found, and what the designer's own
    /// rules find in the table as it is shown - so an edit that puts something right is seen to at once.
    /// </summary>
    private List<TableFinding> Findings(KnowledgeBody body) =>
        // A file of a version this application does not know is one finding of its own, not the reader's complaint about its first line.
        body.Unreadable == KnowledgeBody.AnotherVersion ? [new TableFinding(KnowledgeValidator.NewerVersion, TableFindingSeverity.Warning, KnowledgeBody.AnotherVersion)]
        : body.Unreadable.Length > 0 ? [.. KnowledgeTableMapper.Findings(body.Model)] : [.. KnowledgeTableMapper.Findings(body.Model), .. KnowledgeValidator.Validate(_view, _shownModel, TargetOf)];

    private TableStructureChanged Structure() => KnowledgeTableMapper.Structure(
        _view,
        _body?.ReadOnlyReason ?? "",
        _viewState.ViewId,
        Lines().Count,
        TargetOf,
        _viewState.InSight(_view, LabelOf),
        _viewState.Search);

    /// <summary>
    /// The lines of the view this connection shows, in order: its rows filtered, sorted and grouped
    /// or nested as the view says, and - where the table can be edited - the lines a new row is added at.
    /// </summary>
    private List<TableRow> Lines() =>
        _viewState.Lines(_view, _edits.UnwrittenCells(), editable: _body is { ReadOnlyReason.Length: 0 }, LabelOf);

    /// <summary>The lines the connection has in sight, or null while it has not said which.</summary>
    private TableRowsChanged? RowsOfWindow() => _body is null ? null : _viewState.RowsInWindow(Lines);

    private void Raise(params TableChange?[] changes)
    {
        var real = changes.OfType<TableChange>().ToList();
        if (real.Count > 0)
        {
            Changed?.Invoke(this, new TableChangedEventArgs(real));
        }
    }
}
