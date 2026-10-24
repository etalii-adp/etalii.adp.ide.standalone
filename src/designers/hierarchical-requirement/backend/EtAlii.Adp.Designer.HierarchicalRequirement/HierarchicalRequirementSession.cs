using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using Serilog;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// One open requirement document on one connection, as a hierarchy surface (spec 029 R8, R13): the whole read model,
/// kept true to the Markdown file and its registration on disk, and the person's gestures.
/// </summary>
/// <remarks>
/// <para>
/// <b>The files are the truth.</b> Opening writes nothing. The session reads the body and the registration, when there is
/// one, and reads them again whenever either changes, through ADP or any other program; it sends the whole model each
/// time, and nothing when the bytes are those it already holds - which is how its own writes are passed over.
/// </para>
/// <para>
/// <b>An edit of the body, of a text or of the hierarchy, goes through the project's history</b>, planned against the
/// bytes this session's page was made from; a file that holds others by the time it runs is not written, and the page is
/// read again (FR-024). Undo and redo are the application's, and a page hears of them through the module's store.
/// </para>
/// <para>
/// <b>A file that stops being readable keeps the last page</b>, read-only and saying why, until it can be read again:
/// what the person was looking at does not vanish under them (FR-022).
/// </para>
/// <para>
/// <b>The registration is created with the first setting the person changes</b>, outside the history, as the binding's
/// <c>createOnFirstPlacement</c> says (FBL 0.7 section 8.4, FR-004). Collapsing is such a setting, and is no undo step
/// either: the <c>collapsed</c> block is declared outside the history (FR-010).
/// </para>
/// </remarks>
internal sealed class HierarchicalRequirementSession : IHierarchySurfaceSession
{
    private static readonly ILogger _logger = Log.ForContext<HierarchicalRequirementSession>();

    private readonly string _bodyPath;
    private readonly string _registrationPath;
    private readonly IFileWriter _writer;
    private readonly Func<ICommand, Task<CommandResult>>? _execute;
    private readonly IHierarchicalRequirementDocumentStore? _documents;
    private readonly Lock _gate = new();

    /// <summary>One re-read at a time across both files, so an older reading never lands after a newer one.</summary>
    private readonly SemaphoreSlim _rereads = new(1, 1);

    private Reading _reading;

    /// <summary>The bytes a command of this module last said it writes to the body and to the registration, by path.</summary>
    private readonly Dictionary<string, byte[]> _announced = new(StringComparer.Ordinal);
    private Task _writes = Task.CompletedTask;
    private int _writing;
    private int _writesStarted;

    /// <param name="bodyPath">The Markdown file.</param>
    /// <param name="registrationPath">Its registration, or null when it has none yet: one is then created beside it, named after it, with the first setting.</param>
    /// <param name="writer">How a file is published.</param>
    /// <param name="execute">Runs a command through the project's history; without it the text cannot be changed.</param>
    /// <param name="documents">The module's store, through which a command that wrote a file says so.</param>
    /// <param name="time">The clock a refused read waits on before it is tried again; the system's when null.</param>
    /// <param name="events">The project roots' event sources, through which a file inside a watched root is heard.</param>
    public HierarchicalRequirementSession(
        string bodyPath,
        string? registrationPath,
        IFileWriter writer,
        Func<ICommand, Task<CommandResult>>? execute = null,
        IHierarchicalRequirementDocumentStore? documents = null,
        TimeProvider? time = null,
        ProjectFileEvents? events = null)
    {
        ArgumentNullException.ThrowIfNull(bodyPath);
        ArgumentNullException.ThrowIfNull(writer);
        _bodyPath = bodyPath;
        _registrationPath = registrationPath ?? Path.ChangeExtension(bodyPath, ".adp");
        _writer = writer;
        _execute = execute;
        _documents = documents;
        var files = ReadFiles();
        _reading = Build(files, HierarchySurfaceCauses.Open) ?? Unreadable(files.Refusal);

        Body = new WatchedFile(_bodyPath, OnExternalChangeAsync, time ?? TimeProvider.System, events);
        Registration = new WatchedFile(_registrationPath, OnExternalChangeAsync, time ?? TimeProvider.System, events);
        if (_documents is not null)
        {
            _documents.Writing += OnWriting;
            _documents.Reloaded += OnReloaded;
        }
    }

