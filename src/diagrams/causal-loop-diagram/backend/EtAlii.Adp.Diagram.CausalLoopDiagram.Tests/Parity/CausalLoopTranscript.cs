using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// The causal loop diagram's parity transcript: what today's hand-written code answers for every
/// document of the corpus, frozen as <c>Parity/causal-loop.transcript.json</c> before any of it is
/// derived from a bundled DISL definition or read through an FBL binding (causal-loop-disl-fbl
/// Requirement 1). Every later step must reproduce it byte for byte, except where the requirements
/// name a change.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>: the test project's <c>Fixtures/*.cld</c> (one per finding of the requirements'
/// tables A and B that was only read, each saying in its own comment which), the module's
/// <c>examples/*/*.cld</c> and the shipped copies under <c>src/examples/diagrams/causal-loop-diagram/</c>.
/// A document whose bytes equal one recorded earlier is written as <c>sameAs</c> that one. A body
/// with a registration beside it is opened with that registration, as the host opens it; a fixture
/// has none, so it also pins the refusals a document opened without one gives.
/// </para>
/// <para>
/// <b>Once</b>: the toolbox, and the empty document a new <c>.cld</c> is created with (finding B9).
/// </para>
/// <para>
/// <b>Per document</b>: the context menu of every variable, link and loop id, the empty canvas, a
/// placement, a connect gesture and an unknown id, each for a readable and for a read-only
/// document; the property rows of the same targets, readable and read-only; the findings; the
/// payload of every element the open session sends; and a scripted edit sequence running every
/// command at least once, each step's answer and the hunks it made to the body and, where there is
/// one, to the registration.
/// </para>
/// <para>
/// <b>Read-only is the store's unreadable entry</b>, the one read-only case the backend has: the
/// same model, carrying a reason it could not be read, through every provider unchanged.
/// </para>
/// <para>
/// <b>Nothing is masked.</b> A new variable's name and a new loop's identifier are the module's next
/// free ones, and the layouts are deterministic, so no answer depends on the clock or on chance.
/// </para>
/// </remarks>
internal static class CausalLoopTranscript
{
    /// <summary>The checked-in file's name, in this project's <c>Parity</c> folder.</summary>
    private const string FileName = "causal-loop.transcript.json";

    private const string TestProject = "EtAlii.Adp.Diagram.CausalLoopDiagram.Tests";
    internal const string Unknown = "parity:unknown";
    private const string Nobody = "nobody";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(Wire.CausalLoopReflection.Descriptor);

    /// <summary>The module's folder, <c>src/diagrams/causal-loop-diagram</c>.</summary>
    private static string ModuleFolder { get; } = RepositoryPaths.Under("src", "diagrams", "causal-loop-diagram");

    /// <summary>The checked-in transcript.</summary>
    public static string CheckedInPath => Path.Combine(ModuleFolder, "backend", TestProject, "Parity", FileName);

    internal static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    private static string Placement => GestureIds.Placement(240, 160);

    /// <summary>The corpus, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    internal static IReadOnlyList<string> Corpus()
    {
        var fixtures = Directory.GetFiles(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures"), "*.cld");
        var examples = Directory.GetDirectories(Path.Combine(ModuleFolder, "examples")).SelectMany(folder => Directory.GetFiles(folder, "*.cld"));
        var shipped = Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "causal-loop-diagram")).SelectMany(folder => Directory.GetFiles(folder, "*.cld"));
        return [.. Sorted(fixtures), .. Sorted(examples), .. Sorted(shipped)];

        static IEnumerable<string> Sorted(IEnumerable<string> paths) =>
            paths.Select(path => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/')).Order(StringComparer.Ordinal);
    }

