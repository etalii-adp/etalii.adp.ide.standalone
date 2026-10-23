using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests.Parity;

/// <summary>
/// The agent behavior model's parity transcript: what today's hand-written code answers for every
/// document of the corpus, frozen as <c>Parity/abm.transcript.json</c> before any of it is derived
/// from the bundled DISL definition. Every later switch-over must reproduce it byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>: the module's four examples, their shipped copies under
/// <c>src/examples/diagrams/agent-behavior-modelling/</c> (written as <c>sameAs</c> while identical),
/// and the parser and writer tests' inline documents (<see cref="AbmCorpus"/>).
/// </para>
/// <para>
/// <b>Per document</b>: the context menu of every node and connection id, the empty canvas, a
/// placement, a relation gesture and an unknown id, each for a readable and for a read-only document;
/// the property rows of every one of those targets; the findings; the payload of every mapped element;
/// and a scripted edit sequence, each step's answer and the hunks it made to the Markdown.
/// </para>
/// <para>
/// <b>Read-only is the store's unreadable entry</b>, the one read-only case the backend has. Today's
/// providers do not consult it - the menus and rows come out the same - and the transcript records
/// exactly that, so a derived runtime that starts honouring it shows as a change.
/// </para>
/// <para>
/// <b>Nothing random is written</b>: a node's id is its place in the tree, so an added node's id is
/// known before it exists and nothing needs masking.
/// </para>
/// </remarks>
public sealed class AbmTranscript() : TranscriptAdapter("agent-behavior-modelling", "EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests", "abm.transcript.json")
{
    private const string Unknown = "parity:unknown";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(AgentBehaviorModellingReflection.Descriptor);

    public override string Origin => Diagram.AgentBehaviorModelling.Origin.Key;

    public override IReadOnlyList<ToolboxItemDefinition> Toolbox => AbmDefinition.Definition.Toolbox;

    /// <summary>The corpus, in the order it is recorded: the documents on disk, then the inline ones.</summary>
    public override IReadOnlyList<TranscriptDocument> Corpus() =>
    [
        .. Files().Select(OnDisk),
        .. AbmCorpus.Inline.Select(inline => new TranscriptDocument(inline.Name, _ => Task.FromResult(TranscriptText.Encode(inline.Text)))),
    ];