    /// <summary>The watched Markdown file.</summary>
    private WatchedFile Body { get; }

    /// <summary>The watched registration, which may not exist yet.</summary>
    private WatchedFile Registration { get; }

    /// <summary>The writes still running; for the guards, which wait for them.</summary>
    internal Task Writes
    {
        get
        {
            lock (_gate)
            {
                return _writes;
            }
        }
    }

    public event EventHandler<HierarchySurfaceChangedEventArgs>? Changed;

    public HierarchySurfaceModel Model()
    {
        lock (_gate)
        {
            return _reading.Model;
        }
    }

    public string Gesture(ShortGuid editId, HierarchySurfaceGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (TextOperations.Handles(gesture.Operation) || StructureOperations.Handles(gesture.Operation) || AssessmentOperations.Handles(gesture.Operation) || TagOperations.Handles(gesture.Operation)
            || CategoryStateOperations.Handles(gesture.Operation) || FilterOperations.Handles(gesture.Operation)
            || ColumnOperations.Handles(gesture.Operation))
        {
            return BodyGesture(editId, gesture);
        }

        lock (_gate)
        {
            (ModelChange.Setting? setting, string refusal) = HierarchicalRequirementGestures.Plan(_reading.Model, gesture);
            if (setting is null)
            {
                return refusal;
            }

            var knownIds = _reading.Model.Nodes.Where(node => node.Id == node.Number).Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            _writing++;
            _writesStarted++;
            var previous = _writes;
            _writes = WriteAsync(previous, editId, setting, knownIds);
            return "";
        }
    }

    /// <summary>
    /// Takes a change of the body, of an entry's text, of the hierarchy, of an assessment, or of a list the registration
    /// keeps, the tags, the categories, the states, the filters and the widths of the columns: refused at once when the page cannot be changed,
    /// the entry is gone or a text cannot be written; otherwise run through the history behind the writes before it,
    /// against the bytes the page was made from.
    /// </summary>
    private string BodyGesture(ShortGuid editId, HierarchySurfaceGesture gesture)
    {
        if (_execute is null) return "This document's text cannot be changed here.";
        if (TextOperations.Handles(gesture.Operation))
        {
            var attribute = TextOperations.AttributeOf(gesture.Operation);
            if (!gesture.Params.TryGetValue(attribute, out var value)) return $"'{gesture.Operation}' needs its {attribute}.";
            if (TextOperations.Refusal(gesture.Operation, value) is { Length: > 0 } refusal) return refusal;
        }

        if (AssessmentOperations.Refusal(gesture.Operation, gesture.Params) is { Length: > 0 } unwritable) return unwritable;
        if (TagOperations.Refusal(gesture.Operation, gesture.Params) is { Length: > 0 } unnamed) return unnamed;
        if (CategoryStateOperations.Refusal(gesture.Operation, gesture.Params) is { Length: > 0 } unclassified) return unclassified;
        if (FilterOperations.Refusal(gesture.Operation, gesture.Params) is { Length: > 0 } unstored) return unstored;
        if (ColumnOperations.Refusal(gesture.Operation, gesture.Params) is { Length: > 0 } unsized) return unsized;

        lock (_gate)
        {
            if (_reading.Model.ReadOnlyReason.Length > 0) return _reading.Model.ReadOnlyReason;
            if (_reading.Files.Body is not { } expected) return HierarchicalRequirementGestures.Drifted;
            var gone = gesture.Operation switch
            {
                _ when AssessmentOperations.TargetsAnAssessment(gesture.Operation) => _reading.Model.Notes.All(note => note.Id != gesture.TargetId),
                _ when TagOperations.TargetsATag(gesture.Operation) || CategoryStateOperations.TargetsAMember(gesture.Operation) => _reading.Model.OptionLists.SelectMany(list => list.Options).All(option => option.Id != gesture.TargetId),
                _ when FilterOperations.TargetsAFilter(gesture.Operation) => _reading.Model.Filters.All(filter => filter.Id != gesture.TargetId),
                _ when ColumnOperations.TargetsAColumn(gesture.Operation) => _reading.Model.Widths.All(column => column.Id != gesture.TargetId),
                _ when CategoryStateOperations.TargetsTheDocument(gesture.Operation) => false,
                StructureOperations.AddTopItem or FilterOperations.StoreFilter or ColumnOperations.AddColumnWidth => false,
                _ => _reading.Model.Nodes.All(node => node.Id != gesture.TargetId),
            };
            if (gone) return HierarchicalRequirementGestures.Drifted;

            var command = new HierarchicalRequirementEditCommand(_bodyPath, gesture.Operation, gesture.TargetId, gesture.Params, expected, _registrationPath);
            _writing++;
            _writesStarted++;
            var previous = _writes;
            _writes = ExecuteAsync(previous, editId, command, _execute);
            return "";
        }
    }

