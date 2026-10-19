using System.Globalization;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// The C4 module's parity transcript: what today's hand-written code answers for every document of
/// the corpus, frozen as <c>Parity/c4.transcript.json</c> before any provider, parser or writer is
/// changed (c4-disl-fbl Requirement 1). Every later step must reproduce it byte for byte, or change it
/// only as that specification names.
/// </summary>
/// <remarks>
/// <para>
/// <b>Once</b>: the toolbox of each of the six types that draw a view, what <c>c4/code</c> answers
/// when a document of it is created, and the new document each of the six writes for a handful of
/// base names (finding B23).
/// </para>
/// <para>
/// <b>Per document</b> of <see cref="C4Corpus"/>, read in a temporary folder of its own: which view
/// each registration beside it opens, and three scripted registrations besides (finding B14); the
/// findings, once, with a note when the six validators do not agree; the context menu and the
/// property rows of every element and relationship of the model, of the empty canvas and of an
/// unknown id. <b>Per view</b>: its title, how many stored positions it has and whether they are
/// ignored (finding A12), the menu and rows of everything it draws, under the origin of the view's
/// own kind, and the payload of every element it draws.
/// </para>
/// <para>
/// <b>An edit script</b> that runs every command once: a drag on every view, the property rows and
/// the menu actions of one element of each kind and of the first relationships, the adds, the view
/// command no gesture reaches (finding B24), and the refusals. Each step is written with its answer
/// and the hunks it made to the body, to the sidecar beside it and to a registration it wrote; the
/// steps whose inverse is the module's own (a drag, an add, a view) also with their undo and redo.
/// The script ends by undoing everything and saying whether the original text is back.
/// </para>
/// <para>
/// <b>Nothing is masked</b>: no id is minted and no clock is read. <b>One thing is normalised</b>:
/// the sidecar is compared with its line endings read as LF, because the module writes it with
/// <see cref="Environment.NewLine"/> (finding B16), which would make the transcript differ between
/// the machines it is run on.
/// </para>
/// </remarks>
internal static class C4Transcript
{
    /// <summary>The checked-in file's name, in this project's <c>Parity</c> folder.</summary>
    private const string FileName = "c4.transcript.json";

    private const string Unknown = "parity:unknown";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(C4Reflection.Descriptor);

    /// <summary>The checked-in transcript.</summary>
    public static string CheckedInPath => Path.Combine(C4Corpus.ModuleFolder, "backend", C4Corpus.TestProject, "Parity", FileName);

    /// <summary>The base names each type's new document is written for (finding B23).</summary>
    private static readonly string[] _newBaseNames = ["parity", "Bottling MES", "Ünïcode \"quoted\"", "42 things", "--", " "];