    /// <summary>The documents on disk, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    private IReadOnlyList<string> Files()
    {
        var examples = AbmExamples.Names.Select(AbmExamples.BodyOf);
        var shipped = Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "agent-behavior-modelling")).SelectMany(folder => Directory.GetFiles(folder, "*.md"));
        return [.. Relative(examples), .. Relative(shipped)];
    }

    /// <remarks>
    /// The body is written under its own file name, <c>.md</c> added to an inline document's; a
    /// document on disk with a registration beside it gets a copy of that registration beside it here.
    /// </remarks>
    public override async Task WriteDocumentAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, string folder, CancellationToken cancellationToken)
    {
        var file = document.Name[(document.Name.LastIndexOf('/') + 1)..];
        var body = Path.Combine(folder, file.EndsWith(".md", StringComparison.Ordinal) ? file : file + ".md");
        await File.WriteAllBytesAsync(body, bytes, cancellationToken);

        var source = Path.Combine(SourceFolder, document.Name);
        var registration = Path.ChangeExtension(source, DiagramFileName.Extension);
        if (File.Exists(source) && File.Exists(registration))
        {
            await File.WriteAllBytesAsync(Path.ChangeExtension(body, DiagramFileName.Extension), await File.ReadAllBytesAsync(registration, cancellationToken), cancellationToken);
        }

        using var session = new Session(folder, body);
        await session.ReadAsync(cancellationToken);
        await WriteReadingsAsync(json, session, bytes, cancellationToken);
        await WriteEditsAsync(json, session, cancellationToken);
    }

    private static async Task WriteReadingsAsync(Utf8JsonWriter json, Session session, byte[] bytes, CancellationToken cancellationToken)
    {
        var model = session.Model;
        List<string> elements =
        [
            .. model.Nodes.Select(node => node.Id),
            .. model.Nodes.Where(node => node.ParentId is not null).Select(node => AbmElementMapper.ConnectionIdOf(node.Id)),
        ];
        var relation = model.Nodes.Count >= 2 ? GestureIds.Relation(model.Nodes[0].Id, model.Nodes[1].Id) : GestureIds.Relation("1", "2");
        List<string> targets = [.. elements, "", GestureIds.Placement(240, 160), relation, Unknown];

        var menus = new List<IReadOnlyList<string>>();
        var choices = new List<IReadOnlyList<string>>();
        var lines = new List<(string Target, int Menu, int MenuReadOnly, IReadOnlyList<string> Rows, IReadOnlyList<string> RowsReadOnly)>();
        foreach (var target in targets)
        {
            var menu = menus.IndexOfOrAdd((await session.Actions.DiscoverAsync(session.Target(target), cancellationToken)).MenuLines());
            var menuReadOnly = menus.IndexOfOrAdd((await session.ReadOnlyActions.DiscoverAsync(session.Target(target), cancellationToken)).MenuLines());
            IReadOnlyList<string> rows = [.. (await session.Properties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => row.RowLine(choices))];
            IReadOnlyList<string> rowsReadOnly = [.. (await session.ReadOnlyProperties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => row.RowLine(choices))];
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

        var problems = await new AbmValidator().ValidateAsync(
            new DiagramValidationRequest(TranscriptText.Decode(bytes), Path.GetFileNameWithoutExtension(session.Body), ProjectRoot.From(session.Folder), session.Body, null),
            cancellationToken);
        json.WritePropertyName("findings");
        json.WriteLines([.. problems.Select(problem => $"line {(problem.Location as DiagramProblemLineLocation)?.Number} | {problem.Severity} | {problem.RuleId} | {problem.Message}")]);

        json.WritePropertyName("payloads");
        json.WriteLines([.. new AbmElementMapper().Visible(model, RegistrationLayout.Read(session.Registration), DiagramViewport.Unbounded)
            .Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]);
    }

    /// <summary>
    /// The scripted edit sequence: the property rows, the menus' actions and the parent-line gesture,
    /// each run through the real history on a copy of the document, in an order that reaches refusals too.
    /// </summary>
    /// <remarks>
    /// A node's id is its place, so an id taken from the model before a step can name a different
    /// node after it. Each step therefore picks its node from the model as it is at that step.
    /// </remarks>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var script = new Script(json, session, cancellationToken);
        var original = await script.StartAsync();

        if (Root() is { } root)
        {
            await script.SetAsync(root.Id, AbmContextPropertyProvider.LabelProperty, "Parity root");
            await script.SetAsync(root.Id, AbmContextPropertyProvider.PlaceProperty, "9");
        }

        if (Leaf() is { } leaf)
        {
            await script.SetAsync(leaf.Id, AbmContextPropertyProvider.NotesProperty, "First line.\n\nSecond line.");
            await script.SetAsync(leaf.Id, AbmContextPropertyProvider.KindProperty, "Nonsense");
            await script.SetAsync(leaf.Id, AbmContextPropertyProvider.KindProperty, "Ask the user");
        }

        if (Branching() is { } branching)
        {
            await script.SetAsync(branching.Id, AbmContextPropertyProvider.KindProperty, "Do");
        }

        if (session.Model.Nodes.FirstOrDefault(node => node.Kind == AbmNodeKinds.Retry) is { } retry)
        {
            await script.SetAsync(retry.Id, AbmContextPropertyProvider.AttemptsProperty, "0");
            await script.SetAsync(retry.Id, AbmContextPropertyProvider.AttemptsProperty, "3");
        }

        if (Root() is { } parent)
        {
            if (await script.ExecuteAsync(parent.Id, AbmContextActionProvider.AddActionId(AbmNodeKinds.Check)) is { } added)
            {
                await script.CommitAsync(added, AbmContextActionProvider.RenameActionId, "Parity check");
            }

            await script.ExecuteAsync(parent.Id, AbmContextActionProvider.AddActionId("nonsense"));
        }

        await script.ExecuteAsync(GestureIds.Placement(0, -1000), AbmContextActionProvider.AddActionId(AbmNodeKinds.Action));
        await script.ExecuteAsync(GestureIds.Placement(400, 2000), AbmContextActionProvider.AddActionId(AbmNodeKinds.Delegate));

        if (Root() is { ChildIds.Count: >= 2 } siblings)
        {
            await script.ExecuteAsync(siblings.ChildIds[0], AbmContextActionProvider.MoveLaterActionId);
            await script.ExecuteAsync(siblings.ChildIds[1], AbmContextActionProvider.MoveEarlierActionId);
            await script.ExecuteAsync(siblings.ChildIds[0], AbmContextActionProvider.MoveEarlierActionId);
        }

        if (Root() is { } top && session.Model.Nodes[^1] is var last && last.Id != top.Id)
        {
            await script.ExecuteAsync(GestureIds.Relation(last.Id, top.Id), AbmContextActionProvider.ConnectChildActionId);
            await script.ExecuteAsync(GestureIds.Relation(top.Id, session.Model.Nodes[^1].Id), AbmContextActionProvider.ConnectChildActionId);
        }

        if (Leaf() is { } noted)
        {
            await script.ExecuteAsync(noted.Id, AbmContextActionProvider.EditNotesActionId);
            await script.CommitAsync(noted.Id, AbmContextActionProvider.EditNotesActionId, "");
        }

        if (Branching() is { } removed && await script.ExecuteAsync(removed.Id, AbmContextActionProvider.RemoveActionId) is "confirm")
        {
            await script.CommitAsync(removed.Id, AbmContextActionProvider.RemoveActionId, "");
        }

        await script.ExecuteAsync(Unknown, AbmContextActionProvider.RenameActionId);
        await script.CommitAsync(Unknown, AbmContextActionProvider.RenameActionId, "Nobody");
        await script.ExecuteAsync("", AbmContextActionProvider.ArrangeActionId);
        await script.EndAsync(original);
        return;

        AbmNode? Branching() => session.Model.Nodes.FirstOrDefault(node => node.ChildIds.Count >= 2);
        AbmNode? Leaf() => session.Model.Nodes.FirstOrDefault(node => node.Category == AbmNodeCategory.Leaf);
        AbmNode? Root() => session.Model.Roots.FirstOrDefault();
    }

    /// <summary>One document's store, history and providers, readable and read-only.</summary>
    private sealed class Session : IDisposable
    {
        public Session(string folder, string body)
        {
            Folder = folder;
            Body = body;
            History = TestHistory.For(s => { s.AddAgentBehaviorModelling(); s.AddSingleton<IAbmDocumentStore>(Store); });
            var readOnly = new ReadOnlyStore(Store);
            Actions = new AbmContextActionProvider(History, Store);
            ReadOnlyActions = new AbmContextActionProvider(History, readOnly);
            Properties = new AbmContextPropertyProvider(History, Store);
            ReadOnlyProperties = new AbmContextPropertyProvider(History, readOnly);
        }

        public string Folder { get; }

        public string Body { get; }

        public string Registration => Path.ChangeExtension(Body, DiagramFileName.Extension);

        private AbmDocumentStore Store { get; } = new(new FileWriter());

        public IHistoryStackStore History { get; }

        public AbmContextActionProvider Actions { get; }

        public AbmContextActionProvider ReadOnlyActions { get; }

        public AbmContextPropertyProvider Properties { get; }

        public AbmContextPropertyProvider ReadOnlyProperties { get; }

        /// <summary>Opens the document, as the diagram's first open does; the readings and edits read what it holds.</summary>
        public async Task ReadAsync(CancellationToken cancellationToken) => await Store.GetOrLoadAsync(Body, cancellationToken);

        public AbmModel Model => Store.Get(Body)!.Model;

        public ContextTarget Target(string elementId) =>
            ContextTarget.ForElement(Body, elementId, ProjectRoot.From(Folder), origin: Diagram.AgentBehaviorModelling.Origin);

        public void Dispose() => (History as IDisposable)?.Dispose();
    }

    /// <summary>The store's own entries, each marked as unreadable: the backend's one read-only case.</summary>
    private sealed class ReadOnlyStore(IAbmDocumentStore inner) : IAbmDocumentStore
    {
        public event EventHandler<DocumentChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public async ValueTask<DocumentEntry<AbmBody, AbmModel>> GetOrLoadAsync(string path, CancellationToken cancellationToken) => await inner.GetOrLoadAsync(path, cancellationToken) with { Unreadable = "parity: read-only" };

        public DocumentEntry<AbmBody, AbmModel>? Get(string path) => inner.Get(path) is { } held ? held with { Unreadable = "parity: read-only" } : null;

        public Task<DocumentSaveResult> SaveAsync(string path, AbmBody document, CancellationToken cancellationToken) => inner.SaveAsync(path, document, cancellationToken);

        public void Forget(string path) => inner.Forget(path);

        public Task BodyDeletedAsync(string path, CancellationToken cancellationToken) => inner.BodyDeletedAsync(path, cancellationToken);

        public Task ReloadAsync(string path, CancellationToken cancellationToken) => inner.ReloadAsync(path, cancellationToken);
    }

    /// <summary>Runs the steps of one document's edit sequence and writes each as it goes.</summary>
    private sealed class Script(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        private string _text = "";

        public async Task<string> StartAsync()
        {
            _text = await ReadAsync();
            json.WriteStartArray("edits");
            return _text;
        }

        public async Task EndAsync(string original)
        {
            json.WriteEndArray();

            var stack = session.History.Get(ProjectRoot.From(session.Folder));
            var undone = 0;
            while (stack.CanUndo && (await stack.UndoAsync(cancellationToken)).IsSuccess)
            {
                undone++;
            }

            var restored = await ReadAsync();
            json.WriteString("undoAll", $"{undone} undone; {(string.Equals(restored, original, StringComparison.Ordinal) ? "the original text is back" : "the original text is NOT back")}");
        }

        public async Task SetAsync(string target, string propertyId, string value)
        {
            var result = await session.Properties.SetAsync(session.Target(target), propertyId, value, cancellationToken);
            await RecordAsync($"set {target} {propertyId} = {TranscriptText.Quoted(value)}", result.IsSuccess ? "ok" : $"refused: {result.Error}");
        }

        /// <summary>Runs an action; answers "confirm" when it asked for a confirmation, the new node's id when an add asks for its name, else null.</summary>
        public async Task<string?> ExecuteAsync(string target, string actionId)
        {
            var result = await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken);
            await RecordAsync($"execute {actionId} on {target}", Describe(result));
            return result switch
            {
                ContextExecutionRequiresConfirmation => "confirm",
                ContextExecutionRequiresInput { Request.CommitActionId.Length: > 0 } input => input.Request.InlineLabelElementId,
                _ => null,
            };
        }

        public async Task CommitAsync(string target, string actionId, string value)
        {
            var result = await session.Actions.CommitAsync(session.Target(target), actionId, value, "", cancellationToken);
            await RecordAsync(
                $"commit {actionId} on {target} = {TranscriptText.Quoted(value)}",
                result.Completed ? "ok" : $"refused: {result.Error}");
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