    /// <summary>
    /// Runs one edit through the history after the writes before it; then says what became of it and sends the model as
    /// the files now are. A refused edit is followed by the page read again: it was refused because the file is not what
    /// the page showed.
    /// </summary>
    private async Task ExecuteAsync(Task previous, ShortGuid editId, ICommand command, Func<ICommand, Task<CommandResult>> execute)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        HierarchySurfaceEditOutcome outcome;
        try
        {
            var result = await execute(command).ConfigureAwait(false);
            outcome = new HierarchySurfaceEditOutcome(editId, result.IsSuccess, result.IsSuccess ? "" : result.Error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.Warning(exception, "Could not change {Path}", _bodyPath);
            outcome = new HierarchySurfaceEditOutcome(editId, false, $"'{Path.GetFileName(_bodyPath)}' could not be changed: {exception.Message}");
        }

        Settle(outcome, outcome.Written ? HierarchySurfaceCauses.Edit : HierarchySurfaceCauses.Outside);
    }

    /// <summary>
    /// Writes one setting to the registration, creating it first when there is none, after the writes before it; then says
    /// what became of it and sends the model as the files now are.
    /// </summary>
    private async Task WriteAsync(Task previous, ShortGuid editId, ModelChange.Setting setting, IReadOnlySet<string> knownIds)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        HierarchySurfaceEditOutcome outcome;
        try
        {
            outcome = await WriteSettingAsync(editId, setting, knownIds).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not write {Path}", _registrationPath);
            outcome = new HierarchySurfaceEditOutcome(editId, false, $"'{Path.GetFileName(_registrationPath)}' could not be written: {exception.Message}");
        }

