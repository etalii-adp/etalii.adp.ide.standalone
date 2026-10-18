using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;

/// <summary>
/// The Azure DevOps pipeline's parity transcript: what today's hand-written code answers for every document
/// of the corpus, frozen as <c>Parity/azure-pipeline.transcript.json</c> before any of it is derived from a
/// bundled DISL definition or read through an FBL binding (azure-devops-pipeline-disl-fbl Requirement 1). Every
/// later step must reproduce it byte for byte, or change it only as that specification names.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b> is <see cref="PipelineCorpus"/>'s. A document is opened in a copy of the folder it was
/// taken from, so the templates it names are followed as they are from that folder; a document whose bytes and
/// folder equal one recorded earlier is written as <c>sameAs</c> that one.
/// </para>
/// <para>
/// <b>Per document</b>: the context menu and the property rows of every element the mapper draws with every
/// stage and job opened, of the empty id and of an unknown id; the findings; the payload of every element so
/// drawn; and a scripted edit sequence that runs every command once, with each step's answer and the hunks it
/// made, and for a step that landed on the history its undo and its redo and theirs.
/// </para>
/// <para>
/// <b>Nothing is masked.</b> A document is read from a temporary folder, but no answer names that folder: a
/// template is shown by its path relative to the workspace. A step's ids are the parser's, which are paths and
/// never minted.
/// </para>
/// </remarks>
internal static class PipelineTranscript
{
    /// <summary>The checked-in file's name, in this project's <c>Parity</c> folder.</summary>
    private const string FileName = "azure-pipeline.transcript.json";

    private const string Unknown = "parity:unknown";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(AzurePipelineReflection.Descriptor);

    /// <summary>The checked-in transcript.</summary>
    public static string CheckedInPath => Path.Combine(PipelineCorpus.ModuleFolder, "backend", PipelineCorpus.TestProject, "Parity", FileName);

    /// <summary>The whole transcript, as the bytes the checked-in file must hold.</summary>
    public static async Task<byte[]> GenerateAsync(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await using (var json = new Utf8JsonWriter(stream, TranscriptText.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("module", Diagram.Pipeline.Origin.Key);
            json.WriteString("generator", $"src/diagrams/azure-devops-pipeline/backend/{PipelineCorpus.TestProject}/Parity/PipelineTranscript.cs");
            json.WriteString("regenerate", "ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)");

            // The toolbox is static: it does not read the document, so it is written once.
            json.WriteStartArray("toolbox");
            foreach (var item in PipelineDefinition.Toolbox)
            {
                json.WriteStringValue($"{item.Id} | {item.Label} | {item.Icon} | drops {item.DropActionId} | {item.Description}");
            }

            json.WriteEndArray();

            // What a new pipeline is created with (finding B17): the binding's template must be these bytes.
            await using (var services = Services())
            {
                var factory = services.GetServices<IDiagramDocumentFactory>().Single(candidate => candidate.Origin == Diagram.Pipeline.Origin);
                json.WriteString("emptyDocument", factory.CreateEmptyDocument("parity"));
            }

            json.WriteStartArray("documents");
            var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var document in PipelineCorpus.Documents())
            {
                await WriteDocumentAsync(json, document, recorded, cancellationToken);
            }

            json.WriteEndArray();
            json.WriteEndObject();
            await json.FlushAsync(cancellationToken);
        }

        stream.Write("\r\n"u8);
        return stream.ToArray();
    }

