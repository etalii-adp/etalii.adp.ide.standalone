using System.Text;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests.Parity;

/// <summary>
/// The dependency graph's parity transcript: what the module answers for every document of the
/// corpus, frozen as <c>Parity/dependency-graph.transcript.json</c> from the hand-written code before
/// any of it was derived from the bundled DISL definition. Every switch-over reproduces it byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>: the test project's <c>Fixtures/*.dgr</c>, the module's <c>examples/*/*.dgr</c>,
/// the shipped copies under <c>src/examples/diagrams/dependency-graph/</c>, and the documents of
/// <see cref="DependencyGraphCorpus"/>. A document whose bytes equal one recorded earlier is written as
/// <c>sameAs</c> that one.
/// </para>
/// <para>
/// <b>Per document</b>: the context menu of every id the document declares, the empty canvas, a
/// placement, relation gestures between nodes and to and from a placement, and an unknown id, each
/// for a readable and for a read-only document; the property rows of every one of them; the findings;
/// the payload of every mapped element; and a scripted edit sequence, each step's answer and the hunks
/// it made to the document's text, ending with every step undone.
/// </para>
/// <para>
/// <b>Read-only is the store's unparseable entry</b>, the one case in which the backend refuses an
/// edit: the same model, carrying a reason it could not be read, through every provider.
/// </para>
/// <para>
/// <b>Minted ids are masked.</b> An element or relation added by an action takes a fresh
/// <see cref="ShortGuid"/>, which the transcript writes as <c>{minted-N}</c> in order of minting.
/// </para>
/// </remarks>
internal static class DependencyGraphTranscript
{
    /// <summary>The checked-in file's name, in this project's <c>Parity</c> folder.</summary>
    private const string FileName = "dependency-graph.transcript.json";

    private const string TestProject = "EtAlii.Adp.Diagram.DependencyGraph.Tests";
    private const string Unknown = "parity:unknown";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(DependencyGraphReflection.Descriptor);

    /// <summary>The module's folder, <c>src/diagrams/dependency-graph</c>.</summary>
    private static string ModuleFolder { get; } = FindModuleFolder();

    /// <summary>The checked-in transcript.</summary>
    public static string CheckedInPath => Path.Combine(ModuleFolder, "backend", TestProject, "Parity", FileName);

    private static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The corpus, as paths relative to <c>src/</c> with forward slashes, then the inline documents, in the order they are recorded.</summary>
    private static IReadOnlyList<(string Name, Func<CancellationToken, Task<byte[]>> Read)> Corpus()
    {
        static IEnumerable<string> Sorted(IEnumerable<string> paths) =>
            paths.Select(path => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/')).Order(StringComparer.Ordinal);

        var fixtures = Directory.GetFiles(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures"), "*.dgr");
        var examples = Directory.GetDirectories(Path.Combine(ModuleFolder, "examples")).SelectMany(folder => Directory.GetFiles(folder, "*.dgr"));
        var shipped = Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "dependency-graph")).SelectMany(folder => Directory.GetFiles(folder, "*.dgr"));
        return
        [
            .. Sorted(fixtures).Concat(Sorted(examples)).Concat(Sorted(shipped))
                .Select(relative => (relative, (Func<CancellationToken, Task<byte[]>>)(token => File.ReadAllBytesAsync(Path.Combine(SourceFolder, relative), token)))),
            .. DependencyGraphCorpus.Inline
                .Select(document => (document.Name, (Func<CancellationToken, Task<byte[]>>)(_ => Task.FromResult(Encoding.UTF8.GetBytes(document.Text))))),
        ];
    }

    /// <summary>The whole transcript, as the bytes the checked-in file must hold.</summary>
    public static async Task<byte[]> GenerateAsync(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await using (var json = new Utf8JsonWriter(stream, TranscriptText.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("module", Diagram.DependencyGraph.Origin.Key);
            json.WriteString("generator", $"src/diagrams/dependency-graph/backend/{TestProject}/Parity/DependencyGraphTranscript.cs");
            json.WriteString("regenerate", "ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)");

            // The toolbox is static: it does not read the document, so it is written once.
            json.WriteStartArray("toolbox");
            foreach (var item in new DependencyGraphToolboxProvider().Items)
            {
                json.WriteStringValue($"{item.Id} | {item.Label} | {item.Icon} | drops {item.DropActionId} | {item.Description}");
            }

            json.WriteEndArray();

            json.WriteStartArray("documents");
            var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string name, Func<CancellationToken, Task<byte[]>> read) in Corpus())
            {
                await WriteDocumentAsync(json, name, await read(cancellationToken), recorded, cancellationToken);
            }

            json.WriteEndArray();
            json.WriteEndObject();
            await json.FlushAsync(cancellationToken);
        }