    /// <summary>The whole transcript, as the bytes the checked-in file must hold.</summary>
    public static async Task<byte[]> GenerateAsync(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await using (var json = new Utf8JsonWriter(stream, TranscriptText.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("module", Diagram.CausalLoop.Origin.Key);
            json.WriteString("generator", $"src/diagrams/causal-loop-diagram/backend/{TestProject}/Parity/CausalLoopTranscript.cs");
            json.WriteString("regenerate", "ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)");

            // The toolbox is static: it does not read the document, so it is written once.
            json.WriteStartArray("toolbox");
            foreach (var item in CausalLoopDefinition.Definition.Toolbox)
            {
                json.WriteStringValue($"{item.Id} | {item.Label} | {item.Icon} | drops {item.DropActionId} | {item.Description}");
            }

            json.WriteEndArray();

            // What a new .cld is created with: one of the two sources finding B9 says disagree.
            await using (var services = Services(new DiagramViewportRegistry()))
            {
                var factory = services.GetServices<IDiagramDocumentFactory>().Single(candidate => candidate.Origin == Diagram.CausalLoop.Origin);
                json.WriteString("emptyDocument", factory.CreateEmptyDocument("parity"));
            }

            json.WriteStartArray("documents");
            var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var relative in Corpus())
            {
                await WriteDocumentAsync(json, relative, recorded, cancellationToken);
            }

            json.WriteEndArray();
            json.WriteEndObject();
            await json.FlushAsync(cancellationToken);
        }

        stream.Write("\r\n"u8);
        return stream.ToArray();
    }

    /// <summary>The module's services as the existing tests build them, with the host's viewport registry supplied.</summary>
    private static ServiceProvider Services(IDiagramViewportRegistry registry) =>
        new ServiceCollection()
            .AddSingleton(registry)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddCausalLoop()
            .BuildServiceProvider();

