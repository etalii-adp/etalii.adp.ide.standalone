using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sankey.Tests.Parity;

/// <summary>
/// The Sankey diagram's parity transcript: what today's hand-written code answers for every document
/// of the corpus, frozen as <c>Parity/sankey.transcript.json</c> before any provider, the parser or the
/// writer is changed (sankey-disl-fbl, Requirement 1). Every later step must reproduce it byte for
/// byte, except where the specification names a change.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>: the test project's <c>Fixtures/*.skv</c> (the six it had and one or more for
/// each finding marked <i>read</i>), the module's <c>examples/*/*.skv</c> and the shipped copies under
/// <c>src/examples/diagrams/sankey/</c>. A document whose bytes equal one recorded earlier is written
/// as <c>sameAs</c> that one.
/// </para>
/// <para>
/// <b>Once</b>: the toolbox, and the three refusals that depend on how a session was opened or what a
/// gesture names rather than on the document: a drag without a history, a drag of a flow and a
/// re-parenting.
/// </para>
/// <para>
/// <b>Per document</b>: the context menu of every node and flow id, the empty canvas, a placement, a
/// connect gesture and an unknown id; the property rows of every node and flow; the findings; the
/// payload of every mapped element; and a scripted edit sequence that runs every command once, each
/// step's answer and the hunks it made to the document's text, then undoes all of it.
/// </para>
/// <para>
/// <b>Nothing is masked.</b> A node added through the menu would take a fresh <see cref="ShortGuid"/>,
/// so the script adds a node by dispatching the very command "Add node here" dispatches, with the id
/// given: a fixed id source, which the command record already offers (its handler mints only when the
/// id is empty). The menu's own add is run where it refuses, which mints nothing.
/// </para>
/// </remarks>
public sealed class SankeyTranscript() : TranscriptAdapter("sankey", "EtAlii.Adp.Diagram.Sankey.Tests", "sankey.transcript.json")
{
    private const string Unknown = "parity:unknown";

    /// <summary>The ids the script gives the nodes it adds, in order.</summary>
    private const string AddedPrefix = "parity-node-";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(SankeyReflection.Descriptor);

    public override string Origin => Diagram.Sankey.Origin.Key;

    protected override IReadOnlyList<ToolboxItemDefinition> Toolbox => new SankeyToolboxProvider().Items;

    /// <summary>The corpus, in the order it is recorded.</summary>
    public override IReadOnlyList<TranscriptDocument> Corpus() => [.. Files().Select(OnDisk)];

