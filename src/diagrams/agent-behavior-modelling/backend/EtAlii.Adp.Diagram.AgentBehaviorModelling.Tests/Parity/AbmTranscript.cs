using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
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
internal static class AbmTranscript
{
    /// <summary>The checked-in file's name, in this project's <c>Parity</c> folder.</summary>
    public const string FileName = "abm.transcript.json";

    private const string TestProject = "EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests";
    private const string Unknown = "parity:unknown";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(AgentBehaviorModellingReflection.Descriptor);

    /// <summary>The module's folder, <c>src/diagrams/agent-behavior-modelling</c>.</summary>
    public static string ModuleFolder { get; } = FindModuleFolder();

    /// <summary>The checked-in transcript.</summary>
    public static string CheckedInPath => Path.Combine(ModuleFolder, "backend", TestProject, "Parity", FileName);

    private static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The documents on disk, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    public static IReadOnlyList<string> Files()
    {
        static IEnumerable<string> Sorted(IEnumerable<string> paths) =>
            paths.Select(path => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/')).Order(StringComparer.Ordinal);

        var examples = AbmExamples.Names.Select(AbmExamples.BodyOf);
        var shipped = Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "agent-behavior-modelling")).SelectMany(folder => Directory.GetFiles(folder, "*.md"));
        return [.. Sorted(examples), .. Sorted(shipped)];
    }

    /// <summary>The whole transcript, as the bytes the checked-in file must hold.</summary>
    public static async Task<byte[]> GenerateAsync(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await using (var json = new Utf8JsonWriter(stream, TranscriptText.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("module", Diagram.AgentBehaviorModelling.Origin.Key);
            json.WriteString("generator", $"src/diagrams/agent-behavior-modelling/backend/{TestProject}/Parity/AbmTranscript.cs");
            json.WriteString("regenerate", "ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)");

            // The toolbox is static: it does not read the document, so it is written once.
            json.WriteStartArray("toolbox");
            foreach (var item in new AbmToolboxProvider().Items)
            {
                json.WriteStringValue($"{item.Id} | {item.Label} | {item.Icon} | drops {item.DropActionId} | {item.Description}");
            }

            json.WriteEndArray();

            json.WriteStartArray("documents");
            var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var relative in Files())
            {
                var path = Path.Combine(SourceFolder, relative);
                var registration = Path.ChangeExtension(path, DiagramFileName.Extension);
                await WriteDocumentAsync(
                    json,
                    relative,
                    await File.ReadAllBytesAsync(path, cancellationToken),
                    File.Exists(registration) ? await File.ReadAllBytesAsync(registration, cancellationToken) : null,
                    recorded,
                    cancellationToken);
            }

            foreach ((string name, string text) in AbmCorpus.Inline)
            {
                await WriteDocumentAsync(json, name, TranscriptText.Encode(text), null, recorded, cancellationToken);
            }

            json.WriteEndArray();
            json.WriteEndObject();
            await json.FlushAsync(cancellationToken);
        }

        stream.Write("\r\n"u8);
        return stream.ToArray();
    }

    private static async Task WriteDocumentAsync(
        Utf8JsonWriter json,
        string name,
        byte[] bytes,
        byte[]? registration,
        Dictionary<string, string> recorded,
        CancellationToken cancellationToken)
    {
        var sha = TranscriptText.Sha256(bytes);

        json.WriteStartObject();
        json.WriteString("document", name);
        json.WriteString("sha256", sha);
        if (recorded.TryGetValue(sha, out var first))
        {
            json.WriteString("sameAs", first);
            json.WriteEndObject();
            return;
        }

        recorded[sha] = name;

        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.AbmTranscript", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = name[(name.LastIndexOf('/') + 1)..];
            var body = Path.Combine(folder, file.EndsWith(".md", StringComparison.Ordinal) ? file : file + ".md");
            await File.WriteAllBytesAsync(body, bytes, cancellationToken);
            if (registration is not null)
            {
                await File.WriteAllBytesAsync(Path.ChangeExtension(body, DiagramFileName.Extension), registration, cancellationToken);
            }

            using var session = new Session(folder, body);
            await WriteReadingsAsync(json, session, bytes, cancellationToken);
            await WriteEditsAsync(json, session, cancellationToken);
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
            var menu = MenuIndex(menus, TranscriptText.MenuLines(await session.Actions.DiscoverAsync(session.Target(target), cancellationToken)));
            var menuReadOnly = MenuIndex(menus, TranscriptText.MenuLines(await session.ReadOnlyActions.DiscoverAsync(session.Target(target), cancellationToken)));
            IReadOnlyList<string> rows = [.. (await session.Properties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => TranscriptText.RowLine(row, choices))];
            IReadOnlyList<string> rowsReadOnly = [.. (await session.ReadOnlyProperties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => TranscriptText.RowLine(row, choices))];
            lines.Add((target, menu, menuReadOnly, rows, rowsReadOnly));
        }

        json.WriteStartArray("menus");
        foreach (var menu in menus)
        {
            WriteLines(json, menu);
        }

        json.WriteEndArray();

        json.WriteStartArray("choices");
        foreach (var list in choices)
        {
            WriteLines(json, list);
        }

        json.WriteEndArray();

        json.WriteStartArray("targets");
        foreach ((string target, int menu, int menuReadOnly, _, _) in lines)
        {
            json.WriteStringValue($"{JsonSerializer.Serialize(target, TranscriptText.StringOptions)} => menu #{menu}, read-only #{menuReadOnly}");
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
            WriteLines(json, rows);
            if (TranscriptText.ReadOnlyVariant(rows, rowsReadOnly) is { } same)
            {
                json.WriteString("rowsReadOnly", same);
            }
            else
            {
                json.WritePropertyName("rowsReadOnly");
                WriteLines(json, rowsReadOnly);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();

        var problems = await new AbmValidator().ValidateAsync(
            new DiagramValidationRequest(TranscriptText.Decode(bytes), Path.GetFileNameWithoutExtension(session.Body), session.Folder, session.Body, null),
            cancellationToken);
        json.WritePropertyName("findings");
        WriteLines(json, [.. problems.Select(problem => $"line {(problem.Location as DiagramProblemLineLocation)?.Number} | {problem.Severity} | {problem.RuleId} | {problem.Message}")]);

        json.WritePropertyName("payloads");
        WriteLines(json, [.. new AbmElementMapper().Visible(model, RegistrationLayout.Read(session.Registration), DiagramViewport.Unbounded)
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

        AbmNode? Root() => session.Model.Roots.FirstOrDefault();
        AbmNode? Leaf() => session.Model.Nodes.FirstOrDefault(node => node.Category == AbmNodeCategory.Leaf);
        AbmNode? Branching() => session.Model.Nodes.FirstOrDefault(node => node.ChildIds.Count >= 2);

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
    }

    private static int MenuIndex(List<IReadOnlyList<string>> menus, IReadOnlyList<string> menu)
    {
        var index = menus.FindIndex(known => known.SequenceEqual(menu, StringComparer.Ordinal));
        if (index >= 0)
        {
            return index;
        }

        menus.Add(menu);
        return menus.Count - 1;
    }

    private static void WriteLines(Utf8JsonWriter json, IEnumerable<string> lines)
    {
        json.WriteStartArray();
        foreach (var line in lines)
        {
            json.WriteStringValue(line);
        }

        json.WriteEndArray();
    }

    private static string FindModuleFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.Name == "agent-behavior-modelling")
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No agent-behavior-modelling folder above {AppContext.BaseDirectory}. That is a failure, not a skip.");
    }

    /// <summary>One document's store, history and providers, readable and read-only.</summary>
    private sealed class Session : IDisposable
    {
        public Session(string folder, string body)
        {
            Folder = folder;
            Body = body;
            History = new HistoryStackStore(new AbmTestDispatcher(Store));
            var readOnly = new ReadOnlyStore(Store);
            Actions = new AbmContextActionProvider(History, Store);
            ReadOnlyActions = new AbmContextActionProvider(History, readOnly);
            Properties = new AbmContextPropertyProvider(History, Store);
            ReadOnlyProperties = new AbmContextPropertyProvider(History, readOnly);
        }

        public string Folder { get; }

        public string Body { get; }

        public string Registration => Path.ChangeExtension(Body, DiagramFileName.Extension);

        public AbmDocumentStore Store { get; } = new();

        public HistoryStackStore History { get; }

        public AbmContextActionProvider Actions { get; }

        public AbmContextActionProvider ReadOnlyActions { get; }

        public AbmContextPropertyProvider Properties { get; }

        public AbmContextPropertyProvider ReadOnlyProperties { get; }

        public AbmModel Model => Store.GetOrLoad(Body).Model;

        public ContextTarget Target(string elementId) =>
            new(ContextScope.DiagramElement, Body, IsContainer: false, SourceId: default, Folder, default, elementId, Diagram.AgentBehaviorModelling.Origin);

        public void Dispose() => History.Dispose();
    }

    /// <summary>The store's own entries, each marked as unreadable: the backend's one read-only case.</summary>
    private sealed class ReadOnlyStore(IAbmDocumentStore inner) : IAbmDocumentStore
    {
        public event EventHandler<AbmDocumentChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public AbmDocumentEntry GetOrLoad(string path) => inner.GetOrLoad(path) with { Unreadable = "parity: read-only" };

        public DocumentSaveResult Save(string path, AbmBody document) => inner.Save(path, document);

        public void Forget(string path) => inner.Forget(path);

        public void BodyDeleted(string path) => inner.BodyDeleted(path);

        public void Reload(string path) => inner.Reload(path);
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

            var stack = session.History.Get(session.Folder);
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
            await RecordAsync($"set {target} {propertyId} = {JsonSerializer.Serialize(value, TranscriptText.StringOptions)}", result.IsSuccess ? "ok" : $"refused: {result.Error}");
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
                $"commit {actionId} on {target} = {JsonSerializer.Serialize(value, TranscriptText.StringOptions)}",
                result.Completed ? "ok" : $"refused: {result.Error}");
        }

        private async Task RecordAsync(string step, string answer)
        {
            var after = await ReadAsync();
            json.WriteStartObject();
            json.WriteString("do", step);
            json.WriteString("answer", answer);
            TranscriptText.WriteChange(json, _text, after);
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