    private static async Task WriteDocumentAsync(Utf8JsonWriter json, string relative, Dictionary<string, string> recorded, CancellationToken cancellationToken)
    {
        var source = Path.Combine(SourceFolder, relative);
        var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
        var sha = TranscriptText.Sha256(bytes);

        json.WriteStartObject();
        json.WriteString("document", relative);
        json.WriteString("sha256", sha);
        if (recorded.TryGetValue(sha, out var first))
        {
            json.WriteString("sameAs", first);
            json.WriteEndObject();
            return;
        }

        recorded[sha] = relative;

        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.CausalLoopTranscript", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var body = Path.Combine(folder, Path.GetFileName(source));
            await File.WriteAllBytesAsync(body, bytes, cancellationToken);

            string? registration = null;
            var besideIt = Path.ChangeExtension(source, ".adp");
            if (File.Exists(besideIt))
            {
                registration = Path.ChangeExtension(body, ".adp");
                await File.WriteAllBytesAsync(registration, await File.ReadAllBytesAsync(besideIt, cancellationToken), cancellationToken);
            }

            json.WriteString("registration", registration is null ? "none" : $"{Path.GetFileName(registration)}, sha256 {TranscriptText.Sha256(await File.ReadAllBytesAsync(registration, cancellationToken))}");

            await using var session = new Session(folder, body, registration);

            await session.ReadAsync(cancellationToken);
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
        var model = session.Model;
        List<string> elements =
        [
            .. model.Variables.Select(variable => $"variable:{variable.Id}")
                .Concat(model.Links.Select(link => link.Id))
                .Concat(model.Loops.Select(loop => loop.Id))
                .Distinct(StringComparer.Ordinal),
        ];
        var variableIds = model.Variables.Select(variable => variable.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var relation = variableIds.Count >= 2 ? GestureIds.Relation(variableIds[1], variableIds[0]) : GestureIds.Relation("a", "b");
        List<string> targets = [.. elements, "", Placement, relation, Unknown];

        var menus = new List<IReadOnlyList<string>>();
        var choices = new List<IReadOnlyList<string>>();
        var lines = new List<(string Target, int Menu, int MenuReadOnly, IReadOnlyList<string> Rows, IReadOnlyList<string> RowsReadOnly)>();
        foreach (var target in targets)
        {
            var menu = menus.IndexOfOrAdd(await LinesAsync(async () => (await session.Actions.DiscoverAsync(session.Target(target), cancellationToken)).MenuLines()));
            var menuReadOnly = menus.IndexOfOrAdd(await LinesAsync(async () => (await session.ReadOnlyActions.DiscoverAsync(session.Target(target), cancellationToken)).MenuLines()));
            var rows = await LinesAsync(async () => [.. (await session.Properties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => row.RowLine(choices))]);
            var rowsReadOnly = await LinesAsync(async () => [.. (await session.ReadOnlyProperties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => row.RowLine(choices))]);
            lines.Add((target, menu, menuReadOnly, rows, rowsReadOnly));
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
        foreach ((string target, int menu, int menuReadOnly, _, _) in lines)
        {
            json.WriteStringValue($"{TranscriptText.Quoted(target)} => menu #{menu}, read-only #{menuReadOnly}");
        }

        json.WriteEndArray();

        json.WriteStartArray("rows");
        foreach ((string target, _, _, IReadOnlyList<string> rows, IReadOnlyList<string> rowsReadOnly) in lines)
        {
            if (rows.Count == 0 && rowsReadOnly.Count == 0)
            {
                continue;
            }

            json.WriteStartObject();
            json.WriteString("target", target);
            json.WritePropertyName("rows");
            json.WriteLines(rows);
            if (TranscriptText.ReadOnlyVariant(rows, rowsReadOnly) is { } same)
            {
                json.WriteString("rowsReadOnly", same);
            }
            else
            {
                json.WritePropertyName("rowsReadOnly");
                json.WriteLines(rowsReadOnly);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();

        // What an edit of a document that could not be read is told, on the first element there is.
        var readOnlyTarget = elements.FirstOrDefault() ?? Unknown;
        var readOnlySet = await session.ReadOnlyProperties.SetAsync(
            session.Target(readOnlyTarget), CausalLoopContextPropertyProvider.VariableLabelProperty, "Parity", cancellationToken);
        json.WriteString("readOnlySet", $"set {readOnlyTarget} {CausalLoopContextPropertyProvider.VariableLabelProperty} => {(readOnlySet.IsSuccess ? "ok" : $"refused: {readOnlySet.Error}")}");

        json.WritePropertyName("findings");
        json.WriteLines(await LinesAsync(async () =>
        {
            var problems = await new CausalLoopValidator(Diagram.CausalLoop.Origin).ValidateAsync(
                new DiagramValidationRequest(TranscriptText.Decode(bytes), Path.GetFileNameWithoutExtension(session.Body), session.Folder, session.Body, null),
                cancellationToken);
            return [.. problems.Select(problem => $"{Location(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}")];
        }));

        json.WritePropertyName("payloads");
        json.WriteLines(await LinesAsync(async () => [.. (await session.BaselineAsync(cancellationToken))
            .Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]));
    }

    /// <summary>
    /// The lines an answer is written as, or one line naming what it threw: today's code throws on
    /// some documents of the corpus (two variables of one name), and what it does is what is frozen.
    /// </summary>
    private static async Task<IReadOnlyList<string>> LinesAsync(Func<Task<IReadOnlyList<string>>> answer)
    {
        try
        {
            return await answer();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [Thrown(exception)];
        }
    }

    /// <summary>An exception as the transcript writes it: its type and its message, never its stack.</summary>
    private static string Thrown(Exception exception) => $"throws {exception.GetType().FullName}: {exception.Message}";

    private static string Location(DiagramProblemLocation? location) => location switch
    {
        DiagramProblemLineLocation line => $"line {line.Number}",
        DiagramProblemElementLocation element => $"element {TranscriptText.Quoted(element.Id)}",
        null => "nowhere",
        _ => location.ToString(),
    };

    /// <summary>
    /// The scripted edit sequence: the property rows, the menus' actions, their dialogs' commits, the
    /// connect gesture, a move and the arrangement, each run through the real history on a copy of
    /// the document, in an order that reaches refusals too.
    /// </summary>
    /// <remarks>
    /// A link is edited first, before any rename moves its ends, and its delay is toggled before its
    /// polarity is touched, so that a link written in a spelling of its own shows what one edit of it
    /// writes (finding B5). The removals come last and take whatever the document then holds.
    /// </remarks>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var script = new Script(json, session, cancellationToken);
        await script.StartAsync();

        var model = session.Model;
        var variables = model.Variables.Select(variable => variable.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var first = variables.FirstOrDefault();
        var second = variables.Skip(1).FirstOrDefault();
        var link = model.Links.FirstOrDefault()?.Id;
        var loop = model.Loops.FirstOrDefault(candidate => candidate.Identifier.Length > 0);

        if (link is not null)
        {
            await script.ExecuteAsync(link, CausalLoopContextActionProvider.ToggleDelayActionId);
            await script.ExecuteAsync(link, CausalLoopContextActionProvider.FlipCurvatureActionId);
            await script.SetAsync(link, "causal-loop.link-label", "Parity note");
            await script.SetAsync(link, CausalLoopContextPropertyProvider.LinkWeightProperty, "heavy");
            await script.SetAsync(link, CausalLoopContextPropertyProvider.LinkWeightProperty, "1e21");
            await script.DescribeAsync(link);
            await script.SetAsync(link, CausalLoopContextPropertyProvider.LinkWeightProperty, "0.000001");
            await script.SetAsync(link, CausalLoopContextPropertyProvider.LinkWeightProperty, "");
            await script.SetAsync(link, CausalLoopContextPropertyProvider.LinkPolarityProperty, "sideways");
            await script.SetAsync(link, CausalLoopContextPropertyProvider.LinkPolarityProperty, "unstated");
            await script.SetAsync(link, CausalLoopContextPropertyProvider.LinkDelayedProperty, "true");
            await script.SetAsync(link, CausalLoopContextPropertyProvider.LinkFromProperty, Nobody);
            await script.ExecuteAsync(link, CausalLoopContextActionProvider.MakePositiveActionId);
            await script.ExecuteAsync(link, CausalLoopContextActionProvider.MakeNegativeActionId);
            await script.ExecuteAsync(link, CausalLoopContextActionProvider.MakeNegativeActionId);
        }

        if (loop is not null)
        {
            await script.ExecuteAsync(loop.Id, "causal-loop.rename-loop");
            await script.CommitAsync(loop.Id, "causal-loop.rename-loop", "Parity loop");
            await script.SetAsync(loop.Id, "causal-loop.loop-name", "Parity name");
            await script.SetAsync(loop.Id, "causal-loop.loop-name", "Has \"quote\"");

            (string? current, IReadOnlyList<string>? candidates) = await session.RowValueAsync(loop.Id, CausalLoopContextPropertyProvider.LoopVariablesProperty, cancellationToken);
            await script.SetAsync(loop.Id, CausalLoopContextPropertyProvider.LoopVariablesProperty, current ?? "");
            await script.SetAsync(loop.Id, CausalLoopContextPropertyProvider.LoopVariablesProperty, "nobody → nowhere");
            if (candidates?.FirstOrDefault(candidate => candidate != current) is { } other)
            {
                await script.SetAsync(loop.Id, CausalLoopContextPropertyProvider.LoopVariablesProperty, other);
            }

            if (model.Loops.Skip(1).FirstOrDefault(candidate => candidate.Identifier.Length > 0) is { } taken)
            {
                await script.SetAsync(loop.Id, CausalLoopContextPropertyProvider.LoopIdentifierProperty, taken.Identifier);
            }

            await script.SetAsync(loop.Id, CausalLoopContextPropertyProvider.LoopIdentifierProperty, "");
            await script.SetAsync(loop.Id, CausalLoopContextPropertyProvider.LoopIdentifierProperty, "B99");
        }

        if (first is not null)
        {
            var variable = $"variable:{first}";
            await script.ExecuteAsync(variable, CausalLoopContextActionProvider.RenameVariableActionId);
            await script.ValidateAsync(variable, CausalLoopContextActionProvider.RenameVariableActionId, "Say \"hi\"");
            await script.ValidateAsync(variable, CausalLoopContextActionProvider.RenameVariableActionId, "");
            await script.CommitAsync(variable, CausalLoopContextActionProvider.RenameVariableActionId, "Parity renamed");

            // Finding B13: the label row takes what the rename dialog refuses.
            await script.SetAsync(variable, CausalLoopContextPropertyProvider.VariableLabelProperty, "Say \"hi\"");
            await script.DescribeAsync(variable);
            await script.SetAsync(variable, CausalLoopContextPropertyProvider.VariableLabelProperty, "Parity label");

            await script.ValidateAsync(variable, CausalLoopContextActionProvider.AddLinkActionId, Nobody);
            await script.ValidateAsync(variable, CausalLoopContextActionProvider.AddLinkActionId, second ?? first);
            await script.ExecuteAsync(variable, CausalLoopContextActionProvider.AddLinkActionId);
            await script.CommitAsync(variable, CausalLoopContextActionProvider.AddLinkActionId, second ?? first);
            await script.CommitAsync(variable, CausalLoopContextActionProvider.AddLinkActionId, Nobody);

            await script.ExecuteAsync(variable, CausalLoopContextActionProvider.AddLoopActionId);
            if (session.Model.Loops.FirstOrDefault(candidate => candidate.Identifier.Length > 0) is { } existing)
            {
                await script.ValidateAsync(variable, CausalLoopContextActionProvider.AddLoopActionId, existing.Identifier);
            }

            await script.CommitAsync(variable, CausalLoopContextActionProvider.AddLoopActionId, "R99");

            if (second is not null)
            {
                await script.SetAsync(variable, CausalLoopContextPropertyProvider.VariableIdProperty, second);
            }

            await script.SetAsync(variable, CausalLoopContextPropertyProvider.VariableIdProperty, "has space");
            await script.SetAsync(variable, CausalLoopContextPropertyProvider.VariableIdProperty, "");
            await script.SetAsync(variable, CausalLoopContextPropertyProvider.VariableIdProperty, "parityId");
            first = session.Model.Declares("parityId") ? "parityId" : first;
        }

        if (first is not null && second is not null)
        {
            await script.ExecuteAsync(GestureIds.Relation(second, first), CausalLoopContextActionProvider.ConnectActionId);
            await script.ExecuteAsync(GestureIds.Relation(second, first), CausalLoopContextActionProvider.ConnectActionId);
            await script.ExecuteAsync(GestureIds.Relation(first, first), CausalLoopContextActionProvider.ConnectActionId);
            await script.ExecuteAsync(GestureIds.Relation(first, Nobody), CausalLoopContextActionProvider.ConnectActionId);
        }

        await script.ExecuteAsync(Placement, CausalLoopContextActionProvider.AddVariableActionId);
        await script.ExecuteAsync("", CausalLoopContextActionProvider.AddVariableActionId);

        if (second is not null)
        {
            await script.MoveAsync($"variable:{second}", 100, 200);
        }

        if (session.Model.Links.FirstOrDefault() is { } anyLink)
        {
            await script.MoveAsync(anyLink.Id, 100, 200);
        }

        await script.ExecuteAsync(Placement, CausalLoopContextActionProvider.ArrangeActionId);

        await script.ExecuteAsync(Unknown, CausalLoopContextActionProvider.MakePositiveActionId);
        await script.ExecuteAsync(Unknown, CausalLoopContextActionProvider.RemoveVariableActionId);
        await script.CommitAsync(Unknown, CausalLoopContextActionProvider.RenameVariableActionId, "Nobody");
        await script.SetAsync(Unknown, CausalLoopContextPropertyProvider.VariableLabelProperty, "Nobody");
        await script.ExecuteAsync(Unknown, "timeline.not-ours");

        if (session.Model.Links.FirstOrDefault() is { } removedLink)
        {
            await script.ExecuteAsync(removedLink.Id, CausalLoopContextActionProvider.RemoveLinkActionId);
        }

        if (session.Model.Loops.FirstOrDefault() is { } removedLoop)
        {
            await script.ExecuteAsync(removedLoop.Id, CausalLoopContextActionProvider.RemoveLoopActionId);
        }

        // Finding B4: a variable's removal takes the links and the loops that name it.
        if (first is not null && await script.ExecuteAsync($"variable:{first}", CausalLoopContextActionProvider.RemoveVariableActionId) is "confirm")
        {
            await script.CommitAsync($"variable:{first}", CausalLoopContextActionProvider.RemoveVariableActionId, "");
        }

        await script.EndAsync();
    }

    /// <summary>One document's services, store, history, open session and providers, readable and read-only.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly CausalLoopSession _session;
        private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

        public Session(string folder, string body, string? registration)
        {
            Folder = folder;
            Body = body;
            Registration = registration;

            var registry = new DiagramViewportRegistry();
            _services = Services(registry);
            Store = _services.GetRequiredService<ICausalLoopDocumentStore>();
            History = _services.GetRequiredService<IHistoryStackStore>();

            // Registered as the diagram service registers an open stream, so an action reaches it.
            _session = new CausalLoopSession(body, registration, Store, new CausalLoopElementMapper(), History.Get(folder));
            registry.Register(_watchId, body, _session, _ => { });

            var readOnly = new ReadOnlyStore(Store);
            Actions = new CausalLoopContextActionProvider(Store, History, registry);
            ReadOnlyActions = new CausalLoopContextActionProvider(readOnly, History, registry);
            Properties = new CausalLoopContextPropertyProvider(History, Store);
            ReadOnlyProperties = new CausalLoopContextPropertyProvider(History, readOnly);
        }

        public string Folder { get; }

        public string Body { get; }

        public string? Registration { get; }

        private ICausalLoopDocumentStore Store { get; }

        public IHistoryStackStore History { get; }

        public CausalLoopContextActionProvider Actions { get; }

        public CausalLoopContextActionProvider ReadOnlyActions { get; }

        public CausalLoopContextPropertyProvider Properties { get; }

        public CausalLoopContextPropertyProvider ReadOnlyProperties { get; }

        /// <summary>Opens the document, as the diagram's first open does; the readings and edits read what it holds.</summary>
        public async Task ReadAsync(CancellationToken cancellationToken) => await Store.GetOrLoadAsync(Body, cancellationToken);

        public CausalLoopModel Model => Store.Get(Body)!.Model;

        public ContextTarget Target(string elementId) =>
            ContextTarget.ForElement(Body, elementId, ProjectRoot.From(Folder), _watchId, Diagram.CausalLoop.Origin);

        /// <summary>What the open session sends a connection that has reported no viewport.</summary>
        public async Task<IEnumerable<DiagramElement>> BaselineAsync(CancellationToken cancellationToken) =>
            (await _session.BaselineAsync(cancellationToken)).OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements);

        public Task<string> MoveAsync(string elementId, double x, double y, CancellationToken cancellationToken) =>
            _session.MoveElementToAsync(elementId, x, y, cancellationToken);

        /// <summary>A row's value and candidates as the property grid is given them.</summary>
        public async Task<(string? Value, IReadOnlyList<string>? Candidates)> RowValueAsync(string target, string propertyId, CancellationToken cancellationToken)
        {
            try
            {
                var row = (await Properties.DescribeAsync(Target(target), cancellationToken)).FirstOrDefault(candidate => candidate.Id == propertyId);
                return (row?.Value, row?.Candidates);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // What it throws is recorded by the readings; the script then sets what it can.
                return (null, null);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _session.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    /// <summary>The store's own entries, each marked as unreadable: the backend's one read-only case.</summary>
    private sealed class ReadOnlyStore(ICausalLoopDocumentStore inner) : ICausalLoopDocumentStore
    {
        public event EventHandler<DocumentChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public async ValueTask<DocumentEntry<CausalLoopBody, CausalLoopModel>> GetOrLoadAsync(string path, CancellationToken cancellationToken) => await inner.GetOrLoadAsync(path, cancellationToken) with { Unreadable = "parity: read-only" };

        public DocumentEntry<CausalLoopBody, CausalLoopModel>? Get(string path) => inner.Get(path) is { } held ? held with { Unreadable = "parity: read-only" } : null;

        public Task<DocumentSaveResult> SaveAsync(string path, CausalLoopBody document, CancellationToken cancellationToken) => inner.SaveAsync(path, document, cancellationToken);

        public void Forget(string path) => inner.Forget(path);

        public Task BodyDeletedAsync(string path, CancellationToken cancellationToken) => inner.BodyDeletedAsync(path, cancellationToken);

        public Task ReloadAsync(string path, CancellationToken cancellationToken) => inner.ReloadAsync(path, cancellationToken);
    }

    /// <summary>Runs the steps of one document's edit sequence and writes each as it goes.</summary>
    private sealed class Script(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        private string _original = "";
        private string _text = "";
        private string _originalRegistration = "";
        private string _registration = "";

        public async Task StartAsync()
        {
            _original = _text = await ReadAsync(session.Body);
            _originalRegistration = _registration = session.Registration is { } registration ? await ReadAsync(registration) : "";
            json.WriteStartArray("edits");
        }

        public async Task EndAsync()
        {
            json.WriteEndArray();

            var stack = session.History.Get(session.Folder);
            var undone = 0;
            var threw = "";
            try
            {
                while (stack.CanUndo && (await stack.UndoAsync(cancellationToken)).IsSuccess)
                {
                    undone++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The undo that threw is not counted, and none after it is tried.
                threw = $"; the next {Thrown(exception)}";
            }

            var restored = await ReadAsync(session.Body);
            var restoredRegistration = session.Registration is { } registration ? await ReadAsync(registration) : "";
            var back = string.Equals(restored, _original, StringComparison.Ordinal) && string.Equals(restoredRegistration, _originalRegistration, StringComparison.Ordinal);
            json.WriteString("undoAll", $"{undone} undone{threw}; {(back ? "the original text is back" : "the original text is NOT back")}");
            if (!back)
            {
                // What undoing everything left different, so a change in it is visible rather than
                // hidden behind the same sentence.
                json.WriteStartObject("undoAllLeaves");
                json.WriteChange(_original, restored);
                json.WriteEndObject();
                json.WriteStartObject("undoAllLeavesRegistration");
                json.WriteChange(_originalRegistration, restoredRegistration);
                json.WriteEndObject();
            }
        }

        public async Task SetAsync(string target, string propertyId, string value)
        {
            await RecordAsync($"set {target} {propertyId} = {TranscriptText.Quoted(value)}", async () =>
            {
                var result = await session.Properties.SetAsync(session.Target(target), propertyId, value, cancellationToken);
                return result.IsSuccess ? "ok" : $"refused: {result.Error}";
            });
        }

        public async Task ValidateAsync(string target, string actionId, string value)
        {
            await RecordAsync($"validate {actionId} on {target} = {TranscriptText.Quoted(value)}", async () =>
            {
                var result = await session.Actions.ValidateAsync(session.Target(target), actionId, value, cancellationToken);
                return result.Valid ? "accepted" : $"rejected: {result.Reason}";
            });
        }

        /// <summary>Runs an action; answers "confirm" when it asked for a confirmation, else null.</summary>
        public async Task<string?> ExecuteAsync(string target, string actionId)
        {
            var confirms = false;
            await RecordAsync($"execute {actionId} on {target}", async () =>
            {
                var result = await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken);
                confirms = result is ContextExecutionRequiresConfirmation;
                return Describe(result);
            });
            return confirms ? "confirm" : null;
        }

        public async Task CommitAsync(string target, string actionId, string value)
        {
            await RecordAsync($"commit {actionId} on {target} = {TranscriptText.Quoted(value)}", async () =>
            {
                var result = await session.Actions.CommitAsync(session.Target(target), actionId, value, "", cancellationToken);
                return result.Completed ? "ok" : $"refused: {result.Error}";
            });
        }

        /// <summary>A drag's end: the move the open session is asked for.</summary>
        public async Task MoveAsync(string elementId, int x, int y)
        {
            await RecordAsync($"move {elementId} to ({x}, {y})", async () =>
            {
                var refusal = await session.MoveAsync(elementId, x, y, cancellationToken);
                return refusal.Length == 0 ? "ok" : $"refused: {refusal}";
            });
        }

        /// <summary>The rows a target reads back as now, so what an edit wrote is seen as it is read.</summary>
        public async Task DescribeAsync(string target)
        {
            await RecordAsync($"describe {target}", async () =>
            {
                var rows = await session.Properties.DescribeAsync(session.Target(target), cancellationToken);
                return string.Join(" || ", rows.Select(row => row.RowLine()));
            });
        }

        private async Task RecordAsync(string step, Func<Task<string>> run)
        {
            string answer;
            try
            {
                answer = await run();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            var after = await ReadAsync(session.Body);
            var registrationAfter = session.Registration is { } registration ? await ReadAsync(registration) : "";

            json.WriteStartObject();
            json.WriteString("do", step);
            json.WriteString("answer", answer);
            json.WriteChange(_text, after);
            if (!string.Equals(_registration, registrationAfter, StringComparison.Ordinal))
            {
                json.WriteStartObject("registration");
                json.WriteChange(_registration, registrationAfter);
                json.WriteEndObject();
            }

            json.WriteEndObject();
            _text = after;
            _registration = registrationAfter;
        }

        private async Task<string> ReadAsync(string path) => TranscriptText.Decode(await File.ReadAllBytesAsync(path, cancellationToken));

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