    /// <summary>The corpus, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    private IReadOnlyList<string> Files()
    {
        var fixtures = Directory.GetFiles(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures"), "*.skv");
        var examples = Directory.GetDirectories(Path.Combine(ModuleFolder, "examples")).SelectMany(folder => Directory.GetFiles(folder, "*.skv"));
        var shipped = Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "sankey")).SelectMany(folder => Directory.GetFiles(folder, "*.skv"));
        return [.. Relative(fixtures), .. Relative(examples), .. Relative(shipped)];
    }

    /// <inheritdoc />
    public override async Task WriteBeforeDocumentsAsync(Utf8JsonWriter json, CancellationToken cancellationToken)
    {
        await base.WriteBeforeDocumentsAsync(json, cancellationToken);

        using var folder = TestFolder.Create(nameof(SankeyTranscript));
        var body = Path.Combine(folder.Path, "small.skv");
        File.Copy(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures", "lf-line-endings.skv"), body);
        using var session = new Session(folder.Path, body);
        await session.ReadAsync(cancellationToken);

        json.WriteStartArray("sessionRefusals");
        var mapper = new SankeyElementMapper();
        await using (var readOnly = new SankeySession(body, session.Store, mapper, history: null))
        {
            json.WriteStringValue($"drag a without a history => {Answer(await readOnly.MoveElementToAsync("a", 0, 0, cancellationToken))}");
        }

        await using (var editable = session.Diagram(mapper))
        {
            json.WriteStringValue($"drag a->m => {Answer(await editable.MoveElementToAsync("a->m", 0, 0, cancellationToken))}");
            json.WriteStringValue($"reparent a under m => {Answer(await editable.MoveElementAsync("a", "m", 0, cancellationToken))}");
        }

        json.WriteEndArray();
    }

    /// <inheritdoc />
    public override async Task WriteDocumentAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, string folder, CancellationToken cancellationToken)
    {
        var body = Path.Combine(folder, Path.GetFileName(document.Name));
        await File.WriteAllBytesAsync(body, bytes, cancellationToken);
        using var session = new Session(folder, body);
        await session.ReadAsync(cancellationToken);
        await WriteReadingsAsync(json, session, bytes, cancellationToken);
        await WriteEditsAsync(json, session, cancellationToken);
    }

    private static async Task WriteReadingsAsync(Utf8JsonWriter json, Session session, byte[] bytes, CancellationToken cancellationToken)
    {
        var model = session.Model;
        var nodeIds = model.Nodes.Select(node => node.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        List<string> ids = [.. nodeIds.Concat(model.Flows.Select(flow => flow.Id)).Distinct(StringComparer.Ordinal)];
        var relation = nodeIds.Count >= 2 ? GestureIds.Relation(nodeIds[0], nodeIds[1]) : GestureIds.Relation("a", "b");
        List<string> targets = [.. ids, "", GestureIds.Placement(330, 40), relation, Unknown];

        var menus = new List<IReadOnlyList<string>>();
        var choices = new List<IReadOnlyList<string>>();
        var lines = new List<(string Target, int Menu, IReadOnlyList<string> Rows)>();
        foreach (var target in targets)
        {
            var menu = menus.IndexOfOrAdd((await session.Actions.DiscoverAsync(session.Target(target), cancellationToken)).MenuLines());
            IReadOnlyList<string> rows = [.. (await session.Properties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => row.RowLine(choices))];
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

        var problems = await new SankeyValidator().ValidateAsync(
            new DiagramValidationRequest(TranscriptText.Decode(bytes), Path.GetFileNameWithoutExtension(session.Body), ProjectRoot.From(session.Folder), session.Body, null),
            cancellationToken);
        json.WritePropertyName("findings");
        json.WriteLines([.. problems.Select(problem => $"{Location(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}")]);

        json.WritePropertyName("payloads");
        json.WriteLines([.. new SankeyElementMapper().Visible(model, DiagramViewport.Unbounded)
            .Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]);
    }

    private static string Location(DiagramProblemLocation? location) => location switch
    {
        DiagramProblemLineLocation line => $"line {line.Number}",
        DiagramProblemElementLocation element => $"element {TranscriptText.Quoted(element.Id)}",
        null => "nowhere",
        _ => location.ToString(),
    };

    private static string Answer(string refusal) => refusal.Length == 0 ? "accepted" : $"refused: {refusal}";

    /// <summary>
    /// The scripted edit sequence: drags up and down a column, every property row of a node and of a
    /// flow with values that reach their refusals, the menus' actions and their dialogs' commits, the
    /// adds, the connect gestures, the removals and Arrange diagram, each run through the real history
    /// on a copy of the document, then everything undone.
    /// </summary>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var script = new Script(json, session, cancellationToken);
        var original = await script.StartAsync();

        var model = session.Model;
        var nodeIds = model.Nodes.Select(node => node.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var first = nodeIds.FirstOrDefault();
        var second = nodeIds.Skip(1).FirstOrDefault();
        var flow = model.Flows.Select(candidate => candidate.Id).FirstOrDefault();

        if (first is not null)
        {
            await script.DragAsync(first, toEnd: true);
            await script.DragAsync(first, toEnd: false);

            await script.SetAsync(first, SankeyContextPropertyProvider.NameProperty, "Parity name");
            await script.SetAsync(first, SankeyContextPropertyProvider.NameProperty, " ");
            await script.SetAsync(first, SankeyContextPropertyProvider.NameProperty, "Line one\nline two");
            await script.SetAsync(first, SankeyContextPropertyProvider.DescriptionProperty, "Parity: a description");
            await script.SetAsync(first, SankeyContextPropertyProvider.DescriptionProperty, "");
            await script.SetAsync(first, SankeyContextPropertyProvider.ColorProperty, "teal");
            await script.SetAsync(first, SankeyContextPropertyProvider.ColorProperty, SankeyContextPropertyProvider.CustomColor);
            await script.SetAsync(first, SankeyContextPropertyProvider.CustomColorProperty, "#C0FFEE");
            await script.SetAsync(first, SankeyContextPropertyProvider.ColorProperty, "chartreuse");
            await script.SetAsync(first, SankeyContextPropertyProvider.ColorProperty, SankeyContextPropertyProvider.DefaultColor);
            await script.SetAsync(first, SankeyContextPropertyProvider.ColumnProperty, "3");
            await script.SetAsync(first, SankeyContextPropertyProvider.ColumnProperty, "zero");
            await script.SetAsync(first, SankeyContextPropertyProvider.ColumnProperty, SankeyContextPropertyProvider.AutoColumn);
            await script.SetAsync(first, SankeyContextPropertyProvider.NoteProperty, "+3% Y/Y");
            await script.SetAsync(first, SankeyContextPropertyProvider.FormatProperty, "{value} kg");
            await script.SetAsync(first, SankeyContextPropertyProvider.FormatProperty, "");
            await script.SetAsync(first, SankeyContextPropertyProvider.ValueProperty, "3");
            await script.SetAsync(first, SankeyContextPropertyProvider.StepProperty, "1");
            await script.SetAsync(first, "parity:property", "x");

            await script.ExecuteAsync(first, SankeyContextActionProvider.RenameActionId);
            await script.ValidateAsync(first, SankeyContextActionProvider.RenameActionId, "  ");
            await script.ValidateAsync(first, SankeyContextActionProvider.RenameActionId, "Renamed");
            await script.CommitAsync(first, SankeyContextActionProvider.RenameActionId, "Parity renamed");
            await script.ExecuteAsync(first, SankeyContextActionProvider.IncreaseActionId);
            await script.ExecuteAsync(first, SankeyContextActionProvider.ConnectActionId);
        }

        if (flow is not null)
        {
            await script.ExecuteAsync(flow, SankeyContextActionProvider.IncreaseActionId);
            await script.SetAsync(flow, SankeyContextPropertyProvider.ValueProperty, "0.00025");
            await script.SetAsync(flow, SankeyContextPropertyProvider.ValueProperty, "1.00005");
            await script.SetAsync(flow, SankeyContextPropertyProvider.ValueProperty, "1e21");
            await script.SetAsync(flow, SankeyContextPropertyProvider.ValueProperty, "12.5");
            await script.SetAsync(flow, SankeyContextPropertyProvider.ValueProperty, "-1");
            await script.SetAsync(flow, SankeyContextPropertyProvider.ValueProperty, "lots");
            await script.SetAsync(flow, SankeyContextPropertyProvider.StepProperty, "0");
            await script.SetAsync(flow, SankeyContextPropertyProvider.StepProperty, "0.25");
            await script.ExecuteAsync(flow, SankeyContextActionProvider.IncreaseActionId);
            await script.ExecuteAsync(flow, SankeyContextActionProvider.DecreaseActionId);
            await script.SetAsync(flow, SankeyContextPropertyProvider.StepProperty, "");
            await script.SetAsync(flow, SankeyContextPropertyProvider.ValueProperty, "0.05");
            await script.ExecuteAsync(flow, SankeyContextActionProvider.DecreaseActionId);
            await script.ExecuteAsync(flow, SankeyContextActionProvider.DecreaseActionId);
            await script.SetAsync(flow, SankeyContextPropertyProvider.ValueProperty, "");
            await script.ExecuteAsync(flow, SankeyContextActionProvider.IncreaseActionId);
            await script.SetAsync(flow, SankeyContextPropertyProvider.DescriptionProperty, "Parity flow");
            await script.SetAsync(flow, SankeyContextPropertyProvider.ColorProperty, "orange");
            await script.SetAsync(flow, SankeyContextPropertyProvider.CustomColorProperty, "#123");
            await script.SetAsync(flow, SankeyContextPropertyProvider.NameProperty, "Not for a flow");
            await script.SetAsync(flow, SankeyContextPropertyProvider.ColumnProperty, "2");
            await script.ExecuteAsync(flow, SankeyContextActionProvider.RenameActionId);
        }

        await script.ExecuteAsync("", SankeyContextActionProvider.ThickerActionId);
        await script.ExecuteAsync("", SankeyContextActionProvider.ThinnerActionId);
        await script.ExecuteAsync("", SankeyContextActionProvider.ThinnerActionId);

        await script.ExecuteAsync("", SankeyContextActionProvider.AddActionId);
        var added = await script.AddAsync(330, 40);
        await script.AddAsync(10, 100_000);

        if (first is not null)
        {
            if (second is not null)
            {
                await script.ExecuteAsync(GestureIds.Relation(first, second), SankeyContextActionProvider.ConnectActionId);
            }

            await script.ExecuteAsync(GestureIds.Relation(first, added), SankeyContextActionProvider.ConnectActionId);
            await script.ExecuteAsync(GestureIds.Relation(first, added), SankeyContextActionProvider.ConnectActionId);
            await script.ExecuteAsync(GestureIds.Relation(first, first), SankeyContextActionProvider.ConnectActionId);
            await script.ExecuteAsync(GestureIds.Relation(first, Unknown), SankeyContextActionProvider.ConnectActionId);
        }

        await script.ExecuteAsync(Unknown, SankeyContextActionProvider.RenameActionId);
        await script.ExecuteAsync(Unknown, SankeyContextActionProvider.RemoveActionId);
        await script.ExecuteAsync(Unknown, SankeyContextActionProvider.IncreaseActionId);
        await script.SetAsync(Unknown, SankeyContextPropertyProvider.NameProperty, "Nobody");
        await script.CommitAsync(Unknown, SankeyContextActionProvider.RenameActionId, "Nobody");

        if (flow is not null)
        {
            await script.ExecuteAsync(flow, SankeyContextActionProvider.RemoveActionId);
        }

        foreach (var removed in new[] { second, first }.OfType<string>())
        {
            if (await script.ExecuteAsync(removed, SankeyContextActionProvider.RemoveActionId) is "confirm")
            {
                await script.CommitAsync(removed, SankeyContextActionProvider.RemoveActionId, "");
            }
        }

        await script.ExecuteAsync("", SankeyContextActionProvider.ArrangeActionId);
        await script.ExecuteAsync("", SankeyContextActionProvider.ArrangeActionId);

        await script.EndAsync(original);
    }

    /// <summary>One document's store, history and providers.</summary>
    private sealed class Session : IDisposable
    {
        public Session(string folder, string body)
        {
            Folder = folder;
            Body = body;
            History = TestHistory.For(s => { s.AddSankey(); s.AddSingleton<ISankeyDocumentStore>(Store); });
            Actions = new SankeyContextActionProvider(History, Store);
            Properties = new SankeyContextPropertyProvider(History, Store);
        }

        public string Folder { get; }

        public string Body { get; }

        public SankeyDocumentStore Store { get; } = new(new FileWriter());

        private IHistoryStackStore History { get; }

        public SankeyContextActionProvider Actions { get; }

        public SankeyContextPropertyProvider Properties { get; }

        /// <summary>Opens the document, as the diagram's first open does; the readings and edits read what it holds.</summary>
        public async Task ReadAsync(CancellationToken cancellationToken) => await Store.GetOrLoadAsync(Body, cancellationToken);

        public SankeyModel Model => Store.Get(Body)!.Model;

        public IHistoryStack Stack => History.Get(ProjectRoot.From(Folder));

        public SankeySession Diagram(SankeyElementMapper mapper) => new(Body, Store, mapper, Stack);

        public ContextTarget Target(string elementId) =>
            ContextTarget.ForElement(Body, elementId, ProjectRoot.From(Folder), origin: Sankey.Diagram.Sankey.Origin);

        public void Dispose() => (History as IDisposable)?.Dispose();
    }

    /// <summary>Runs the steps of one document's edit sequence and writes each as it goes.</summary>
    private sealed class Script(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        private readonly SankeyElementMapper _mapper = new();
        private string _text = "";
        private int _added;

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
                while (session.Stack.CanUndo && (await session.Stack.UndoAsync(cancellationToken)).IsSuccess)
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

        /// <summary>
        /// Drags a node by its bar to below the last node of its column, or above the first, as the
        /// canvas reports a drop: the bar's new top-left.
        /// </summary>
        public async Task DragAsync(string id, bool toEnd)
        {
            var layout = SankeyLayout.Of(session.Model);
            string answer;
            if (!layout.Boxes.TryGetValue(id, out var box))
            {
                answer = "not asked: the node is not drawn";
            }
            else
            {
                var column = layout.ColumnOrder[layout.Columns[id]];
                var y = toEnd ? column.Max(other => layout.Boxes[other].Y + layout.Boxes[other].Height) + 1 : column.Min(other => layout.Boxes[other].Y) - box.Height - 1;
                try
                {
                    await using var diagram = session.Diagram(_mapper);
                    answer = Answer(await diagram.MoveElementToAsync(id, box.X, y, cancellationToken));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    answer = Thrown(exception);
                }
            }

            await RecordAsync($"drag {id} to the {(toEnd ? "end" : "top")} of its column", answer);
        }

        public async Task SetAsync(string target, string propertyId, string value)
        {
            string answer;
            try
            {
                var result = await session.Properties.SetAsync(session.Target(target), propertyId, value, cancellationToken);
                answer = result.IsSuccess ? "ok" : $"refused: {result.Error}";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"set {target} {propertyId} = {TranscriptText.Quoted(value)}", answer);
        }

        public async Task ValidateAsync(string target, string actionId, string value)
        {
            string answer;
            try
            {
                var result = await session.Actions.ValidateAsync(session.Target(target), actionId, value, cancellationToken);
                answer = result.Valid ? "accepted" : $"rejected: {result.Reason}";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"validate {actionId} on {target} = {TranscriptText.Quoted(value)}", answer);
        }

        /// <summary>Runs an action; answers "confirm" when it asked for a confirmation, else null.</summary>
        public async Task<string?> ExecuteAsync(string target, string actionId)
        {
            ContextExecutionResult result;
            try
            {
                result = await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RecordAsync($"execute {actionId} on {target}", Thrown(exception));
                return null;
            }

            await RecordAsync($"execute {actionId} on {target}", Describe(result));
            return result is ContextExecutionRequiresConfirmation ? "confirm" : null;
        }

        /// <summary>
        /// "Add node here" at a placement, as the action provider dispatches it, with the id the
        /// command record lets a caller give; answers that id.
        /// </summary>
        public async Task<string> AddAsync(double x, double y)
        {
            var id = $"{AddedPrefix}{++_added}";
            string answer;
            try
            {
                var result = await session.Stack.ExecuteAsync(new AddSankeyNodeCommand(session.Body, x, y, id), cancellationToken);
                answer = result.IsSuccess ? "ok" : $"refused: {result.Error}";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"add node {id} at {GestureIds.Placement(x, y)}", answer);
            return id;
        }

        public async Task CommitAsync(string target, string actionId, string value)
        {
            string answer;
            try
            {
                var result = await session.Actions.CommitAsync(session.Target(target), actionId, value, "", cancellationToken);
                answer = result.Completed ? "ok" : $"refused: {result.Error}";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"commit {actionId} on {target} = {TranscriptText.Quoted(value)}", answer);
        }

        /// <summary>
        /// A step today's code throws on, recorded rather than ending the transcript: what it throws is
        /// the answer a user would meet, and the steps after it still run.
        /// </summary>
        private static string Thrown(Exception exception) => $"throws {exception.GetType().Name}: {exception.Message}";

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