    /// <summary>The whole transcript, as the bytes the checked-in file must hold.</summary>
    public static async Task<byte[]> GenerateAsync(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await using (var json = new Utf8JsonWriter(stream, TranscriptText.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("module", "c4");
            json.WriteString("generator", $"src/diagrams/c4/backend/{C4Corpus.TestProject}/Parity/C4Transcript.cs");
            json.WriteString("regenerate", "ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)");

            await using (var services = new ServiceCollection().AddC4().BuildServiceProvider())
            {
                // The toolbox is static per type: it does not read the document, so it is written once.
                json.WriteStartObject("toolbox");
                foreach (var toolbox in services.GetServices<IDiagramToolboxProvider>())
                {
                    json.WritePropertyName(toolbox.Origin.Key);
                    json.WriteLines(toolbox.Items.Select(item => $"{item.Id} | {item.Label} | {item.Icon} | drops {item.DropActionId} | {item.Description}"));
                }

                json.WriteEndObject();

                var factories = services.GetServices<IDiagramDocumentFactory>().ToList();
                json.WriteStartObject("newDocuments");
                foreach (var factory in factories)
                {
                    json.WriteStartObject(factory.Origin.Key);
                    foreach (var baseName in _newBaseNames)
                    {
                        json.WriteString(baseName, NewDocument(factory, baseName));
                    }

                    json.WriteEndObject();
                }

                json.WriteEndObject();
            }

            json.WriteStartArray("documents");
            var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var document in await C4Corpus.DocumentsAsync(cancellationToken))
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

    private static string NewDocument(IDiagramDocumentFactory factory, string baseName)
    {
        try
        {
            return factory.CreateEmptyDocument(baseName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Thrown(exception);
        }
    }

    private static string Thrown(Exception exception) => $"throws {exception.GetType().Name}: {exception.Message}";

    private static async Task WriteDocumentAsync(Utf8JsonWriter json, C4Corpus.Document document, Dictionary<string, string> recorded, CancellationToken cancellationToken)
    {
        var sha = TranscriptText.Sha256(document.Bytes);
        json.WriteStartObject();
        json.WriteString("document", document.Relative);
        json.WriteString("sha256", sha);
        if (document.Companions.Count > 0)
        {
            json.WritePropertyName("beside");
            json.WriteLines(document.Companions.Select(companion => $"{companion.Name} | {TranscriptText.Sha256(companion.Bytes)}"));
        }

        // A copy is the same reading only where everything beside it is the same too.
        var key = string.Join(" ", document.Companions.Select(companion => $"{companion.Name}={TranscriptText.Sha256(companion.Bytes)}").Prepend(sha));
        if (recorded.TryGetValue(key, out var first))
        {
            json.WriteString("sameAs", first);
            json.WriteEndObject();
            return;
        }

        recorded[key] = document.Relative;

        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.C4Transcript", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var body = Path.Combine(folder, Path.GetFileName(document.Relative));
            await File.WriteAllBytesAsync(body, document.Bytes, cancellationToken);
            foreach ((string name, byte[] bytes) in document.Companions)
            {
                await File.WriteAllBytesAsync(Path.Combine(folder, name), bytes, cancellationToken);
            }

            await using var session = new Session(folder, body);

            await session.ReadAsync(cancellationToken);
            await WriteRegistrationsAsync(json, session, cancellationToken);
            await WriteReadingsAsync(json, session, cancellationToken);
            await WriteEditsAsync(json, session, cancellationToken);
        }
        finally
        {
            TestFolder.TryDelete(folder);
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// Which view each registration beside the document opens, as the session factory of the type its
    /// first line names opens it; then three registrations written for the purpose (finding B14).
    /// </summary>
    private static async Task WriteRegistrationsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        foreach (var registration in Directory.GetFiles(session.Folder, "*.adp").Order(StringComparer.Ordinal))
        {
            var text = await File.ReadAllTextAsync(registration, cancellationToken);
            var header = text.Split('\n').Select(line => line.Trim()).ToList();
            var bodyLine = header.FirstOrDefault(line => line.StartsWith("body:", StringComparison.OrdinalIgnoreCase));
            var body = bodyLine is null ? Path.GetFileNameWithoutExtension(registration) + Diagram.DocumentExtension : bodyLine["body:".Length..].Trim();
            if (!string.Equals(body, Path.GetFileName(session.Body), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lines.Add($"{Path.GetFileName(registration)} | {TranscriptText.Quoted(text)} => {await OpensAsync(session, header[0], registration)}");
        }

        var scripted = Path.Combine(session.Folder, "scripted");
        Directory.CreateDirectory(scripted);
        var views = session.Model.Views;
        var other = views.Count > 0 ? views[^1].Key.ToUpperInvariant() : "ANYTHING";
        lines.Add($"no registration | c4/context => {await OpensAsync(session, "c4/context", null)}");
        foreach (var text in new[] { $"c4/context\nview: {other}\n", "c4/context\nview: parity-missing\n", "c4/context\nview:\n" })
        {
            var registration = Path.Combine(scripted, $"scripted{lines.Count}.adp");
            await File.WriteAllTextAsync(registration, text, cancellationToken);
            lines.Add($"scripted | {TranscriptText.Quoted(text)} => {await OpensAsync(session, "c4/context", registration)}");
        }

        TestFolder.TryDelete(scripted);
        json.WritePropertyName("registrations");
        json.WriteLines(lines);
    }

    private static async Task<string> OpensAsync(Session session, string originKey, string? registration)
    {
        var factory = session.Services.GetServices<IDiagramSessionFactory>().FirstOrDefault(candidate => candidate.Origin.Key == originKey.Trim());
        if (factory is null)
        {
            return $"no session factory answers {TranscriptText.Quoted(originKey.Trim())}";
        }

        await using var opened = factory.Open(ShortGuid.NewShortGuid(), session.Folder, session.Body, registration);
        return await ((C4Session)opened).ViewAsync(CancellationToken.None) is { } view ? $"shows {Describe(view)}" : "shows no view";
    }

    private static string Describe(C4View view) => $"{TranscriptText.Quoted(view.Key)} ({view.Kind}, line {view.Line})";

    private static async Task WriteReadingsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var workspace = session.Model;

        // The six validators are one rule set under six origins; written once, unless they disagree.
        var findings = new List<(string Origin, IReadOnlyList<string> Lines)>();
        foreach ((ToolOrigin origin, _) in C4Corpus.Types)
        {
            var problems = await new C4Validator(origin).ValidateAsync(
                new DiagramValidationRequest(await File.ReadAllTextAsync(session.Body, cancellationToken), Path.GetFileNameWithoutExtension(session.Body), session.Folder, session.Body, null),
                cancellationToken);
            findings.Add((origin.Key, [.. problems.Select(problem => $"{Location(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}")]));
        }

        json.WritePropertyName("findings");
        json.WriteLines(findings[0].Lines);
        foreach ((string origin, IReadOnlyList<string> lines) in findings.Where(each => !each.Lines.SequenceEqual(findings[0].Lines, StringComparer.Ordinal)))
        {
            json.WritePropertyName($"findings {origin}");
            json.WriteLines(lines);
        }

        var menus = new List<IReadOnlyList<string>>();
        var choices = new List<IReadOnlyList<string>>();
        var rowSets = new List<IReadOnlyList<string>>();

        // The model's own targets, under the origin of the first view's kind: what a reader that
        // draws none of them would still be answered.
        var documentOrigin = workspace.Views.Count > 0 ? C4Corpus.OriginOf(workspace.Views[0].Kind) : Diagram.SystemContext.Origin;
        List<string> modelTargets =
        [
            .. workspace.Elements.Select(element => element.Id)
                .Concat(workspace.Relationships.Select(relationship => relationship.Id))
                .Distinct(StringComparer.Ordinal),
            "",
            Unknown,
        ];
        var targetLines = new List<string>();
        foreach (var target in modelTargets)
        {
            targetLines.Add(await TargetLineAsync(session, target, documentOrigin, menus, choices, rowSets, cancellationToken));
        }

        var viewLines = new List<(C4View View, string Title, string Layout, List<string> Targets, IReadOnlyList<string> Payloads)>();
        foreach (var view in workspace.Views)
        {
            var origin = C4Corpus.OriginOf(view.Kind);
            var authored = session.Sidecar.Read(session.Body, view.Key);
            var layout = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default, authored);
            var drawn = session.Mapper.Visible(workspace, view, DiagramViewport.Unbounded, session.Body);
            var targets = new List<string>();
            foreach (var element in drawn)
            {
                targets.Add(await TargetLineAsync(session, element.Id, origin, menus, choices, rowSets, cancellationToken));
            }

            targets.Add(await TargetLineAsync(session, "", origin, menus, choices, rowSets, cancellationToken));
            viewLines.Add((
                view,
                C4ElementMapper.TitleOf(workspace, view),
                $"{authored.Count} stored positions{(layout.AuthoredPositionsIgnored ? ", ignored because the view declares autoLayout" : "")}",
                targets,
                [.. drawn.Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]));
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

        json.WriteStartArray("rowSets");
        foreach (var rows in rowSets)
        {
            json.WriteLines(rows);
        }

        json.WriteEndArray();

        json.WritePropertyName("targets");
        json.WriteLines(targetLines);

        json.WriteStartArray("views");
        foreach ((C4View view, string title, string layout, List<string> targets, IReadOnlyList<string> payloads) in viewLines)
        {
            json.WriteStartObject();
            json.WriteString("view", Describe(view));
            json.WriteString("origin", C4Corpus.OriginOf(view.Kind).Key);
            json.WriteString("title", title);
            json.WriteString("layout", layout);
            json.WritePropertyName("targets");
            json.WriteLines(targets);
            json.WritePropertyName("payloads");
            json.WriteLines(payloads);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>One target as a line: its menu and its rows, each as an index into the document's tables.</summary>
    private static async Task<string> TargetLineAsync(
        Session session,
        string target,
        ToolOrigin origin,
        List<IReadOnlyList<string>> menus,
        List<IReadOnlyList<string>> choices,
        List<IReadOnlyList<string>> rowSets,
        CancellationToken cancellationToken)
    {
        var menu = menus.IndexOfOrAdd((await session.Actions.DiscoverAsync(session.Target(target, origin), cancellationToken)).MenuLines());
        IReadOnlyList<string> rows = [.. (await session.Properties.DescribeAsync(session.Target(target, origin), cancellationToken)).Select(row => row.RowLine(choices))];
        return $"{TranscriptText.Quoted(target)} => menu #{menu}, {(rows.Count == 0 ? "no rows" : $"rows #{rowSets.IndexOfOrAdd(rows)}")}";
    }

    private static string Location(DiagramProblemLocation? location) => location switch
    {
        DiagramProblemLineLocation line => $"line {line.Number}",
        DiagramProblemElementLocation element => $"element {TranscriptText.Quoted(element.Id)}",
        null => "nowhere",
        _ => location.ToString(),
    };

    /// <summary>The kinds the script edits one element of, in this order.</summary>
    private static readonly C4ElementKind[] _scriptedKinds = Enum.GetValues<C4ElementKind>();

    /// <summary>
    /// The scripted edit sequence: every command of the module once, through the real history on a
    /// copy of the document, in an order that reaches its refusals too.
    /// </summary>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var script = new Script(json, session, cancellationToken);
        var original = await script.StartAsync();

        var workspace = session.Model;
        var relationshipIds = workspace.Relationships.Select(relationship => relationship.Id).ToHashSet(StringComparer.Ordinal);

        // A drag on every view, of the first element it draws, and its undo and redo; then the
        // drags that are refused. Before anything else, so the drawn positions are today's reading.
        string? lifted = null;
        foreach (var view in workspace.Views)
        {
            var drawn = session.Mapper.Visible(workspace, view, DiagramViewport.Unbounded, session.Body);
            lifted ??= drawn.FirstOrDefault(element => element.Type == C4ElementMapper.RelationshipType && !relationshipIds.Contains(element.Id))?.Id;
            if (drawn.FirstOrDefault(element => element.Type == C4ElementMapper.NodeType) is { } moved)
            {
                await script.MoveAsync(view.Key, moved.Id, moved.X + 40, moved.Y + 20.5);
            }
        }

        var firstView = workspace.Views.Count > 0 ? workspace.Views[0].Key : "parity-missing";
        await script.MoveAsync(firstView, Unknown, 10, 10);
        await script.MoveAsync(firstView, "c4:view", 10, 10);
        await script.MoveAsync("parity-missing", workspace.Elements.FirstOrDefault()?.Id ?? Unknown, 10, 10);

        // One element of each kind: its rows, its menu's actions and their commits.
        var picked = _scriptedKinds
            .Select(kind => workspace.Elements.FirstOrDefault(element => element.Kind == kind))
            .OfType<C4Element>()
            .ToList();
        var relationship = workspace.Relationships.FirstOrDefault(candidate => workspace.Find(candidate.SourceId) is not null && workspace.Find(candidate.DestinationId) is not null);
        var untechnical = workspace.Relationships.FirstOrDefault(candidate => candidate.Technology.Length == 0 && candidate != relationship);
        var dangling = workspace.Relationships.FirstOrDefault(candidate => workspace.Find(candidate.SourceId) is null || workspace.Find(candidate.DestinationId) is null);
        var person = workspace.Elements.FirstOrDefault(element => element.Kind == C4ElementKind.Person)?.Id;
        var system = workspace.Elements.FirstOrDefault(element => element.Kind == C4ElementKind.SoftwareSystem)?.Id;
        var container = workspace.Elements.FirstOrDefault(element => element.Kind == C4ElementKind.Container)?.Id;

        foreach (var element in picked)
        {
            var id = element.Id;
            await script.SetAsync(id, C4ContextPropertyProvider.NamePropertyId, "Parity \"name\"", undoAndRedo: true);
            await script.SetAsync(id, C4ContextPropertyProvider.DescriptionPropertyId, "Parity description");
            await script.SetAsync(id, C4ContextPropertyProvider.TechnologyPropertyId, "Parity technology");
            await script.SetAsync(id, "c4.tags", "Parity");
            await script.SetAsync(id, C4ContextPropertyProvider.IdentifierPropertyId, "parity");
            await script.ExecuteAsync(id, C4ContextActionProvider.RenameActionId);
            await script.ExecuteAsync(id, C4ContextActionProvider.EditDescriptionActionId);
            await script.ExecuteAsync(id, C4ContextActionProvider.SetTechnologyActionId);
            await script.ValidateAsync(id, C4ContextActionProvider.RenameActionId, " ");
            await script.ValidateAsync(id, C4ContextActionProvider.EditDescriptionActionId, "");
            await script.CommitAsync(id, C4ContextActionProvider.RenameActionId, "Parity renamed");
            await script.CommitAsync(id, C4ContextActionProvider.EditDescriptionActionId, "Parity described");
            await script.CommitAsync(id, C4ContextActionProvider.SetTechnologyActionId, "Parity tech");
            await script.CommitAsync(id, C4ContextActionProvider.RenameActionId, "");
        }

        // The relationships, their ids taken before anything is added above them (finding A7).
        if (relationship is not null)
        {
            await script.SetAsync(relationship.Id, C4ContextPropertyProvider.RelationshipDescriptionPropertyId, "Parity label", undoAndRedo: true);
            await script.SetAsync(relationship.Id, C4ContextPropertyProvider.RelationshipTechnologyPropertyId, "Parity protocol");
            await script.SetAsync(relationship.Id, C4ContextPropertyProvider.RelationshipSourcePropertyId, "parity");
            await script.ExecuteAsync(relationship.Id, C4ContextActionProvider.RelabelActionId);
            await script.ExecuteAsync(relationship.Id, C4ContextActionProvider.SetProtocolActionId);
            await script.CommitAsync(relationship.Id, C4ContextActionProvider.RelabelActionId, "Parity relabelled");
            await script.CommitAsync(relationship.Id, C4ContextActionProvider.SetProtocolActionId, "Parity protocol two");
            await script.CommitAsync(relationship.Id, C4ContextActionProvider.RenameActionId, "Not for a relationship");
        }

        if (untechnical is not null)
        {
            await script.SetAsync(untechnical.Id, C4ContextPropertyProvider.RelationshipTechnologyPropertyId, "Parity protocol");
        }

        if (dangling is not null)
        {
            await script.SetAsync(dangling.Id, C4ContextPropertyProvider.RelationshipDescriptionPropertyId, "Parity dangling");
        }

        // A line the view draws that is no relationship of the model (finding A10).
        if (lifted is not null)
        {
            await script.ExecuteAsync(lifted, C4ContextActionProvider.RelabelActionId);
            await script.SetAsync(lifted, C4ContextPropertyProvider.RelationshipDescriptionPropertyId, "Parity lifted");
        }

        // The adds: on the empty canvas, inside a parent, and refused (findings A9, B20).
        await script.ExecuteAsync("", "c4.add-person");
        await script.ValidateAsync("", "c4.add-person", "");
        await script.CommitAsync("", "c4.add-person", "Parity Person", undoAndRedo: true);
        await script.CommitAsync("", "c4.add-softwaresystem", "Web App", undoAndRedo: true);
        await script.CommitAsync("", "c4.add-softwaresystem", "Web App");
        await script.CommitAsync("", "c4.add-softwaresystem", "42 Things");
        await script.CommitAsync("", "c4.add-softwaresystem", "Ärger \"quoted\"");
        await script.CommitAsync("", "c4.add-softwaresystem", "!!!");
        await script.CommitAsync("", "c4.add-softwaresystem", "  ");
        await script.CommitAsync("", "c4.add-container", "Nowhere");
        await script.CommitAsync("", "c4.add-component", "Nowhere");
        await script.ExecuteAsync("", "c4.add-deploymentnode");
        await script.CommitAsync("", "c4.add-deploymentnode", "Server");
        if (system is not null)
        {
            await script.CommitAsync(system, "c4.add-container", "Parity Container", undoAndRedo: true);
        }

        if (container is not null)
        {
            await script.CommitAsync(container, "c4.add-component", "Parity Component", undoAndRedo: true);
        }

        if (person is not null)
        {
            await script.CommitAsync(person, "c4.add-container", "Wrong parent");
        }

        await script.CommitAsync(Unknown, "c4.add-container", "Orphan");

        // The relationship again, by the id it had before the adds.
        if (relationship is not null)
        {
            await script.SetAsync(relationship.Id, C4ContextPropertyProvider.RelationshipDescriptionPropertyId, "Parity label after the adds");
        }

        await script.ExecuteAsync(Unknown, C4ContextActionProvider.RenameActionId);
        await script.CommitAsync(Unknown, C4ContextActionProvider.RenameActionId, "Nobody");
        await script.SetAsync(Unknown, C4ContextPropertyProvider.NamePropertyId, "Nobody");

        // The view command, which no gesture constructs (finding B24).
        await script.AddViewAsync(C4ViewKind.Container, "ParityView", undoAndRedo: true);
        await script.AddViewAsync(C4ViewKind.SystemLandscape, workspace.Views.Count > 0 ? workspace.Views[0].Key : "ParityView");

        await script.EndAsync(original);
    }

    /// <summary>One document's services, store, history and providers, over a folder of its own.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

        public Session(string folder, string body)
        {
            Folder = folder;
            Body = body;
            Services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
            Documents = Services.GetRequiredService<IC4DocumentStore>();
            Sidecar = Services.GetRequiredService<C4LayoutSidecar>();
            Mapper = Services.GetRequiredService<C4ElementMapper>();
            History = Services.GetRequiredService<IHistoryStackStore>();
            Actions = new C4ContextActionProvider(History, Documents);
            Properties = new C4ContextPropertyProvider(History, Documents);
        }

        public string Folder { get; }

        public string Body { get; }

        public ServiceProvider Services { get; }

        public IC4DocumentStore Documents { get; }

        public C4LayoutSidecar Sidecar { get; }

        public C4ElementMapper Mapper { get; }

        private IHistoryStackStore History { get; }

        public C4ContextActionProvider Actions { get; }

        public C4ContextPropertyProvider Properties { get; }

        /// <summary>Opens the document, as the diagram's first open does; the readings and edits read what it holds.</summary>
        public async Task ReadAsync(CancellationToken cancellationToken) => await Documents.GetOrLoadAsync(Body, cancellationToken);

        public C4Workspace Model => Documents.Get(Body)!.Model;

        public IHistoryStack Stack => History.Get(ProjectRoot.From(Folder));

        public ContextTarget Target(string elementId, ToolOrigin? origin = null) =>
            ContextTarget.ForElement(Body, elementId, ProjectRoot.From(Folder), _watchId, origin ?? Diagram.SystemContext.Origin);

        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }

    /// <summary>Runs the steps of one document's edit sequence and writes each as it goes.</summary>
    private sealed class Script(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        private string _text = "";
        private string? _sidecar;
        private Dictionary<string, string> _registrations = [];

        public async Task<string> StartAsync()
        {
            (_text, _sidecar, _registrations) = await ReadAsync();
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
                while (session.Stack.CanUndo && (await session.Stack.UndoAsync(cancellationToken)).IsSuccess)
                {
                    undone++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                thrown = $"; the next {Thrown(exception)}";
            }

            (string restored, _, _) = await ReadAsync();
            var back = string.Equals(restored, original, StringComparison.Ordinal);
            json.WriteString("undoAll", $"{undone} undone{thrown}; {(back ? "the original text is back" : "the original text is NOT back")}");
            if (!back)
            {
                // What undoing everything left different, so a change in it is visible rather than
                // hidden behind the same sentence.
                json.WriteStartObject("undoAllLeaves");
                json.WriteChange(original, restored);
                json.WriteEndObject();
            }
        }

        public Task SetAsync(string target, string propertyId, string value, bool undoAndRedo = false) =>
            StepAsync($"set {target} {propertyId} = {TranscriptText.Quoted(value)}", undoAndRedo, async () =>
            {
                var result = await session.Properties.SetAsync(session.Target(target), propertyId, value, cancellationToken);
                return result.IsSuccess ? "ok" : $"refused: {result.Error}";
            });

        public Task ExecuteAsync(string target, string actionId) =>
            StepAsync($"execute {actionId} on {target}", false, async () => Describe(await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken)));

        public Task ValidateAsync(string target, string actionId, string value) =>
            StepAsync($"validate {actionId} on {target} = {TranscriptText.Quoted(value)}", false, async () =>
            {
                var result = await session.Actions.ValidateAsync(session.Target(target), actionId, value, cancellationToken);
                return result.Valid ? "accepted" : $"rejected: {result.Reason}";
            });

        public Task CommitAsync(string target, string actionId, string value, bool undoAndRedo = false) =>
            StepAsync($"commit {actionId} on {target} = {TranscriptText.Quoted(value)}", undoAndRedo, async () =>
            {
                var result = await session.Actions.CommitAsync(session.Target(target), actionId, value, "", cancellationToken);
                var next = result.Next is { } chained ? $"; then {Describe(chained)}" : "";
                return (result.Completed ? "ok" : $"refused: {result.Error}") + next;
            });

        /// <summary>A drag on the view <paramref name="viewKey"/>, as the session of that view takes a drop.</summary>
        public Task MoveAsync(string viewKey, string target, double x, double y) =>
            StepAsync($"move {target} on view {TranscriptText.Quoted(viewKey)} to {Number(x)}, {Number(y)}", true, async () =>
            {
                await using var view = new C4Session(session.Body, viewKey, session.Documents, session.Mapper, session.Stack);
                var refusal = await view.MoveElementToAsync(target, x, y, cancellationToken);
                return refusal.Length == 0 ? "ok" : $"refused: {refusal}";
            });

        /// <summary>The view command, with a registration beside the body that names it.</summary>
        public Task AddViewAsync(C4ViewKind kind, string key, bool undoAndRedo = false) =>
            StepAsync($"add view {kind} {TranscriptText.Quoted(key)}", undoAndRedo, async () =>
            {
                var command = new AddC4ViewCommand(
                    session.Body,
                    Path.Combine(session.Folder, "parity-view.adp"),
                    C4Corpus.OriginOf(kind).Key,
                    kind,
                    key,
                    Path.GetFileName(session.Body));
                var result = await session.Stack.ExecuteAsync(command, cancellationToken);
                return result.IsSuccess ? "ok" : $"refused: {result.Error}";
            });

        /// <summary>
        /// A step, and where it landed on the history and asks for it, its undo and its redo: each
        /// recorded with its answer and the hunks it made.
        /// </summary>
        private async Task StepAsync(string step, bool undoAndRedo, Func<Task<string>> run)
        {
            var before = session.Stack.Availability.UndoCount;
            await RecordAsync(step, await AnswerAsync(run));
            if (!undoAndRedo || session.Stack.Availability.UndoCount <= before)
            {
                return;
            }

            await RecordAsync($"undo {step}", await AnswerAsync(async () => Result(await session.Stack.UndoAsync(cancellationToken))));
            await RecordAsync($"redo {step}", await AnswerAsync(async () => Result(await session.Stack.RedoAsync(cancellationToken))));
        }

        private static string Result(CommandResult result) =>
            (result.IsSuccess ? "ok" : $"refused: {result.Error}") + (result.Warning.Length > 0 ? $"; warns: {result.Warning}" : "");

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
            (string text, string? sidecar, Dictionary<string, string> registrations) = await ReadAsync();
            json.WriteStartObject();
            json.WriteString("do", step);
            json.WriteString("answer", answer);
            json.WriteChange(_text, text);
            if (!string.Equals(_sidecar, sidecar, StringComparison.Ordinal))
            {
                json.WriteStartObject("sidecar");
                if (sidecar is null)
                {
                    json.WriteBoolean("deleted", true);
                }
                else
                {
                    json.WriteChange(_sidecar ?? "", sidecar);
                }

                json.WriteEndObject();
            }

            foreach ((string name, string content) in registrations.Where(pair => !_registrations.TryGetValue(pair.Key, out var was) || was != pair.Value))
            {
                json.WriteString($"writes {name}", content);
            }

            foreach (var name in _registrations.Keys.Where(name => !registrations.ContainsKey(name)))
            {
                json.WriteString($"deletes {name}", "");
            }

            json.WriteEndObject();
            (_text, _sidecar, _registrations) = (text, sidecar, registrations);
        }

        /// <summary>The body, the sidecar with its line endings as LF (or null when there is none), and the registrations beside the body.</summary>
        private async Task<(string Text, string? Sidecar, Dictionary<string, string> Registrations)> ReadAsync()
        {
            var text = TranscriptText.Decode(await File.ReadAllBytesAsync(session.Body, cancellationToken));
            var path = C4LayoutSidecar.PathFor(session.Body);
            var sidecar = File.Exists(path)
                ? TranscriptText.Decode(await File.ReadAllBytesAsync(path, cancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal)
                : null;
            var registrations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var registration in Directory.GetFiles(session.Folder, "*.adp").Order(StringComparer.Ordinal))
            {
                registrations[Path.GetFileName(registration)] = TranscriptText.Decode(await File.ReadAllBytesAsync(registration, cancellationToken));
            }

            return (text, sidecar, registrations);
        }

        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

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