        Settle(outcome, HierarchySurfaceCauses.Edit);
    }

    /// <summary>A write of this session's has ended: says what became of it, then sends the model as the files now are, if they changed.</summary>
    private void Settle(HierarchySurfaceEditOutcome outcome, string cause)
    {
        HierarchySurfaceModel? model;
        lock (_gate)
        {
            _writing--;
            model = Take(Build(ReadFiles(), cause));
        }

        Changed?.Invoke(this, new HierarchySurfaceChangedEventArgs(outcome));
        if (model is not null)
        {
            Changed?.Invoke(this, new HierarchySurfaceChangedEventArgs(model));
        }
    }

    /// <summary>
    /// Takes <paramref name="reading"/> as the page's when its files differ from those the page was made from, and
    /// returns the model to send; null when there is nothing to send. Called with the lock held. A reading of a file that
    /// cannot be read keeps the last page's nodes, read-only with the reason, for as long as it cannot (FR-022).
    /// </summary>
    private HierarchySurfaceModel? Take(Reading? reading)
    {
        if (reading is null || reading.SameFiles(_reading))
        {
            return null;
        }

        if (reading.Unreadable && _reading.Model.Nodes.Count > 0)
        {
            var last = _reading.Model;
            reading = reading with { Model = last with { ReadOnlyReason = reading.Model.ReadOnlyReason, Cause = reading.Model.Cause, Findings = reading.Model.Findings } };
        }

        _reading = reading;
        return reading.Model;
    }

    /// <summary>
    /// A command of this module is about to write a file: its bytes are remembered, so a page that hears of the write from
    /// the watcher before the store says so takes it as the edit it is, not as a change from outside.
    /// </summary>
    private void OnWriting(string path, byte[] bytes)
    {
        var full = Path.GetFullPath(path);
        if (string.Equals(full, Path.GetFullPath(_bodyPath), StringComparison.Ordinal) ||
            string.Equals(full, Path.GetFullPath(_registrationPath), StringComparison.Ordinal))
        {
            lock (_gate)
            {
                _announced[full] = bytes;
            }
        }
    }

    /// <summary>Whether either file holds the bytes a command of this module said it writes there. Called with the lock held.</summary>
    private bool Announced(Files files) =>
        (files.Body is { } body && _announced.TryGetValue(Path.GetFullPath(_bodyPath), out var written) && body.AsSpan().SequenceEqual(written)) ||
        (files.Registration is { } registration && _announced.TryGetValue(Path.GetFullPath(_registrationPath), out var kept) && registration.AsSpan().SequenceEqual(kept));

    /// <summary>A command of this module wrote a file: a page of it reads the file again at once, as an edit rather than a change from outside.</summary>
    private void OnReloaded(string path)
    {
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_bodyPath), StringComparison.Ordinal) ||
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(_registrationPath), StringComparison.Ordinal))
        {
            // Not awaited: the store raises this while its command runs, and a page's read must not hold that.
            _ = ReadAgainAsync(HierarchySurfaceCauses.Edit);
        }
    }

    private async Task<HierarchySurfaceEditOutcome> WriteSettingAsync(ShortGuid editId, ModelChange.Setting setting, IReadOnlySet<string> knownIds)
    {
        var body = await File.ReadAllBytesAsync(_bodyPath).ConfigureAwait(false);
        var existing = File.Exists(_registrationPath) ? await File.ReadAllBytesAsync(_registrationPath).ConfigureAwait(false) : null;
        var created = RegistrationCreation.Create(
            HierarchicalRequirementDefinition.Binding, HierarchicalRequirementDefinition.Origin, Path.GetFileName(_registrationPath), Path.GetFileName(_bodyPath), body, existing);
        if (created.Bytes is null)
        {
            return new HierarchySurfaceEditOutcome(editId, false, created.Refusal ?? "");
        }

        var registration = OpenRegistration.Open(created.Bytes, HierarchicalRequirementDefinition.Binding);
        registration.KnownIds = knownIds;
        if (registration.Change(setting) is PlanResult.Refused refused)
        {
            return new HierarchySurfaceEditOutcome(editId, false, refused.Reason);
        }

        await _writer.SaveAsync(_registrationPath, registration.Bytes, CancellationToken.None).ConfigureAwait(false);
        return new HierarchySurfaceEditOutcome(editId, true);
    }

    /// <summary>Reads both files again when either changed outside the session.</summary>
    private Task OnExternalChangeAsync() => ReadAgainAsync(HierarchySurfaceCauses.Outside);

    /// <summary>Reads both files again, and sends the model when they changed, as following <paramref name="cause"/>.</summary>
    private async Task ReadAgainAsync(string cause)
    {
        await _rereads.WaitAsync().ConfigureAwait(false);
        try
        {
            int writesStarted;
            lock (_gate)
            {
                if (_writing > 0)
                {
                    // This session's own write is landing: the files are read when it has, once.
                    return;
                }

                writesStarted = _writesStarted;
            }

            // A file that cannot be read as a document is read again too: another program may be halfway through writing it.
            ((Files files, Reading? reading), int reads) = await Body.ReadAsync(_ => ReadAs(cause), read => read.Reading is { Unreadable: false }).ConfigureAwait(false);
            if (files.Body is not null && reads > 1)
            {
                // Worded as the other sessions word theirs, so one search finds every retried read.
                _logger.Information("Read {Path} on attempt {Attempt} of {Attempts}", _bodyPath, reads, WatchedFile.ReadAttempts);
            }

            HierarchySurfaceModel? model;
            lock (_gate)
            {
                if (_writing > 0 || writesStarted != _writesStarted)
                {
                    // A write started while this was reading; its own re-read, made after it, is the newer.
                    return;
                }

                if (reading is null)
                {
                    // Out of attempts: the last good reading stays.
                    _logger.Warning("The changed {Path} no longer opens after {Attempts} attempts: {Refusal}", _bodyPath, WatchedFile.ReadAttempts, files.Refusal);
                    return;
                }

                // The same bytes are the same page: a write of this session's own, or one that changed nothing.
                model = Take(reading);
            }

            if (model is not null)
            {
                Changed?.Invoke(this, new HierarchySurfaceChangedEventArgs(model));
            }
        }
        finally
        {
            _rereads.Release();
        }
    }

    /// <summary>Ends the session once the writes still waiting have been made: closing a page does not lose a fold.</summary>
    public async ValueTask DisposeAsync()
    {
        await Writes.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (_documents is not null)
        {
            _documents.Writing -= OnWriting;
            _documents.Reloaded -= OnReloaded;
        }

        Body.Dispose();
        Registration.Dispose();
        _rereads.Dispose();
    }

    /// <summary>
    /// Both files read, and the reading made of them as following <paramref name="cause"/>; a change from outside that
    /// holds the bytes a command of this module wrote is that command's edit.
    /// </summary>
    private (Files Files, Reading? Reading) ReadAs(string cause)
    {
        var files = ReadFiles();
        lock (_gate)
        {
            if (cause == HierarchySurfaceCauses.Outside && Announced(files)) cause = HierarchySurfaceCauses.Edit;
        }

        return (files, Build(files, cause));
    }

    /// <summary>The bytes of the body and of the registration, when there is one; the reason when the body cannot be read.</summary>
    private Files ReadFiles()
    {
        try
        {
            var body = SharedDocumentReader.ReadAllBytes(_bodyPath);
            var registration = File.Exists(_registrationPath) ? SharedDocumentReader.ReadAllBytes(_registrationPath) : null;
            return new Files(body, registration is not null && IsThisDesigners(registration) ? registration : null, "");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new Files(null, null, exception.Message);
        }
    }

    /// <summary>Whether a registration's first line names this designer type: one beside the body that names another is not read.</summary>
    private static bool IsThisDesigners(byte[] registration)
    {
        var text = Encoding.UTF8.GetString(registration);
        var end = text.IndexOfAny(['\r', '\n']);
        return (end < 0 ? text : text[..end]).TrimStart('\uFEFF').Trim() == HierarchicalRequirementDefinition.Origin;
    }

    private Reading? Build(Files files, string cause)
    {
        if (files.Body is null)
        {
            return null;
        }

        var name = Path.GetFileName(_bodyPath);
        var read = HierarchicalRequirementBody.Read(files.Body, name, files.Registration, Path.GetFileName(_registrationPath));
        var title = HierarchicalRequirementSurfaceMapper.Title(Encoding.UTF8.GetString(files.Body), name);
        var model = HierarchicalRequirementSurfaceMapper.Map(read.Document, title, cause, block => read.Registration?.BlockEntries(block)?.Select(entry => entry.Key).ToList());
        return new Reading(files, model, read.Body.Model.Unreadable);
    }

    private Reading Unreadable(string refusal) => new(
        new Files(null, null, refusal),
        new HierarchySurfaceModel(
            Path.GetFileNameWithoutExtension(_bodyPath),
            $"'{Path.GetFileName(_bodyPath)}' cannot be read: {refusal}",
            HierarchySurfaceCauses.Open,
            [],
            [],
            [],
            [],
            [],
            []),
        true);

    /// <summary>The bytes of the two files as they were read; the body is null when it could not be.</summary>
    private sealed record Files(byte[]? Body, byte[]? Registration, string Refusal);

    /// <summary>One reading of the two files and the model made of it, and whether the body could not be read as a requirement document.</summary>
    private sealed record Reading(Files Files, HierarchySurfaceModel Model, bool Unreadable)
    {
        public bool SameFiles(Reading other) =>
            Same(Files.Body, other.Files.Body) && Same(Files.Registration, other.Files.Registration);

        private static bool Same(byte[]? left, byte[]? right) =>
            left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
    }
}