    /// <summary>The module's services, with the one core service its document factory needs nothing beyond.</summary>
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHistoryStackStore>(new HistoryStackStore(new PipelineTestDispatcher(new PipelineDocumentStore())));
        services.AddAzurePipeline();
        return services.BuildServiceProvider();
    }

    private static async Task WriteDocumentAsync(Utf8JsonWriter json, PipelineCorpus.Document document, Dictionary<string, string> recorded, CancellationToken cancellationToken)
    {
        var source = Path.Combine(PipelineCorpus.SourceFolder, document.Relative);
        var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
        var sha = TranscriptText.Sha256(bytes);

        json.WriteStartObject();
        json.WriteString("document", document.Relative);
        json.WriteString("sha256", sha);

        // A copy is the same reading only where the templates around it are the same too.
        var key = $"{sha} {await PipelineCorpus.FolderShaAsync(document, cancellationToken)} {document.InFolder}";
        if (recorded.TryGetValue(key, out var first))
        {
            json.WriteString("sameAs", first);
            json.WriteEndObject();
            return;
        }

        recorded[key] = document.Relative;

        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.PipelineTranscript", Guid.NewGuid().ToString("N"));
        try
        {
            PipelineCorpus.CopyFolder(Path.Combine(PipelineCorpus.SourceFolder, document.Root), folder);
            var body = Path.Combine(folder, document.InFolder);
            using var session = new Session(folder, body);
            await WriteReadingsAsync(json, session, bytes, cancellationToken);
            await WriteEditsAsync(json, session, cancellationToken);
        }
        finally
        {
            TestFolder.TryDelete(folder);
        }

        json.WriteEndObject();
    }

    private static async Task WriteReadingsAsync(Utf8JsonWriter json, Session session, byte[] bytes, CancellationToken cancellationToken)
    {
        // What the mapper draws, or what it throws; the parser's own ids are judged either way.
        IReadOnlyList<DiagramElement> drawn = [];
        var drawing = "";
        try
        {
            drawn = session.Drawn();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            drawing = Thrown(exception);
        }

        List<string> targets =
        [
            .. drawn.Select(element => element.Id).Concat(session.Model.Stages.SelectMany(Elements)).Distinct(StringComparer.Ordinal),
            "",
            Unknown,
        ];

        var menus = new List<IReadOnlyList<string>>();
        var choices = new List<IReadOnlyList<string>>();
        var lines = new List<(string Target, int Menu, IReadOnlyList<string> Rows)>();
        foreach (var target in targets)
        {
            var menu = menus.IndexOfOrAdd(await LinesAsync(async () => (await session.Actions.DiscoverAsync(session.Target(target), cancellationToken)).MenuLines()));
            var rows = await LinesAsync(async () => [.. (await session.Properties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => row.RowLine(choices))]);
            lines.Add((target, menu, rows));
        }

        json.WriteStartArray("menus");
        foreach (var menu in menus)
        {
            json.WriteLines(menu);
        }

        json.WriteEndArray();

        json.WriteStartArray("choices");
        foreach (var list in choices)
        {
            json.WriteLines(list);
        }

        json.WriteEndArray();

        json.WriteStartArray("targets");
        foreach ((string target, int menu, _) in lines)
        {
            json.WriteStringValue($"{TranscriptText.Quoted(target)} => menu #{menu}");
        }

        json.WriteEndArray();

        json.WriteStartArray("rows");
        foreach ((string target, _, IReadOnlyList<string> rows) in lines)
        {
            if (rows.Count == 0)
            {
                continue;
            }

            json.WriteStartObject();
            json.WriteString("target", target);
            json.WritePropertyName("rows");
            json.WriteLines(rows);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WritePropertyName("findings");
        json.WriteLines(await LinesAsync(async () =>
        {
            var problems = await new PipelineValidator().ValidateAsync(
                new DiagramValidationRequest(TranscriptText.Decode(bytes), Path.GetFileNameWithoutExtension(session.Body), session.Folder, session.Body, null),
                cancellationToken);
            return [.. problems.Select(problem => $"{Location(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}")];
        }));

        json.WritePropertyName("payloads");
        json.WriteLines(drawing.Length > 0
            ? [drawing]
            : [.. drawn.Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]);
    }

    /// <summary>The lines a reading gives, or the one line saying what it threw.</summary>
    private static async Task<IReadOnlyList<string>> LinesAsync(Func<Task<IReadOnlyList<string>>> reading)
    {
        try
        {
            return await reading();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [Thrown(exception)];
        }
    }

    /// <summary>
    /// What today's code throws, recorded rather than ending the transcript: it is the answer a user would meet,
    /// and what follows it still runs.
    /// </summary>
    private static string Thrown(Exception exception) => $"throws {exception.GetType().Name}: {exception.Message}";

    private static string Location(DiagramProblemLocation? location) => location switch
    {
        DiagramProblemLineLocation line => $"line {line.Number}",
        DiagramProblemElementLocation element => $"element {TranscriptText.Quoted(element.Id)}",
        null => "nowhere",
        _ => location.ToString(),
    };

    /// <summary>
    /// The scripted edit sequence: every command once, through the menus, the dialogs and the property rows,
    /// run through the real history on a copy of the document, in an order that reaches refusals too.
    /// </summary>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var script = new Script(json, session, cancellationToken);
        var original = await script.StartAsync();

        var model = session.Model;
        var stages = model.Stages.Where(stage => PipelineEditTarget.For(stage).IsEditable).ToList();
        var stage = stages.FirstOrDefault();
        var later = stages.Skip(1).FirstOrDefault();
        var job = stages.SelectMany(candidate => candidate.Jobs).FirstOrDefault(candidate => PipelineEditTarget.For(candidate).IsEditable);
        var steps = model.Stages.SelectMany(candidate => candidate.Jobs).Where(candidate => PipelineEditTarget.For(candidate).IsEditable && candidate.Steps.Count > 1).Select(candidate => candidate.Steps).FirstOrDefault()
            ?? model.Stages.SelectMany(candidate => candidate.Jobs).Where(candidate => PipelineEditTarget.For(candidate).IsEditable).Select(candidate => candidate.Steps).FirstOrDefault()
            ?? [];
        var step = steps.FirstOrDefault(candidate => !candidate.IsFromTemplate)?.Id;
        var second = steps.Skip(1).FirstOrDefault(candidate => !candidate.IsFromTemplate)?.Id;
        var foreign = model.Stages.SelectMany(Elements).FirstOrDefault(id => PipelineEdits.Locate(model, id) is { Target.IsEditable: false });

        // Opening and closing, which is view state and writes nothing.
        if (stage is not null && stage.Jobs.Count > 0)
        {
            await script.ExecuteAsync(stage.Id, "azure-pipeline.toggle-stage");
            await script.ExecuteAsync(stage.Id, "azure-pipeline.toggle-stage");
        }

        if (job is not null && job.Steps.Count > 0)
        {
            await script.ExecuteAsync(job.Id, PipelineContextActionProvider.ToggleJobActionId);
        }

        // A rename of every stage the file states, so each style of value a document holds is rewritten once (finding B7).
        foreach (var each in stages)
        {
            await script.ExecuteAsync(each.Id, PipelineContextActionProvider.RenameActionId);
            await script.CommitAsync(each.Id, PipelineContextActionProvider.RenameActionId, "Parity stage");
        }

        if (stage is not null)
        {
            await script.SetAsync(stage.Id, PipelineContextPropertyProvider.DisplayNamePropertyId, "  Row name  ");
            await script.SetAsync(stage.Id, PipelineContextPropertyProvider.DisplayNamePropertyId, "");
            await script.ExecuteAsync(stage.Id, PipelineContextActionProvider.ToggleEnabledActionId);
            await script.SetAsync(stage.Id, PipelineContextPropertyProvider.EnabledPropertyId, "true");
            await script.SetAsync(stage.Id, "azure-pipeline.name", "Renamed");
        }

        if (job is not null)
        {
            await script.CommitAsync(job.Id, PipelineContextActionProvider.RenameActionId, "Parity job");
            await script.ExecuteAsync(job.Id, PipelineContextActionProvider.ToggleEnabledActionId);
            await script.SetAsync(job.Id, PipelineContextPropertyProvider.DependsOnPropertyId, PipelineContextPropertyProvider.NothingCandidate);
            await script.SetAsync(job.Id, PipelineContextPropertyProvider.DependsOnPropertyId, "nobody");
            await script.SetAsync(job.Id, PipelineContextPropertyProvider.DependsOnPropertyId, PipelineContextPropertyProvider.DefaultCandidate);
        }

        // A key added to a step, which must land after the whole of a script written as a block (finding B8).
        if (step is not null)
        {
            await script.CommitAsync(step, PipelineContextActionProvider.RenameActionId, "Parity step");
            await script.ExecuteAsync(step, PipelineContextActionProvider.ToggleEnabledActionId);
            await script.SetAsync(step, PipelineContextPropertyProvider.DependsOnPropertyId, "nobody");
        }

        if (later is not null && stage is not null)
        {
            await script.SetAsync(later.Id, PipelineContextPropertyProvider.DependsOnPropertyId, stage.Name);
            await script.SetAsync(later.Id, PipelineContextPropertyProvider.DependsOnPropertyId, PipelineContextPropertyProvider.NothingCandidate);
            await script.SetAsync(later.Id, PipelineContextPropertyProvider.DependsOnPropertyId, "nobody");
            await script.SetAsync(stage.Id, PipelineContextPropertyProvider.DependsOnPropertyId, later.Name);
            await script.ExecuteAsync(later.Id, PipelineContextActionProvider.ClearDependenciesActionId);
        }

        // Moving a step, the one thing whose order is its meaning (finding B12).
        if (second is not null)
        {
            await script.ExecuteAsync(second, PipelineContextActionProvider.MoveStepUpActionId);
            await script.ExecuteAsync(second, PipelineContextActionProvider.MoveStepDownActionId);
        }

        // The adds, each where it belongs and where it does not (findings B10 and B11).
        if (stage is not null)
        {
            await script.ExecuteAsync(stage.Id, PipelineContextActionProvider.AddStageActionId);
            await script.ExecuteAsync(stage.Id, PipelineContextActionProvider.AddJobActionId);
            await script.ExecuteAsync(stage.Id, PipelineContextActionProvider.AddDeploymentJobActionId);
            await script.ExecuteAsync(stage.Id, PipelineContextActionProvider.AddStepActionId);
        }

        if (job is not null)
        {
            await script.ExecuteAsync(job.Id, PipelineContextActionProvider.AddStepActionId);
            await script.ExecuteAsync(job.Id, PipelineContextActionProvider.AddJobActionId);
        }

        // An element whose text is in another file, or that nobody wrote.
        if (foreign is not null)
        {
            await script.ExecuteAsync(foreign, PipelineContextActionProvider.RenameActionId);
            await script.CommitAsync(foreign, PipelineContextActionProvider.RenameActionId, "Not here");
            await script.SetAsync(foreign, PipelineContextPropertyProvider.EnabledPropertyId, "false");
        }

        await script.ExecuteAsync(Unknown, PipelineContextActionProvider.RenameActionId);
        await script.CommitAsync(Unknown, PipelineContextActionProvider.RemoveActionId, "");
        await script.SetAsync(Unknown, PipelineContextPropertyProvider.DisplayNamePropertyId, "Nobody");
        await script.ExecuteAsync("", PipelineContextActionProvider.AddStageActionId);

        // The removals, last, of a step, a job and a stage as the document now holds them (finding B9, struck).
        var now = session.Model;
        var removed = new[]
        {
            now.Stages.SelectMany(candidate => candidate.Jobs).Where(candidate => PipelineEditTarget.For(candidate).IsEditable).SelectMany(candidate => candidate.Steps).FirstOrDefault(candidate => !candidate.IsFromTemplate)?.Id,
            now.Stages.Where(candidate => PipelineEditTarget.For(candidate).IsEditable).SelectMany(candidate => candidate.Jobs).FirstOrDefault(candidate => PipelineEditTarget.For(candidate).IsEditable)?.Id,
            now.Stages.FirstOrDefault(candidate => PipelineEditTarget.For(candidate).IsEditable)?.Id,
        };
        foreach (var id in removed.OfType<string>())
        {
            await script.ExecuteAsync(id, PipelineContextActionProvider.RemoveActionId);
        }

        await script.EndAsync(original);
    }

    /// <summary>Every id a stage holds: the stage, its jobs and their steps.</summary>
    private static IEnumerable<string> Elements(PipelineStage stage) =>
        [stage.Id, .. stage.Jobs.SelectMany(job => job.Steps.Select(step => step.Id).Prepend(job.Id))];

    /// <summary>One document's store, history, view state and providers.</summary>
    private sealed class Session : IDisposable
    {
        private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

        public Session(string folder, string body)
        {
            Folder = folder;
            Body = body;
            History = new HistoryStackStore(new PipelineTestDispatcher(Store));
            Actions = new PipelineContextActionProvider(History, Store, Views);
            Properties = new PipelineContextPropertyProvider(History, Store);
        }

        public string Folder { get; }

        public string Body { get; }

        private PipelineDocumentStore Store { get; } = new();

        private PipelineViewState Views { get; } = new();

        public HistoryStackStore History { get; }

        public PipelineContextActionProvider Actions { get; }

        public PipelineContextPropertyProvider Properties { get; }

        public PipelineModel Model => Store.GetOrLoad(Folder, Body).Model;

        public ContextTarget Target(string elementId) =>
            ContextTarget.ForElement(Body, elementId, Folder, _watchId, Diagram.Pipeline.Origin);

        /// <summary>What the mapper draws with every stage and job opened.</summary>
        public IReadOnlyList<DiagramElement> Drawn()
        {
            var model = Model;
            var opened = model.Stages.SelectMany(stage => stage.Jobs.Select(job => job.Id).Prepend(stage.Id)).ToHashSet(StringComparer.Ordinal);
            return new PipelineElementMapper(PipelineMetrics.Default).Visible(model, DiagramViewport.Unbounded, opened);
        }

        public void Dispose() => History.Dispose();
    }

    /// <summary>Runs the steps of one document's edit sequence and writes each as it goes.</summary>
    private sealed class Script(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        private string _text = "";

        private IHistoryStack Stack => session.History.Get(session.Folder);

        public async Task<string> StartAsync()
        {
            _text = await ReadAsync();
            json.WriteStartArray("edits");
            return _text;
        }

        public async Task EndAsync(string original)
        {
            json.WriteEndArray();

            var undone = 0;
            var thrown = "";
            try
            {
                while (Stack.CanUndo && (await Stack.UndoAsync(cancellationToken)).IsSuccess)
                {
                    undone++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                thrown = $"; the next {Thrown(exception)}";
            }

            var restored = await ReadAsync();
            json.WriteString("undoAll", $"{undone} undone{thrown}; {(string.Equals(restored, original, StringComparison.Ordinal) ? "the original text is back" : "the original text is NOT back")}");
            if (!string.Equals(restored, original, StringComparison.Ordinal))
            {
                // What undoing everything left different, so a change in it is visible rather than
                // hidden behind the same sentence.
                json.WriteStartObject("undoAllLeaves");
                json.WriteChange(original, restored);
                json.WriteEndObject();
            }
        }

        public Task SetAsync(string target, string propertyId, string value) =>
            StepAsync($"set {target} {propertyId} = {TranscriptText.Quoted(value)}", async () =>
            {
                var result = await session.Properties.SetAsync(session.Target(target), propertyId, value, cancellationToken);
                return result.IsSuccess ? "ok" : $"refused: {result.Error}";
            });

        public Task ExecuteAsync(string target, string actionId) =>
            StepAsync($"execute {actionId} on {target}", async () => Describe(await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken)));

        public Task CommitAsync(string target, string actionId, string value) =>
            StepAsync($"commit {actionId} on {target} = {TranscriptText.Quoted(value)}", async () =>
            {
                var result = await session.Actions.CommitAsync(session.Target(target), actionId, value, "", cancellationToken);
                return result.Completed ? "ok" : $"refused: {result.Error}";
            });

        /// <summary>
        /// A step, and where it landed on the history its undo and its redo: each recorded with its answer and
        /// the hunks it made, so that what an undo gives back is as visible as what the edit took.
        /// </summary>
        private async Task StepAsync(string step, Func<Task<string>> run)
        {
            var before = Stack.Availability.UndoCount;
            await RecordAsync(step, await AnswerAsync(run));
            if (Stack.Availability.UndoCount <= before)
            {
                return;
            }

            await RecordAsync($"undo {step}", await AnswerAsync(async () => Result(await Stack.UndoAsync(cancellationToken))));
            await RecordAsync($"redo {step}", await AnswerAsync(async () => Result(await Stack.RedoAsync(cancellationToken))));
        }

        private static string Result(CommandResult result) => result.IsSuccess ? "ok" : $"refused: {result.Error}";

        /// <summary>A step's answer, or what it threw (<see cref="Thrown"/>).</summary>
        private static async Task<string> AnswerAsync(Func<Task<string>> run)
        {
            try
            {
                return await run();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Thrown(exception);
            }
        }

        private async Task RecordAsync(string step, string answer)
        {
            var after = await ReadAsync();
            json.WriteStartObject();
            json.WriteString("do", step);
            json.WriteString("answer", answer);
            json.WriteChange(_text, after);
            json.WriteEndObject();
            _text = after;
        }

        private async Task<string> ReadAsync() => TranscriptText.Decode(await File.ReadAllBytesAsync(session.Body, cancellationToken));

        private static string Describe(ContextExecutionResult result) => result switch
        {
            ContextExecutionCompleted => "ok",
            ContextExecutionFailed failed => $"refused: {failed.Message}",
            ContextExecutionRequiresInput input => $"asks: {input.Request}",
            ContextExecutionRequiresConfirmation confirmation => $"confirms: {confirmation.Request}",
            _ => result.ToString(),
        };
    }
}
