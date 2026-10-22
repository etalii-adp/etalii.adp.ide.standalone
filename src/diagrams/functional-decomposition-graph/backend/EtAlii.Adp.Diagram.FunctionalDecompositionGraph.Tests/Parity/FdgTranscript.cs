using System.Globalization;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// The functional decomposition graph's parity transcript: what today's hand-written code answers
/// for every document of the corpus, frozen as <c>Parity/functional-decomposition-graph.transcript.json</c>
/// before any of it is derived from a bundled DISL definition or read through an FBL binding. Every
/// later step of <c>functional-decomposition-graph-disl-fbl</c> must reproduce it byte for byte, or
/// change it only where that specification's requirements name the change.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>: the test project's <c>Fixtures/*.fdg</c>, the module's <c>examples/*/*.fdg</c>
/// and the shipped copies under <c>src/examples/diagrams/functional-decomposition-graph/</c>. A
/// document whose bytes equal one recorded earlier is written as <c>sameAs</c> that one.
/// </para>
/// <para>
/// <b>Per document</b>: the context menu of every element and connection id, of the empty canvas,
/// of a placement, of a connect gesture and of an unknown id, each for a readable and for a
/// read-only document; the property rows of each of those targets; the findings; the payload of
/// every mapped element; a scripted edit sequence that runs every one of the module's nine commands
/// at least once, each step's answer and the hunks it made to the text; and, for the fixtures that
/// need a step of their own to show their finding, a short probe sequence on a fresh copy.
/// </para>
/// <para>
/// <b>Nothing is masked.</b> Unlike the hype cycle's, this transcript never lets the code mint an
/// id: every add and every successful connect is a command whose new id is given, and only refused
/// connects and adds go through the menus, which mint nothing. A minted id would otherwise have to
/// be masked, and a masked value is one the transcript could not hold the code to.
/// </para>
/// <para>
/// <b>The invariant culture is set for the whole run</b>, so the transcript is the same on every
/// machine. Two sentences of the rule set format a number with the machine's culture (finding A15
/// of the specification); the probe of <c>width-fraction.fdg</c> records them once more under a
/// culture whose decimal separator is a comma, so that the defect is in the transcript and its
/// repair is a named change of it. Task 7 repaired it: the probe reads "12.5" under both cultures.
/// </para>
/// </remarks>
internal static class FdgTranscript
{
    /// <summary>The checked-in file's name, in this project's <c>Parity</c> folder.</summary>
    private const string FileName = "functional-decomposition-graph.transcript.json";

    private const string TestProject = "EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests";
    private const string Unknown = "parity:unknown";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(FunctionalDecompositionGraphReflection.Descriptor);

    /// <summary>The module's folder, <c>src/diagrams/functional-decomposition-graph</c>.</summary>
    private static string ModuleFolder { get; } = RepositoryPaths.Under("src", "diagrams", "functional-decomposition-graph");

    /// <summary>The checked-in transcript.</summary>
    public static string CheckedInPath => Path.Combine(ModuleFolder, "backend", TestProject, "Parity", FileName);

    internal static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The corpus, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    internal static IReadOnlyList<string> Corpus()
    {
        var fixtures = Directory.GetFiles(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures"), "*.fdg");
        var examples = Directory.GetDirectories(Path.Combine(ModuleFolder, "examples")).SelectMany(folder => Directory.GetFiles(folder, "*.fdg"));
        var shipped = Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "functional-decomposition-graph")).SelectMany(folder => Directory.GetFiles(folder, "*.fdg"));
        return [.. Sorted(fixtures), .. Sorted(examples), .. Sorted(shipped)];

        static IEnumerable<string> Sorted(IEnumerable<string> paths) => paths.Select(path => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/')).Order(StringComparer.Ordinal);
    }

