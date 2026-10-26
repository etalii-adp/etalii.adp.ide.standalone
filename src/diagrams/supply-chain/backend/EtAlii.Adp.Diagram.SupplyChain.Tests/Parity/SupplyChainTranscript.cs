using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>
/// The supply chain diagram's parity transcript: what today's hand-written code answers for every
/// document of the corpus, frozen as <c>Parity/supply-chain.transcript.json</c> before any provider,
/// the parser or the writer is changed (supply-chain-disl-fbl, Requirement 1). Every later step must
/// reproduce it byte for byte, except where the specification names a change.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>: the test project's <c>Fixtures/*.supply</c> (the six it had and one for each
/// finding marked <i>read</i> that concerns a file, each fixture's first comment naming its finding),
/// the module's <c>examples/*/*.supply</c> and the shipped copies under
/// <c>src/examples/diagrams/supply-chain/</c>. A document whose bytes equal one recorded earlier is
/// written as <c>sameAs</c> that one.
/// </para>
/// <para>
/// <b>Once</b>: the toolbox, and the three refusals that depend on how a session was opened or what a
/// gesture names rather than on the document: a drag without a history, a drag of a flow and a
/// re-parenting.
/// </para>
/// <para>
/// <b>Per document</b>: the context menu of every group, node and flow id, the empty canvas, a
/// placement, a connect gesture and an unknown id; the property rows of every one of them; the
/// findings; the payload of every mapped element; the traces, which are the payloads' marks with the
/// first drawn node, flow and group selected in turn; and a scripted edit sequence that runs every
/// command once, each step's answer and the hunks it made to the document's text, then undoes all of
/// it.
/// </para>
/// <para>
/// <b>Only minted ids are masked.</b> An entry added by a gesture or a dialog takes a fresh
/// <see cref="ShortGuid"/>, so it is written as <c>{minted-N}</c> in order of minting; nothing else
/// the transcript records depends on the clock or on chance. Every refusal that needs an id of the
/// script's choosing dispatches the command with that id given, which its record already allows.
/// </para>
/// </remarks>
public sealed partial class SupplyChainTranscript() : TranscriptAdapter("supply-chain", "EtAlii.Adp.Diagram.SupplyChain.Tests", "supply-chain.transcript.json")
{
    private const string Unknown = "parity:unknown";

    /// <summary>Where the script's placements land: halves, so that every write shows how it rounds.</summary>
    private const double PlacementX = 400.5;

    private const double PlacementY = 300.5;

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(SupplyChainReflection.Descriptor);

    public override string Origin => Diagram.SupplyChain.Origin.Key;

    protected override IReadOnlyList<ToolboxItemDefinition> Toolbox => SupplyChainDefinition.Toolbox;

    /// <summary>The corpus, in the order it is recorded.</summary>
    public override IReadOnlyList<TranscriptDocument> Corpus() => [.. Files().Select(OnDisk)];

