using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
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
    private readonly Lock _gate = new();

    /// <summary>One re-read at a time across both files, so an older reading never lands after a newer one.</summary>
    private readonly SemaphoreSlim _rereads = new(1, 1);

    private Reading _reading;
    private Task _writes = Task.CompletedTask;
    private int _writing;
    private int _writesStarted;

    /// <param name="bodyPath">The Markdown file.</param>
    /// <param name="registrationPath">Its registration, or null when it has none yet: one is then created beside it, named after it, with the first setting.</param>
    /// <param name="writer">How a file is published.</param>
    /// <param name="time">The clock a refused read waits on before it is tried again; the system's when null.</param>
    /// <param name="events">The project roots' event sources, through which a file inside a watched root is heard.</param>
    public HierarchicalRequirementSession(string bodyPath, string? registrationPath, IFileWriter writer, TimeProvider? time = null, ProjectFileEvents? events = null)
    {
        ArgumentNullException.ThrowIfNull(bodyPath);
        ArgumentNullException.ThrowIfNull(writer);
        _bodyPath = bodyPath;
        _registrationPath = registrationPath ?? Path.ChangeExtension(bodyPath, ".adp");
        _writer = writer;
        var files = ReadFiles();
        _reading = Build(files, HierarchySurfaceCauses.Open) ?? Unreadable(files.Refusal);

        Body = new WatchedFile(_bodyPath, OnExternalChangeAsync, time ?? TimeProvider.System, events);
        Registration = new WatchedFile(_registrationPath, OnExternalChangeAsync, time ?? TimeProvider.System, events);
    }

    /// <summary>The watched Markdown file; for the guards, which reach its watcher.</summary>
    internal WatchedFile Body { get; }

    /// <summary>The watched registration, which may not exist yet.</summary>
    internal WatchedFile Registration { get; }

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

        HierarchySurfaceModel? model;
        lock (_gate)
        {
            _writing--;
            model = Build(ReadFiles(), HierarchySurfaceCauses.Edit) is { } reading && !reading.SameFiles(_reading) ? (_reading = reading).Model : null;
        }

        Changed?.Invoke(this, new HierarchySurfaceChangedEventArgs(outcome));
        if (model is not null)
        {
            Changed?.Invoke(this, new HierarchySurfaceChangedEventArgs(model));
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

    /// <summary>Internal for the guards, which call it as a watcher would.</summary>
    internal async Task OnExternalChangeAsync()
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

            (Files files, int reads) = await Body.ReadAsync(_ => ReadFiles(), read => read.Body is not null).ConfigureAwait(false);
            if (files.Body is not null && reads > 1)
            {
                // Worded as the other sessions word theirs, so one search finds every retried read.
                _logger.Information("Read {Path} on attempt {Attempt} of {Attempts}", _bodyPath, reads, WatchedFile.ReadAttempts);
            }

            HierarchySurfaceModel model;
            lock (_gate)
            {
                if (_writing > 0 || writesStarted != _writesStarted)
                {
                    // A write started while this was reading; its own re-read, made after it, is the newer.
                    return;
                }

                if (Build(files, HierarchySurfaceCauses.Outside) is not { } reading)
                {
                    // Out of attempts: the last good reading stays.
                    _logger.Warning("The externally changed {Path} no longer opens after {Attempts} attempts: {Refusal}", _bodyPath, WatchedFile.ReadAttempts, files.Refusal);
                    return;
                }

                if (reading.SameFiles(_reading))
                {
                    // The same bytes are the same page: a write of this session's own, or one that changed nothing.
                    return;
                }

                _reading = reading;
                model = reading.Model;
            }

            Changed?.Invoke(this, new HierarchySurfaceChangedEventArgs(model));
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
        Body.Dispose();
        Registration.Dispose();
        _rereads.Dispose();
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
        return new Reading(files, model);
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
            []));

    /// <summary>The bytes of the two files as they were read; the body is null when it could not be.</summary>
    private sealed record Files(byte[]? Body, byte[]? Registration, string Refusal);

    /// <summary>One reading of the two files and the model made of it.</summary>
    private sealed record Reading(Files Files, HierarchySurfaceModel Model)
    {
        public bool SameFiles(Reading other) =>
            Same(Files.Body, other.Files.Body) && Same(Files.Registration, other.Files.Registration);

        private static bool Same(byte[]? left, byte[]? right) =>
            left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
    }
}