    /// <summary>The whole transcript, as the bytes the checked-in file must hold.</summary>
    public static async Task<byte[]> GenerateAsync(CancellationToken cancellationToken)
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            return await WriteAsync(cancellationToken);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private static async Task<byte[]> WriteAsync(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await using (var json = new Utf8JsonWriter(stream, TranscriptText.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("module", Diagram.FunctionalDecompositionGraph.Origin.Key);
            json.WriteString("generator", $"src/diagrams/functional-decomposition-graph/backend/{TestProject}/Parity/FdgTranscript.cs");
            json.WriteString("regenerate", "ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)");

            // The toolbox is static: it does not read the document, so it is written once.
            json.WriteStartArray("toolbox");
            foreach (var item in FdgDefinition.Definition.Toolbox)
            {
                json.WriteStringValue($"{item.Id} | {item.Label} | {item.Icon} | drops {item.DropActionId} | {item.Description}");
            }

            json.WriteEndArray();

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

        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.FdgTranscript", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var body = Path.Combine(folder, Path.GetFileName(source));
            await File.WriteAllBytesAsync(body, bytes, cancellationToken);
            using var session = new Session(folder, body);
            await session.ReadAsync(cancellationToken);
            await WriteReadingsAsync(json, session, bytes, cancellationToken);
            await WriteEditsAsync(json, session, cancellationToken);
            if (FdgTranscriptProbes.For(Path.GetFileName(source)) is { } probes)
            {
                var script = new Script(json, session, "probes", cancellationToken);
                var original = await script.StartAsync();
                await probes(script);
                await script.EndAsync(original, "probesUndoAll");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A temp folder left behind changes nothing recorded.
            }
        }

        json.WriteEndObject();
    }

    private static async Task WriteReadingsAsync(Utf8JsonWriter json, Session session, byte[] bytes, CancellationToken cancellationToken)
    {
        var model = session.Model;
        List<string> ids =
        [
            .. model.Elements.Select(element => element.Id)
                .Concat(model.Connections.Select(connection => connection.Id))
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];
        var elementIds = model.Elements.Select(element => element.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var relation = elementIds.Count >= 2 ? GestureIds.Relation(elementIds[0], elementIds[1]) : GestureIds.Relation("a", "b");
        List<string> targets = [.. ids, "", GestureIds.Placement(240, 160), relation, Unknown];

        var menus = new List<IReadOnlyList<string>>();
        var lines = new List<(string Target, int Menu, int MenuReadOnly, IReadOnlyList<string> Rows, IReadOnlyList<string> RowsReadOnly)>();
        foreach (var target in targets)
        {
            var menu = menus.IndexOfOrAdd((await session.Actions.DiscoverAsync(session.Target(target), cancellationToken)).MenuLines());
            var menuReadOnly = menus.IndexOfOrAdd((await session.ReadOnlyActions.DiscoverAsync(session.Target(target), cancellationToken)).MenuLines());
            IReadOnlyList<string> rows = [.. (await session.Properties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => row.RowLine())];
            IReadOnlyList<string> rowsReadOnly = [.. (await session.ReadOnlyProperties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => row.RowLine())];
            lines.Add((target, menu, menuReadOnly, rows, rowsReadOnly));
        }

        json.WriteStartArray("menus");
        foreach (var menu in menus)
        {
            json.WriteLines(menu);
        }

        json.WriteEndArray();

        json.WriteStartArray("targets");
        foreach ((string target, int menu, int menuReadOnly, _, _) in lines)
        {
            json.WriteStringValue($"{TranscriptText.Quoted(target)} => menu #{menu}, read-only #{menuReadOnly}");
        }

        json.WriteEndArray();

        json.WriteStartArray("rows");
        foreach ((string target, _, _, IReadOnlyList<string> rows, IReadOnlyList<string> rowsReadOnly) in lines.Where(line => line.Rows.Count > 0 || line.RowsReadOnly.Count > 0))
        {
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

        json.WritePropertyName("findings");
        json.WriteLines(await FindingsAsync(session, TranscriptText.Decode(bytes), cancellationToken));

        json.WritePropertyName("payloads");
        json.WriteLines([.. new FdgElementMapper().Visible(model, DiagramViewport.Unbounded)
            .Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]);
    }

    /// <summary>The validator's findings for <paramref name="text"/>, one line each.</summary>
    private static async Task<IReadOnlyList<string>> FindingsAsync(Session session, string text, CancellationToken cancellationToken)
    {
        var problems = await new FdgValidator().ValidateAsync(
            new DiagramValidationRequest(text, Path.GetFileNameWithoutExtension(session.Body), ProjectRoot.From(session.Folder), session.Body, null),
            cancellationToken);
        return [.. problems.Select(problem => $"line {(problem.Location as DiagramProblemLineLocation)?.Number} | {problem.Severity} | {problem.RuleId} | {problem.Message}")];
    }

    /// <summary>
    /// The scripted edit sequence, run through the real history on a copy of the document: the
    /// property rows, the menus' actions and the nine commands, in an order that reaches refusals too.
    /// </summary>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var script = new Script(json, session, "edits", cancellationToken);
        var original = await script.StartAsync();

        var model = session.Model;
        var named = model.Elements.Where(element => element.Id.Length > 0 && !element.IsComment && FdgElementTypes.IsKnown(element.Type)).Select(element => element.Id).Distinct(StringComparer.Ordinal).FirstOrDefault();
        var comment = model.Elements.Where(element => element is { Id.Length: > 0, IsComment: true }).Select(element => element.Id).FirstOrDefault();
        var connection = model.Connections.Where(candidate => candidate.Id.Length > 0).Select(candidate => candidate.Id).FirstOrDefault();

        if (named is not null)
        {
            await script.SetAsync(named, FdgContextPropertyProvider.NameProperty, "Parity name");
            await script.SetAsync(named, FdgContextPropertyProvider.DescriptionProperty, "Line one\nline two");
            await script.SetAsync(named, FdgContextPropertyProvider.WidthProperty, "not a size");
            await script.SetAsync(named, FdgContextPropertyProvider.WidthProperty, "12");
            await script.SetAsync(named, FdgContextPropertyProvider.WidthProperty, "140.5");
            await script.SetAsync(named, FdgContextPropertyProvider.HeightProperty, "60");
            await script.SetAsync(named, FdgContextPropertyProvider.TextProperty, "Not a Comment");
            await script.SetAsync(named, FdgContextPropertyProvider.DescriptionProperty, "");
            await script.ExecuteAsync(named, FdgContextActionProvider.RenameActionId);
            await script.CommitAsync(named, FdgContextActionProvider.RenameActionId, "Parity renamed");
            var element = session.Model.ElementOf(named)!;
            await script.CommandAsync($"move {named} to ({Number(element.X + 40)}, {Number(element.Y)})", new SetFdgPlacementCommand(session.Body, named, element.X + 40, element.Y));
        }

        if (comment is not null)
        {
            await script.SetAsync(comment, FdgContextPropertyProvider.TextProperty, "Parity text\nsecond line");
            await script.SetAsync(comment, FdgContextPropertyProvider.NameProperty, "A Comment's name");
            await script.SetAsync(comment, FdgContextPropertyProvider.HeightProperty, "30");
            await script.SetAsync(comment, FdgContextPropertyProvider.HeightProperty, "-1");
            await script.SetAsync(comment, FdgContextPropertyProvider.WidthProperty, "200");
            await script.ExecuteAsync(comment, FdgContextActionProvider.RenameActionId);
            await script.CommandAsync($"size {comment} to 260 x 120", new SetFdgSizeCommand(session.Body, comment, 260, 120));
        }

        if (connection is not null)
        {
            await script.SetAsync(connection, FdgContextPropertyProvider.ConnectionNameProperty, "Parity link");
            await script.SetAsync(connection, FdgContextPropertyProvider.DescriptionProperty, "Parity link description");
            await script.SetAsync(connection, FdgContextPropertyProvider.WidthProperty, "100");
            await script.ExecuteAsync(connection, ConnectionAction.Rename);
            await script.CommitAsync(connection, ConnectionAction.Rename, "Parity renamed link");
        }

        // Every target that names nothing.
        await script.SetAsync(Unknown, FdgContextPropertyProvider.NameProperty, "Nobody");
        await script.SetAsync(Unknown, FdgContextPropertyProvider.DescriptionProperty, "Nobody");
        await script.ExecuteAsync(Unknown, FdgContextActionProvider.RenameActionId);
        await script.CommitAsync(Unknown, FdgContextActionProvider.RenameActionId, "Nobody");
        await script.ExecuteAsync(Unknown, FdgContextActionProvider.RemoveActionId);
        await script.ExecuteAsync(Unknown, ConnectionAction.Disconnect);
        await script.ExecuteAsync(Unknown, FdgContextActionProvider.AddActionId(FdgElementTypes.UiElement));
        await script.ExecuteAsync(Unknown, ConnectionAction.Connect(FdgConnectionTypes.UiChild));
        await script.ExecuteAsync(GestureIds.Placement(240, 160), FdgContextActionProvider.AddActionId("not-a-type"));
        await script.CommandAsync($"move {Unknown} to (1, 2)", new SetFdgPlacementCommand(session.Body, Unknown, 1, 2));
        await script.CommandAsync($"size {Unknown} to 100", new SetFdgSizeCommand(session.Body, Unknown, 100));
        await script.CommandAsync($"rename connection {Unknown}", new RenameFdgConnectionCommand(session.Body, Unknown, "Nobody"));
        await script.CommandAsync($"disconnect {Unknown}", new DisconnectFdgConnectionCommand(session.Body, Unknown));
        await script.CommandAsync($"remove {Unknown}", new RemoveFdgElementCommand(session.Body, Unknown));

        // One element of each type, at given ids, and the two refusals of an add.
        const string ui = "parity-ui-element";
        const string action = "parity-action";
        const string data = "parity-data-element";
        const string function = "parity-function";
        const string otherFunction = "parity-function-2";
        const string note = "parity-comment";
        await script.AddAsync(FdgElementTypes.UiElement, ui, 240, 160);
        await script.AddAsync(FdgElementTypes.Action, action, 240, 280);
        await script.AddAsync(FdgElementTypes.DataElement, data, 240, 400);
        await script.AddAsync(FdgElementTypes.Function, function, 240, 520);
        await script.AddAsync(FdgElementTypes.Function, otherFunction, 240, 640);
        await script.AddAsync(FdgElementTypes.Comment, note, 520, 160);
        await script.AddAsync(FdgElementTypes.UiElement, ui, 0, 0);
        await script.AddAsync("not-a-type", "parity-unknown-type", 0, 0);

        // The refusals of a connect, through the menu, which mints nothing when it refuses.
        await script.ExecuteAsync(GestureIds.Relation(ui, ui), ConnectionAction.Connect(FdgConnectionTypes.UiChild));
        await script.ExecuteAsync(GestureIds.Relation(action, ui), ConnectionAction.Connect(FdgConnectionTypes.UiChild));
        await script.ExecuteAsync(GestureIds.Relation(ui, action), ConnectionAction.Connect("not-a-relation"));
        await script.ExecuteAsync(GestureIds.Relation(ui, Unknown), ConnectionAction.Connect(FdgConnectionTypes.UiChild));
        await script.ExecuteAsync(GestureIds.Relation(note, ui), ConnectionAction.Connect(FdgConnectionTypes.Shows));
        await script.ExecuteAsync(Unknown, ConnectionAction.Connect(FdgConnectionTypes.Shows));

        // Connects at given ids, and the three checks' refusals in their order.
        await script.ConnectAsync(FdgConnectionTypes.OwnsAction, ui, action, "parity-owns-action");
        await script.ConnectAsync(FdgConnectionTypes.OwnsData, action, data, action);
        await script.ConnectAsync(FdgConnectionTypes.OwnsData, action, data, "parity-owns-data");
        await script.ConnectAsync(FdgConnectionTypes.OwnsFunction, function, otherFunction, "parity-owns-function");
        await script.ConnectAsync(FdgConnectionTypes.Shows, action, ui, "parity-shows");
        await script.ExecuteAsync(GestureIds.Relation(ui, action), ConnectionAction.Connect(FdgConnectionTypes.OwnsAction));
        await script.ExecuteAsync(GestureIds.Relation(action, ui), ConnectionAction.Connect(FdgConnectionTypes.Shows));
        await script.ExecuteAsync(GestureIds.Relation(otherFunction, function), ConnectionAction.Connect(FdgConnectionTypes.OwnsFunction));
        if (named is not null && session.Model.ElementOf(named) is { Type: FdgElementTypes.UiElement })
        {
            await script.ConnectAsync(FdgConnectionTypes.UiChild, named, ui, "parity-ui-child");
        }

        await script.SetAsync("parity-shows", FdgContextPropertyProvider.ConnectionNameProperty, "Back");

        // Disconnect, then the removals, each with its confirmation when it has one.
        if (connection is not null)
        {
            await script.ExecuteAsync(connection, ConnectionAction.Disconnect);
        }

        await script.CommandAsync("disconnect parity-owns-function", new DisconnectFdgConnectionCommand(session.Body, "parity-owns-function"));
        foreach (var removed in new[] { named, comment, ui, data }.OfType<string>())
        {
            if (await script.ExecuteAsync(removed, FdgContextActionProvider.RemoveActionId) is "confirm")
            {
                await script.CommitAsync(removed, FdgContextActionProvider.RemoveActionId, "");
            }
        }

        await script.CommandAsync($"remove {function}", new RemoveFdgElementCommand(session.Body, function));

        await script.EndAsync(original, "undoAll");
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The connection actions the provider keeps private, as the client names them in <c>fdgIds.ts</c>.</summary>
    private static class ConnectionAction
    {
        public const string Rename = "fdg.rename-connection";
        public const string Disconnect = "fdg.disconnect";

        public static string Connect(string relation) => $"fdg.connect.{relation}";
    }

    /// <summary>One document's store, history and providers, readable and read-only.</summary>
    internal sealed class Session : IDisposable
    {
        public Session(string folder, string body)
        {
            Folder = folder;
            Body = body;
            History = new HistoryStackStore(new FdgTestDispatcher(Store));
            var readOnly = new ReadOnlyStore(Store);
            Actions = new FdgContextActionProvider(History, Store);
            ReadOnlyActions = new FdgContextActionProvider(History, readOnly);
            Properties = new FdgContextPropertyProvider(History, Store);
            ReadOnlyProperties = new FdgContextPropertyProvider(History, readOnly);
        }

        public string Folder { get; }

        public string Body { get; }

        private FdgDocumentStore Store { get; } = new(new FileWriter());

        public HistoryStackStore History { get; }

        public FdgContextActionProvider Actions { get; }

        public FdgContextActionProvider ReadOnlyActions { get; }

        public FdgContextPropertyProvider Properties { get; }

        public FdgContextPropertyProvider ReadOnlyProperties { get; }

        /// <summary>Opens the document, as the diagram's first open does; the readings and edits read what it holds.</summary>
        public async Task ReadAsync(CancellationToken cancellationToken) => await Store.GetOrLoadAsync(Body, cancellationToken);

        public FdgModel Model => Store.Get(Body)!.Model;

        public ContextTarget Target(string elementId) =>
            ContextTarget.ForElement(Body, elementId, ProjectRoot.From(Folder), origin: Diagram.FunctionalDecompositionGraph.Origin);

        public void Dispose() => History.Dispose();
    }

    /// <summary>The store's own entries, each marked as unreadable: the backend's one read-only case.</summary>
    private sealed class ReadOnlyStore(IFdgDocumentStore inner) : IFdgDocumentStore
    {
        public event EventHandler<DocumentChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public async ValueTask<DocumentEntry<LineDocument, FdgModel>> GetOrLoadAsync(string path, CancellationToken cancellationToken) => await inner.GetOrLoadAsync(path, cancellationToken) with { Unreadable = "parity: read-only" };

        public DocumentEntry<LineDocument, FdgModel>? Get(string path) => inner.Get(path) is { } held ? held with { Unreadable = "parity: read-only" } : null;

        public Task<DocumentSaveResult> SaveAsync(string path, LineDocument document, CancellationToken cancellationToken) => inner.SaveAsync(path, document, cancellationToken);

        public void Forget(string path) => inner.Forget(path);

        public Task BodyDeletedAsync(string path, CancellationToken cancellationToken) => inner.BodyDeletedAsync(path, cancellationToken);

        public Task ReloadAsync(string path, CancellationToken cancellationToken) => inner.ReloadAsync(path, cancellationToken);
    }

    /// <summary>Runs the steps of one document's edit sequence and writes each as it goes.</summary>
    internal sealed class Script(Utf8JsonWriter json, Session session, string name, CancellationToken cancellationToken)
    {
        private string _text = "";

        public async Task<string> StartAsync()
        {
            _text = await ReadTextAsync();
            json.WriteStartArray(name);
            return _text;
        }

        public async Task EndAsync(string original, string undoName)
        {
            json.WriteEndArray();

            var stack = session.History.Get(ProjectRoot.From(session.Folder));
            var undone = 0;
            while (stack.CanUndo && (await stack.UndoAsync(cancellationToken)).IsSuccess)
            {
                undone++;
            }

            var restored = await ReadTextAsync();
            json.WriteString(undoName, $"{undone} undone; {(string.Equals(restored, original, StringComparison.Ordinal) ? "the original text is back" : "the original text is NOT back")}");
        }

        public async Task SetAsync(string target, string propertyId, string value)
        {
            var result = await session.Properties.SetAsync(session.Target(target), propertyId, value, cancellationToken);
            await RecordAsync($"set {target} {propertyId} = {TranscriptText.Quoted(value)}", result.IsSuccess ? "ok" : $"refused: {result.Error}");
        }

        /// <summary>Runs an action; answers "confirm" when it asked for a confirmation, else null.</summary>
        public async Task<string?> ExecuteAsync(string target, string actionId)
        {
            var result = await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken);
            await RecordAsync($"execute {actionId} on {target}", Describe(result));
            return result is ContextExecutionRequiresConfirmation ? "confirm" : null;
        }

        public async Task CommitAsync(string target, string actionId, string value)
        {
            var result = await session.Actions.CommitAsync(session.Target(target), actionId, value, "", cancellationToken);
            await RecordAsync($"commit {actionId} on {target} = {TranscriptText.Quoted(value)}", result.Completed ? "ok" : $"refused: {result.Error}");
        }

        public async Task AddAsync(string type, string id, double x, double y) =>
            await CommandAsync($"add {type} {id} at ({Number(x)}, {Number(y)})", new AddFdgElementCommand(session.Body, type, x, y, id));

        public async Task ConnectAsync(string relation, string from, string to, string id) =>
            await CommandAsync($"connect {relation} {from} -> {to} as {id}", new ConnectFdgElementsCommand(session.Body, relation, from, to, id));

        public async Task MoveAsync(string id, double x, double y) =>
            await CommandAsync($"move {id} to ({Number(x)}, {Number(y)})", new SetFdgPlacementCommand(session.Body, id, x, y));

        public async Task CommandAsync(string description, ICommand command)
        {
            var result = await session.History.Get(ProjectRoot.From(session.Folder)).ExecuteAsync(command, cancellationToken);
            await RecordAsync($"command {description}", result.IsSuccess ? "ok" : $"refused: {result.Error}");
        }

        /// <summary>What the document now reads for one target: its rows, and the findings of the whole text.</summary>
        public async Task ReadBackAsync(string target)
        {
            var text = await ReadTextAsync();
            var rows = (await session.Properties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => row.RowLine()).ToList();
            json.WriteStartObject();
            json.WriteString("do", $"read {target}");
            json.WritePropertyName("rows");
            json.WriteLines(rows);
            json.WritePropertyName("findings");
            json.WriteLines(await FindingsAsync(session, text, cancellationToken));
            json.WriteEndObject();
        }

        /// <summary>The findings of the text as it stands, under <paramref name="culture"/>.</summary>
        public async Task FindingsUnderAsync(string cultureName, CultureInfo culture)
        {
            var text = await ReadTextAsync();
            var before = CultureInfo.CurrentCulture;
            IReadOnlyList<string> findings;
            CultureInfo.CurrentCulture = culture;
            try
            {
                findings = await FindingsAsync(session, text, cancellationToken);
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }

            json.WriteStartObject();
            json.WriteString("do", $"findings under {cultureName}");
            json.WritePropertyName("findings");
            json.WriteLines(findings);
            json.WriteEndObject();
        }

        private async Task RecordAsync(string step, string answer)
        {
            var after = await ReadTextAsync();
            json.WriteStartObject();
            json.WriteString("do", step);
            json.WriteString("answer", answer);
            json.WriteChange(_text, after);
            json.WriteEndObject();
            _text = after;
        }

        private async Task<string> ReadTextAsync() => TranscriptText.Decode(await File.ReadAllBytesAsync(session.Body, cancellationToken));

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