        stream.Write("\r\n"u8);
        return stream.ToArray();
    }

    private static async Task WriteDocumentAsync(Utf8JsonWriter json, string name, byte[] bytes, Dictionary<string, string> recorded, CancellationToken cancellationToken)
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

        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.DependencyGraphTranscript", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var body = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + Diagram.DocumentExtension);
            await File.WriteAllBytesAsync(body, bytes, cancellationToken);
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
        List<string> declared =
        [
            .. model.Elements.Select(element => element.Id)
                .Concat(model.Relations.Select(relation => relation.Id))
                .Distinct(StringComparer.Ordinal),
        ];
        var nodes = model.Elements.Select(element => element.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var from = nodes.Count > 0 ? nodes[0] : "a";
        var to = nodes.Count > 1 ? nodes[1] : "b";
        var placement = GestureIds.RowPlacement(240, 2);
        List<string> targets =
        [
            .. declared,
            placement,
            GestureIds.Relation(from, to),
            GestureIds.Relation(from, placement),
            GestureIds.Relation(placement, from),
            Unknown,
        ];
        if (!targets.Contains("", StringComparer.Ordinal))
        {
            targets.Insert(declared.Count, "");
        }

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

        json.WriteString("readable", session.Store.GetOrLoad(session.Body) is { IsUsable: true } ? "yes" : "no");

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

        var problems = await new DependencyGraphValidator().ValidateAsync(
            new DiagramValidationRequest(TranscriptText.Decode(bytes), Path.GetFileNameWithoutExtension(session.Body), session.Folder, session.Body, null),
            cancellationToken);
        json.WritePropertyName("findings");
        WriteLines(json, [.. problems.Select(problem => $"{Location(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}")]);

        json.WritePropertyName("payloads");
        WriteLines(json, [.. new DependencyGraphElementMapper().Elements(model)
            .Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]);
    }

    private static string Location(DiagramProblemLocation? location) => location switch
    {
        DiagramProblemLineLocation line => $"line {line.Number}",
        DiagramProblemElementLocation element => $"element {JsonSerializer.Serialize(element.Id, TranscriptText.StringOptions)}",
        null => "nowhere",
        _ => location.ToString() ?? "",
    };

    /// <summary>
    /// The scripted edit sequence: the property rows, the menus' actions, the gestures and a direct
    /// move, each run through the real history on a copy of the document, in an order that reaches
    /// refusals too.
    /// </summary>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var script = new Script(json, session, cancellationToken);
        var original = await script.StartAsync();

        var model = session.Model;
        var nodes = model.Elements.Select(element => element.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var node = nodes.FirstOrDefault();
        var other = nodes.Skip(1).FirstOrDefault();
        var relation = model.Relations.Select(candidate => candidate.Id).FirstOrDefault(id => id.Length > 0);
        var placement = GestureIds.RowPlacement(-120.5, 3);

        if (node is not null)
        {
            await script.SetAsync(node, DependencyGraphContextPropertyProvider.LabelProperty, "Parity: renamed, with a colon");
            await script.SetAsync(node, DependencyGraphContextPropertyProvider.XProperty, "not a number");
            await script.SetAsync(node, DependencyGraphContextPropertyProvider.XProperty, "1234.5");
            await script.SetAsync(node, DependencyGraphContextPropertyProvider.RowProperty, "1.5");
            await script.SetAsync(node, DependencyGraphContextPropertyProvider.RowProperty, "-2");
            await script.SetAsync(node, DependencyGraphContextPropertyProvider.FromProperty, "x");
            await script.ExecuteAsync(node, DependencyGraphContextActionProvider.RenameActionId);
            await script.CommitAsync(node, DependencyGraphContextActionProvider.RenameActionId, "Parity renamed");
            await script.ExecuteAsync(node, DependencyGraphContextActionProvider.AddAfterActionId);
            await script.ExecuteAsync(node, DependencyGraphContextActionProvider.AddBelowActionId);
            await script.ExecuteAsync(node, DependencyGraphContextActionProvider.AddElementActionId);
            await script.CommitAsync(node, DependencyGraphContextActionProvider.AddElementActionId, "Committed from a node");
            await script.ExecuteAsync(node, DependencyGraphContextActionProvider.RelabelActionId);
            await script.ExecuteAsync(GestureIds.Relation(node, node), DependencyGraphContextActionProvider.ConnectActionId);
            await script.ExecuteAsync(GestureIds.Relation(node, Unknown), DependencyGraphContextActionProvider.ConnectActionId);
            await script.ExecuteAsync(GestureIds.Relation(node, placement), DependencyGraphContextActionProvider.ConnectActionId);
            await script.ExecuteAsync(GestureIds.Relation(placement, node), DependencyGraphContextActionProvider.ConnectActionId);
            if (other is not null)
            {
                await script.ExecuteAsync(GestureIds.Relation(node, other), DependencyGraphContextActionProvider.ConnectActionId);
                await script.CommandAsync($"move {other} to (300, 4)", new SetDependencyGraphPlacementCommand(session.Body, other, 300, 4));
            }
        }

        if (relation is not null)
        {
            await script.SetAsync(relation, DependencyGraphContextPropertyProvider.LabelProperty, "Parity relabelled");
            await script.SetAsync(relation, DependencyGraphContextPropertyProvider.XProperty, "1");
            await script.ExecuteAsync(relation, DependencyGraphContextActionProvider.RelabelActionId);
            await script.CommitAsync(relation, DependencyGraphContextActionProvider.RelabelActionId, "");
            await script.ExecuteAsync(relation, DependencyGraphContextActionProvider.RemoveActionId);
            await script.CommitAsync(relation, DependencyGraphContextActionProvider.RenameActionId, "Not a node");
            await script.ExecuteAsync(relation, DependencyGraphContextActionProvider.DisconnectActionId);
        }

        await script.ExecuteAsync(GestureIds.RowPlacement(480.25, -1), DependencyGraphContextActionProvider.AddElementActionId);
        await script.ExecuteAsync("", DependencyGraphContextActionProvider.AddElementActionId);
        await script.CommitAsync("", DependencyGraphContextActionProvider.AddElementActionId, "");
        await script.ExecuteAsync(GestureIds.Relation(placement, Unknown), DependencyGraphContextActionProvider.ConnectActionId);
        await script.ExecuteAsync(Unknown, DependencyGraphContextActionProvider.RemoveActionId);
        await script.ExecuteAsync(Unknown, DependencyGraphContextActionProvider.DisconnectActionId);
        await script.ExecuteAsync(Unknown, DependencyGraphContextActionProvider.RelabelActionId);
        await script.CommitAsync(Unknown, DependencyGraphContextActionProvider.RenameActionId, "Nobody");
        await script.ExecuteAsync(Unknown, "parity.no-such-action");

        foreach (var removed in new[] { other, node }.OfType<string>())
        {
            if (await script.ExecuteAsync(removed, DependencyGraphContextActionProvider.RemoveActionId) is "confirm")
            {
                await script.CommitAsync(removed, DependencyGraphContextActionProvider.RemoveActionId, "");
            }
        }

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
            if (directory.Name == "dependency-graph")
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No dependency-graph module folder above {AppContext.BaseDirectory}. That is a failure, not a skip.");
    }

    /// <summary>One document's store, history and providers, readable and read-only.</summary>
    private sealed class Session : IDisposable
    {
        public Session(string folder, string body)
        {
            Folder = folder;
            Body = body;
            History = new HistoryStackStore(new DependencyGraphTestDispatcher(Store));
            var readOnly = new ReadOnlyStore(Store);
            Actions = new DependencyGraphContextActionProvider(History, Store);
            ReadOnlyActions = new DependencyGraphContextActionProvider(History, readOnly);
            Properties = new DependencyGraphContextPropertyProvider(History, Store);
            ReadOnlyProperties = new DependencyGraphContextPropertyProvider(History, readOnly);
        }

        public string Folder { get; }

        public string Body { get; }

        public DependencyGraphDocumentStore Store { get; } = new();

        public HistoryStackStore History { get; }

        public DependencyGraphContextActionProvider Actions { get; }

        public DependencyGraphContextActionProvider ReadOnlyActions { get; }

        public DependencyGraphContextPropertyProvider Properties { get; }

        public DependencyGraphContextPropertyProvider ReadOnlyProperties { get; }

        public DependencyGraphModel Model => Store.GetOrLoad(Body).Model;

        public ContextTarget Target(string elementId) =>
            new(ContextScope.DiagramElement, Body, IsContainer: false, SourceId: default, Folder, default, elementId, Diagram.DependencyGraph.Origin);

        public void Dispose() => History.Dispose();
    }

    /// <summary>The store's own entries, each marked as not parsing: the backend's one read-only case.</summary>
    private sealed class ReadOnlyStore(IDependencyGraphDocumentStore inner) : IDependencyGraphDocumentStore
    {
        public event EventHandler<DependencyGraphDocumentChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public DependencyGraphDocumentEntry GetOrLoad(string path) => inner.GetOrLoad(path) with { Error = "parity: read-only" };

        public DocumentSaveResult Save(string path, DependencyGraphDocumentEntry entry) => inner.Save(path, entry);

        public void Forget(string path) => inner.Forget(path);

        public void BodyDeleted(string path) => inner.BodyDeleted(path);

        public void Reload(string path) => inner.Reload(path);
    }

    /// <summary>Runs the steps of one document's edit sequence and writes each as it goes.</summary>
    private sealed class Script(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        private readonly Dictionary<string, string> _minted = new(StringComparer.Ordinal);
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
            var before = session.Model;
            var answer = await Answer(async () =>
            {
                var result = await session.Properties.SetAsync(session.Target(target), propertyId, value, cancellationToken);
                return result.IsSuccess ? "ok" : $"refused: {result.Error}";
            });
            await RecordAsync($"set {Mask(target)} {propertyId} = {JsonSerializer.Serialize(value, TranscriptText.StringOptions)}", answer, before);
        }

        /// <summary>Runs an action; answers "confirm" when it asked for a confirmation, else null.</summary>
        public async Task<string?> ExecuteAsync(string target, string actionId)
        {
            var before = session.Model;
            ContextExecutionResult? result = null;
            var answer = await Answer(async () => Describe(result = await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken)));
            await RecordAsync($"execute {actionId} on {Mask(target)}", answer, before);
            return result is ContextExecutionRequiresConfirmation ? "confirm" : null;
        }

        public async Task CommitAsync(string target, string actionId, string value)
        {
            var before = session.Model;
            var answer = await Answer(async () =>
            {
                var result = await session.Actions.CommitAsync(session.Target(target), actionId, value, "", cancellationToken);
                return result.Completed ? "ok" : $"refused: {result.Error}";
            });
            await RecordAsync($"commit {actionId} on {Mask(target)} = {JsonSerializer.Serialize(value, TranscriptText.StringOptions)}", answer, before);
        }

        public async Task CommandAsync(string description, ICommand command)
        {
            var before = session.Model;
            var answer = await Answer(async () =>
            {
                var result = await session.History.Get(session.Folder).ExecuteAsync(command, cancellationToken);
                return result.IsSuccess ? "ok" : $"refused: {result.Error}";
            });
            await RecordAsync($"command {Mask(description)}", answer, before);
        }

        /// <summary>
        /// A step's answer, or the exception it threw by its type: what the code does is recorded, a
        /// crash included, so a change to it shows as a change rather than as a transcript that cannot be made.
        /// </summary>
        private static async Task<string> Answer(Func<Task<string>> step)
        {
            try
            {
                return await step();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return $"throws {exception.GetType().Name}";
            }
        }

        private async Task RecordAsync(string step, string answer, DependencyGraphModel before)
        {
            var after = await ReadAsync();
            var known = Ids(before);
            foreach (var id in Ids(session.Model).Where(id => id.Length > 0 && !known.Contains(id) && !_minted.ContainsKey(id)))
            {
                _minted[id] = $"{{minted-{_minted.Count + 1}}}";
            }

            json.WriteStartObject();
            json.WriteString("do", step);
            json.WriteString("answer", Mask(answer));
            TranscriptText.WriteChange(json, Mask(_text), Mask(after));
            json.WriteEndObject();
            _text = after;
        }

        private async Task<string> ReadAsync() =>
            File.Exists(session.Body) ? TranscriptText.Decode(await File.ReadAllBytesAsync(session.Body, cancellationToken)) : "";

        private string Mask(string text) =>
            _minted.Aggregate(text, (masked, entry) => masked.Replace(entry.Key, entry.Value, StringComparison.Ordinal));

        private static HashSet<string> Ids(DependencyGraphModel model) =>
        [
            .. model.Elements.Select(element => element.Id)
                .Concat(model.Relations.Select(relation => relation.Id)),
        ];

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