    /// <summary>The corpus, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    private IReadOnlyList<string> Files()
    {
        var fixtures = Directory.GetFiles(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures"), "*.supply");
        var examples = Directory.GetDirectories(Path.Combine(ModuleFolder, "examples")).SelectMany(folder => Directory.GetFiles(folder, "*.supply"));
        var shipped = Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "supply-chain")).SelectMany(folder => Directory.GetFiles(folder, "*.supply"));
        return [.. Relative(fixtures), .. Relative(examples), .. Relative(shipped)];
    }

    /// <summary>The placement every document's menus and adds are recorded at.</summary>
    private static string Placement => GestureIds.Placement(PlacementX, PlacementY);

    /// <summary>The targets the transcript records: every group, node and flow id, the empty id, a placement, a connect gesture and an unknown id.</summary>
    internal static IReadOnlyList<string> Targets(SupplyChainModel model)
    {
        var nodeIds = NodeIds(model);
        var relation = nodeIds.Count >= 2 ? GestureIds.Relation(nodeIds[0], nodeIds[1]) : GestureIds.Relation("a", "b");
        IEnumerable<string> ids = model.Groups.Select(group => group.Id)
            .Concat(model.Nodes.Select(node => node.Id))
            .Concat(model.Flows.Select(flow => flow.Id))
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal);
        return [.. ids, "", Placement, relation, Unknown];
    }

    /// <inheritdoc />
    public override async Task WriteBeforeDocumentsAsync(Utf8JsonWriter json, CancellationToken cancellationToken)
    {
        await base.WriteBeforeDocumentsAsync(json, cancellationToken);

        using var folder = TestFolder.Create(nameof(SupplyChainTranscript));
        var body = Path.Combine(folder.Path, "small.supply");
        File.Copy(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures", "lf-line-endings.supply"), body);
        using var session = new Session(folder.Path, body);
        await session.ReadAsync(cancellationToken);

        json.WriteStartArray("sessionRefusals");
        await using (var readOnly = session.Diagram(history: false))
        {
            json.WriteStringValue($"drag mine without a history => {Answer(await readOnly.MoveElementToAsync("mine", 0, 0, cancellationToken))}");
        }

        await using (var editable = session.Diagram(history: true))
        {
            json.WriteStringValue($"drag ore => {Answer(await editable.MoveElementToAsync("ore", 0, 0, cancellationToken))}");
            json.WriteStringValue($"reparent mine under north => {Answer(await editable.MoveElementAsync("mine", "north", 0, cancellationToken))}");
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
        WriteTraces(json, session.Model);
        await new Script(json, session, cancellationToken).RunAsync();
    }

    private static async Task WriteReadingsAsync(Utf8JsonWriter json, Session session, byte[] bytes, CancellationToken cancellationToken)
    {
        var model = session.Model;
        var menus = new List<IReadOnlyList<string>>();
        var choices = new List<IReadOnlyList<string>>();
        var lines = new List<(string Target, int Menu, IReadOnlyList<string> Rows)>();
        foreach (var target in Targets(model))
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

        var problems = await new SupplyChainValidator().ValidateAsync(
            new DiagramValidationRequest(TranscriptText.Decode(bytes), Path.GetFileNameWithoutExtension(session.Body), ProjectRoot.From(session.Folder), session.Body, null),
            cancellationToken);
        json.WritePropertyName("findings");
        json.WriteLines([.. problems.Select(problem => $"{Location(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}")]);

        json.WritePropertyName("payloads");
        json.WriteLines([.. new SupplyChainElementMapper().Visible(model, DiagramViewport.Unbounded, SupplyChainTrace.None)
            .Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]);
    }

    /// <summary>
    /// The marks every payload carries with the first drawn node, the first drawn flow and the first
    /// drawn group selected in turn: what the trace highlight sends to the canvas.
    /// </summary>
    private static void WriteTraces(Utf8JsonWriter json, SupplyChainModel model)
    {
        var layout = SupplyChainLayout.Of(model);
        var mapper = new SupplyChainElementMapper();
        IEnumerable<string?> selections =
        [
            layout.Nodes.Select(node => node.Id).FirstOrDefault(),
            layout.Flows.Select(flow => flow.Id).FirstOrDefault(),
            layout.Groups.Select(group => group.Id).FirstOrDefault(),
        ];

        json.WriteStartArray("traces");
        foreach (var selected in selections.OfType<string>())
        {
            json.WriteStartObject();
            json.WriteString("selected", selected);
            json.WritePropertyName("marks");
            json.WriteLines([.. mapper.Visible(model, DiagramViewport.Unbounded, SupplyChainTrace.Through(layout, selected))
                .Select(element => $"{TranscriptText.Quoted(element.Id)} | {MarkOf(element)}")]);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>The trace mark a payload carries, read from the bytes the canvas receives.</summary>
    private static string MarkOf(DiagramElement element)
    {
        var bytes = element.Payload.ToArray();
        return element.Type switch
        {
            SupplyChainElementMapper.GroupType => SupplyChainGroupPayload.Parser.ParseFrom(bytes).Trace,
            SupplyChainElementMapper.FlowType => SupplyChainFlowPayload.Parser.ParseFrom(bytes).Trace,
            _ => SupplyChainNodePayload.Parser.ParseFrom(bytes).Trace,
        };
    }

    private static string Location(DiagramProblemLocation? location) => location switch
    {
        DiagramProblemLineLocation line => $"line {line.Number}",
        DiagramProblemElementLocation element => $"element {TranscriptText.Quoted(element.Id)}",
        null => "nowhere",
        _ => location.ToString(),
    };

    private static string Answer(string refusal) => refusal.Length == 0 ? "accepted" : $"refused: {refusal}";

    /// <summary>The distinct non-empty node ids, in the order the document gives them.</summary>
    private static List<string> NodeIds(SupplyChainModel model) =>
        [.. model.Nodes.Select(node => node.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal)];

    /// <summary>One document's store, history and providers.</summary>
    private sealed class Session : IDisposable
    {
        public Session(string folder, string body)
        {
            Folder = folder;
            Body = body;
            History = TestHistory.For(s => { s.AddSupplyChain(); s.AddSingleton<ISupplyChainDocumentStore>(Store); });
            Actions = new SupplyChainContextActionProvider(History, Store);
            Properties = new SupplyChainContextPropertyProvider(History, Store);
        }

        public string Folder { get; }

        public string Body { get; }

        private SupplyChainDocumentStore Store { get; } = new(new FileWriter());

        private IHistoryStackStore History { get; }

        public SupplyChainContextActionProvider Actions { get; }

        public SupplyChainContextPropertyProvider Properties { get; }

        /// <summary>Opens the document, as the diagram's first open does; the readings and edits read what it holds.</summary>
        public async Task ReadAsync(CancellationToken cancellationToken) => await Store.GetOrLoadAsync(Body, cancellationToken);

        public SupplyChainModel Model => Store.Get(Body)!.Model;

        public IHistoryStack Stack => History.Get(ProjectRoot.From(Folder));

        /// <summary>A canvas session over the document, as a connection opens one.</summary>
        public SupplyChainSession Diagram(bool history) =>
            new(ShortGuid.NewShortGuid(), Body, Store, new SupplyChainElementMapper(), new SupplyChainSelections(), history ? Stack : null);

        public ContextTarget Target(string elementId) =>
            ContextTarget.ForElement(Body, elementId, ProjectRoot.From(Folder), origin: SupplyChain.Diagram.SupplyChain.Origin);

        public void Dispose() => (History as IDisposable)?.Dispose();
    }

    /// <summary>
    /// One document's scripted edit sequence: drags of a node and a group, every property row of a
    /// node, a flow and a group with values that reach their refusals, the menus' actions and their
    /// dialogs' commits, the adds, the connect gestures, every command's own refusals, the removals and
    /// Arrange diagram, each run through the real history on a copy of the document, then everything
    /// undone. Each step is written as it goes.
    /// </summary>
    private sealed partial class Script(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        private readonly Dictionary<string, string> _minted = new(StringComparer.Ordinal);
        private string _text = "";

        public async Task RunAsync()
        {
            _text = await ReadAsync();
            var original = _text;
            json.WriteStartArray("edits");

            var model = session.Model;
            var nodeIds = NodeIds(model);
            var first = nodeIds.FirstOrDefault();
            var second = nodeIds.Skip(1).FirstOrDefault();
            var flow = model.Flows.Select(candidate => candidate.Id).FirstOrDefault(id => id.Length > 0);
            var group = model.Groups.Select(candidate => candidate.Id).FirstOrDefault(id => id.Length > 0);
            var groupName = model.Groups.FirstOrDefault(candidate => candidate.Id.Length > 0) is { } named ? (named.Name.Length > 0 ? named.Name : named.Id) : "Nowhere";

            if (first is not null)
            {
                await DragNodeAsync(first);
                await DragIntoAnotherGroupAsync(first);
                await NodeRowsAsync(first, groupName);
                await NodeActionsAsync(first);
            }

            if (group is not null)
            {
                await DragGroupAsync(group);
                await GroupRowsAndActionsAsync(group);
            }

            if (flow is not null)
            {
                await FlowRowsAndActionsAsync(flow);
            }

            var added = await AddsAsync(first);
            await ConnectsAsync(first, second, added);
            await RefusalsAsync(first, group);
            await PlacesAsync(first);
            await RemovalsAsync(first, second, flow, group);
            await ExecuteAsync("", SupplyChainContextActionProvider.ArrangeActionId);

            json.WriteEndArray();
            await UndoAllAsync(original);
        }

        private async Task NodeRowsAsync(string node, string groupName)
        {
            await SetAsync(node, SupplyChainContextPropertyProvider.NameProperty, "Parity: name #1");
            await SetAsync(node, SupplyChainContextPropertyProvider.NameProperty, "on");
            await SetAsync(node, SupplyChainContextPropertyProvider.NameProperty, "Line one\nline two");
            await SetAsync(node, SupplyChainContextPropertyProvider.NameProperty, "");
            await SetAsync(node, SupplyChainContextPropertyProvider.StageProperty, SupplyChainNodeTypes.Producer);
            await SetAsync(node, SupplyChainContextPropertyProvider.StageProperty, "supplier");
            await SetAsync(node, SupplyChainContextPropertyProvider.StageProperty, "quarry");
            await SetAsync(node, SupplyChainContextPropertyProvider.GroupProperty, groupName);
            await SetAsync(node, SupplyChainContextPropertyProvider.GroupProperty, "Nowhere");
            await SetAsync(node, SupplyChainContextPropertyProvider.GroupProperty, SupplyChainContextPropertyProvider.NoGroup);
            await SetAsync(node, SupplyChainContextPropertyProvider.DescriptionProperty, "2026-10-11");
            await SetAsync(node, SupplyChainContextPropertyProvider.DescriptionProperty, "y");
            await SetAsync(node, SupplyChainContextPropertyProvider.DescriptionProperty, "");
            await SetAsync(node, SupplyChainContextPropertyProvider.QuantityProperty, "12.5");
            await SetAsync(node, SupplyChainContextPropertyProvider.QuantityProperty, "-1");
            await SetAsync(node, SupplyChainContextPropertyProvider.QuantityProperty, "lots");
            await SetAsync(node, SupplyChainContextPropertyProvider.QuantityProperty, "");
            await SetAsync(node, SupplyChainContextPropertyProvider.UnitProperty, "t: metric");
            await SetAsync(node, SupplyChainContextPropertyProvider.UnitProperty, "");
            await SetAsync(node, SupplyChainContextPropertyProvider.StepProperty, "0");
            await SetAsync(node, SupplyChainContextPropertyProvider.StepProperty, "0.25");
            await SetAsync(node, SupplyChainContextPropertyProvider.ProductProperty, "Not for a node");
            await SetAsync(node, "parity:property", "x");
        }

        private async Task NodeActionsAsync(string node)
        {
            await ExecuteAsync(node, SupplyChainContextActionProvider.DecreaseActionId);
            await ExecuteAsync(node, SupplyChainContextActionProvider.IncreaseActionId);
            await ExecuteAsync(node, SupplyChainContextActionProvider.IncreaseActionId);
            await ExecuteAsync(node, SupplyChainContextActionProvider.DecreaseActionId);
            await SetAsync(node, SupplyChainContextPropertyProvider.StepProperty, "");
            await ExecuteAsync(node, SupplyChainContextActionProvider.IncreaseActionId);
            await ExecuteAsync(node, "supply-chain.rename");
            await CommitAsync(node, "supply-chain.rename", "Parity renamed");
            await ExecuteAsync(node, SupplyChainContextActionProvider.GroupActionId);
            await ValidateAsync(node, SupplyChainContextActionProvider.GroupActionId, "  ");
            await ValidateAsync(node, SupplyChainContextActionProvider.GroupActionId, "Parity group");
            await CommitAsync(node, SupplyChainContextActionProvider.GroupActionId, "Parity group");
            await ExecuteAsync(node, SupplyChainContextActionProvider.ConnectActionId);
            await ExecuteAsync(node, SupplyChainContextActionProvider.ArrangeActionId);
        }

        private async Task GroupRowsAndActionsAsync(string group)
        {
            await SetAsync(group, SupplyChainContextPropertyProvider.NameProperty, "Parity region");
            await SetAsync(group, SupplyChainContextPropertyProvider.DescriptionProperty, "Parity: a region #2");
            await SetAsync(group, SupplyChainContextPropertyProvider.DescriptionProperty, "");
            await SetAsync(group, SupplyChainContextPropertyProvider.QuantityProperty, "3");
            await SetAsync(group, SupplyChainContextPropertyProvider.StageProperty, SupplyChainNodeTypes.Hub);
            await ExecuteAsync(group, "supply-chain.rename");
            await CommitAsync(group, "supply-chain.rename", "Parity renamed region");
            await ExecuteAsync(group, SupplyChainContextActionProvider.IncreaseActionId);
            await ExecuteAsync(group, SupplyChainContextActionProvider.GroupActionId);
            await DispatchAsync($"step {group} by 1", new StepSupplyChainValueCommand(session.Body, group, 1));
        }

        private async Task FlowRowsAndActionsAsync(string flow)
        {
            await SetAsync(flow, SupplyChainContextPropertyProvider.ProductProperty, "Parity: product #3");
            await SetAsync(flow, SupplyChainContextPropertyProvider.DescriptionProperty, "off");
            await SetAsync(flow, SupplyChainContextPropertyProvider.DescriptionProperty, "");
            await SetAsync(flow, SupplyChainContextPropertyProvider.VolumeProperty, "3");
            await SetAsync(flow, SupplyChainContextPropertyProvider.UnitProperty, "kt");
            await SetAsync(flow, SupplyChainContextPropertyProvider.StepProperty, "0.5");
            await ExecuteAsync(flow, SupplyChainContextActionProvider.IncreaseActionId);
            await ExecuteAsync(flow, SupplyChainContextActionProvider.DecreaseActionId);
            await SetAsync(flow, SupplyChainContextPropertyProvider.VolumeProperty, "0");
            await ExecuteAsync(flow, SupplyChainContextActionProvider.DecreaseActionId);
            await SetAsync(flow, SupplyChainContextPropertyProvider.VolumeProperty, "");
            await SetAsync(flow, SupplyChainContextPropertyProvider.NameProperty, "Not for a flow");
            await SetAsync(flow, SupplyChainContextPropertyProvider.StageProperty, SupplyChainNodeTypes.Hub);
            await SetAsync(flow, SupplyChainContextPropertyProvider.GroupProperty, SupplyChainContextPropertyProvider.NoGroup);
            await ExecuteAsync(flow, "supply-chain.rename");
            await CommitAsync(flow, "supply-chain.rename", "Parity product");
            await ExecuteAsync(flow, SupplyChainContextActionProvider.GroupActionId);
        }

        /// <summary>The menus' adds at a placement and off one, each stage once and a group, with what they mint.</summary>
        private async Task<string?> AddsAsync(string? first)
        {
            var placement = Placement;
            string? added = null;
            foreach (var stage in SupplyChainNodeTypes.All)
            {
                added ??= await ExecuteAsync(placement, SupplyChainContextActionProvider.AddActionId(stage));
            }

            await ExecuteAsync(GestureIds.Placement(PlacementX + 1000, PlacementY), SupplyChainContextActionProvider.AddGroupActionId);
            await ExecuteAsync(placement, SupplyChainContextActionProvider.AddActionId("quarry"));
            await ExecuteAsync("", SupplyChainContextActionProvider.AddActionId(SupplyChainNodeTypes.Source));
            await ExecuteAsync(first ?? "", SupplyChainContextActionProvider.AddGroupActionId);
            return added;
        }

        private async Task ConnectsAsync(string? first, string? second, string? added)
        {
            if (first is not null)
            {
                if (second is not null)
                {
                    await ExecuteAsync(GestureIds.Relation(first, second), SupplyChainContextActionProvider.ConnectActionId);
                    await ExecuteAsync(GestureIds.Relation(first, second), SupplyChainContextActionProvider.ConnectActionId);
                }

                if (added is not null)
                {
                    await ExecuteAsync(GestureIds.Relation(added, first), SupplyChainContextActionProvider.ConnectActionId);
                }

                await ExecuteAsync(GestureIds.Relation(first, first), SupplyChainContextActionProvider.ConnectActionId);
                await ExecuteAsync(GestureIds.Relation(first, Unknown), SupplyChainContextActionProvider.ConnectActionId);
            }

            await ExecuteAsync("", SupplyChainContextActionProvider.ConnectActionId);
        }

        /// <summary>Each command's refusals that no gesture reaches: an id already taken, a stage or a name that is no such thing, a step of nothing, and an entry that is not there.</summary>
        private async Task RefusalsAsync(string? first, string? group)
        {
            var body = session.Body;
            var taken = first ?? group;
            if (taken is not null)
            {
                await DispatchAsync($"add a source as {taken}", new AddSupplyChainNodeCommand(body, SupplyChainNodeTypes.Source, PlacementX, PlacementY, taken));
                await DispatchAsync($"add a group as {taken}", new AddSupplyChainGroupCommand(body, PlacementX, PlacementY, taken));
            }

            if (first is not null)
            {
                await DispatchAsync($"put {first} in a new group as {first}", new GroupSupplyChainNodeCommand(body, first, "Parity", first));
                await DispatchAsync($"put {first} in a new group named \" \"", new GroupSupplyChainNodeCommand(body, first, " "));
                await DispatchAsync($"step {first} by 0", new StepSupplyChainValueCommand(body, first, 0));
                var other = session.Model.Nodes.Select(node => node.Id).FirstOrDefault(id => id.Length > 0 && id != first && !session.Model.Flows.Any(flow => flow.From == first && flow.To == id));
                if (other is not null)
                {
                    await DispatchAsync($"connect {first} to {other} as {first}", new ConnectSupplyChainNodesCommand(body, first, other, first));
                }
            }

            await DispatchAsync("add a quarry", new AddSupplyChainNodeCommand(body, "quarry", PlacementX, PlacementY));
            await ExecuteAsync(Unknown, "supply-chain.rename");
            await ExecuteAsync(Unknown, SupplyChainContextActionProvider.RemoveActionId);
            await ExecuteAsync(Unknown, SupplyChainContextActionProvider.IncreaseActionId);
            await ExecuteAsync(Unknown, SupplyChainContextActionProvider.GroupActionId);
            await SetAsync(Unknown, SupplyChainContextPropertyProvider.NameProperty, "Nobody");
            await CommitAsync(Unknown, "supply-chain.rename", "Nobody");
            await DispatchAsync($"remove {Unknown}", new RemoveSupplyChainEntryCommand(body, Unknown));
            await DispatchAsync($"step {Unknown} by 1", new StepSupplyChainValueCommand(body, Unknown, 1));
            await DispatchAsync($"put {Unknown} in a new group", new GroupSupplyChainNodeCommand(body, Unknown, "Parity"));
            await DispatchAsync($"move {Unknown}", new MoveSupplyChainEntryCommand(body, Unknown, 0, 0));
        }

        private async Task PlacesAsync(string? first)
        {
            if (first is not null)
            {
                await DispatchAsync($"place {first} at (10.5, 21.5)", new PlaceSupplyChainNodesCommand(session.Body, new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal) { [first] = (10.5, 21.5) }));
            }

            await DispatchAsync($"place {Unknown}", new PlaceSupplyChainNodesCommand(session.Body, new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal) { [Unknown] = (0, 0) }));
        }

        private async Task RemovalsAsync(string? first, string? second, string? flow, string? group)
        {
            if (flow is not null)
            {
                await ExecuteAsync(flow, SupplyChainContextActionProvider.RemoveActionId);
            }

            if (group is not null)
            {
                await ExecuteAsync(group, SupplyChainContextActionProvider.RemoveActionId);
            }

            foreach (var removed in new[] { second, first }.OfType<string>())
            {
                if (await ExecuteAsync(removed, SupplyChainContextActionProvider.RemoveActionId) is "confirm")
                {
                    await CommitAsync(removed, SupplyChainContextActionProvider.RemoveActionId, "");
                }
            }
        }

        /// <summary>A drag of a node by a few units and a half from where it is drawn, as the canvas commits one.</summary>
        private async Task DragNodeAsync(string node)
        {
            var layout = SupplyChainLayout.Of(session.Model);
            if (!layout.NodeBoxes.TryGetValue(node, out var box))
            {
                await RecordAsync($"drag {node}", "not asked: the node is not drawn");
                return;
            }

            await DragAsync($"drag {node} by (10.5, 20.5)", node, box.X + 10.5, box.Y + 20.5);
        }

        /// <summary>A drag of a node into the middle of the first other group's frame, which it joins.</summary>
        private async Task DragIntoAnotherGroupAsync(string node)
        {
            var layout = SupplyChainLayout.Of(session.Model);
            var current = layout.Nodes.FirstOrDefault(candidate => candidate.Id == node)?.Group ?? "";
            var other = layout.Groups.FirstOrDefault(candidate => candidate.Id != current);
            if (!layout.NodeBoxes.ContainsKey(node) || other is null)
            {
                await RecordAsync($"drag {node} into another group", "not asked: no node and other group are drawn");
                return;
            }

            var frame = layout.GroupBoxes[other.Id];
            await DragAsync(
                $"drag {node} into {other.Id}",
                node,
                frame.CenterX - (SupplyChainGeometry.NodeWidth / 2),
                frame.CenterY - (SupplyChainGeometry.NodeHeight / 2));
        }

        /// <summary>A drag of a group's frame, which moves its members by the same distance.</summary>
        private async Task DragGroupAsync(string group)
        {
            var layout = SupplyChainLayout.Of(session.Model);
            if (!layout.GroupBoxes.TryGetValue(group, out var frame))
            {
                await RecordAsync($"drag {group}", "not asked: the group is not drawn");
                return;
            }

            await DragAsync($"drag {group} by (30, 40.5)", group, frame.X + 30, frame.Y + 40.5);
        }

        private async Task DragAsync(string step, string id, double x, double y)
        {
            string answer;
            try
            {
                await using var diagram = session.Diagram(history: true);
                answer = Answer(await diagram.MoveElementToAsync(id, x, y, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"{step} to ({Number(x)}, {Number(y)})", answer);
        }

        private async Task SetAsync(string target, string propertyId, string value)
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

            await RecordAsync($"set {Mask(target)} {propertyId} = {TranscriptText.Quoted(value)}", answer);
        }

        private async Task ValidateAsync(string target, string actionId, string value)
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

            await RecordAsync($"validate {actionId} on {Mask(target)} = {TranscriptText.Quoted(value)}", answer);
        }

        /// <summary>Runs an action; answers "confirm" when it asked for a confirmation, else the first id it minted, else null.</summary>
        private async Task<string?> ExecuteAsync(string target, string actionId)
        {
            ContextExecutionResult result;
            try
            {
                result = await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RecordAsync($"execute {actionId} on {Mask(target)}", Thrown(exception));
                return null;
            }

            var minted = await RecordAsync($"execute {actionId} on {Mask(target)}", Describe(result));
            return result is ContextExecutionRequiresConfirmation ? "confirm" : minted;
        }

        private async Task CommitAsync(string target, string actionId, string value)
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

            await RecordAsync($"commit {actionId} on {Mask(target)} = {TranscriptText.Quoted(value)}", answer);
        }

        /// <summary>Dispatches a command on the project's history, as a provider or the session does.</summary>
        private async Task DispatchAsync(string step, ICommand command)
        {
            string answer;
            try
            {
                var result = await session.Stack.ExecuteAsync(command, cancellationToken);
                answer = result.IsSuccess ? "ok" : $"refused: {result.Error}";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"dispatch {Mask(step)}", answer);
        }

        private async Task UndoAllAsync(string original)
        {
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
                json.WriteChange(Mask(original), Mask(restored));
                json.WriteEndObject();
            }
        }

        /// <summary>
        /// A step today's code throws on, recorded rather than ending the transcript: what it throws is
        /// the answer a user would meet, and the steps after it still run.
        /// </summary>
        private static string Thrown(Exception exception) => $"throws {exception.GetType().Name}: {exception.Message}";

        /// <summary>
        /// Writes one step, first naming every id it minted, and answers the first of them. A minted id
        /// is found in the text rather than in the model, because a step may leave a document the
        /// parser can no longer read: a <see cref="ShortGuid"/> the text did not hold before the step.
        /// </summary>
        private async Task<string?> RecordAsync(string step, string answer)
        {
            var after = await ReadAsync();
            string? minted = null;
            foreach (var id in MintedIds().Matches(after).Select(match => match.Value).Distinct(StringComparer.Ordinal))
            {
                if (_minted.ContainsKey(id) || _text.Contains(id, StringComparison.Ordinal) || !ShortGuid.TryParse(id, null, out _))
                {
                    continue;
                }

                _minted[id] = $"{{minted-{_minted.Count + 1}}}";
                minted ??= id;
            }

            json.WriteStartObject();
            json.WriteString("do", step);
            json.WriteString("answer", Mask(answer));
            json.WriteChange(Mask(_text), Mask(after));
            json.WriteEndObject();
            _text = after;
            return minted;
        }

        /// <summary>What a <see cref="ShortGuid"/> looks like in a document: 25 lower-case base-36 characters standing alone.</summary>
        [GeneratedRegex("(?<![0-9a-z])[0-9a-z]{25}(?![0-9a-z])")]
        private static partial Regex MintedIds();

        private async Task<string> ReadAsync() => TranscriptText.Decode(await File.ReadAllBytesAsync(session.Body, cancellationToken));

        private string Mask(string text) =>
            _minted.Aggregate(text, (masked, entry) => masked.Replace(entry.Key, entry.Value, StringComparison.Ordinal));

        private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

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
