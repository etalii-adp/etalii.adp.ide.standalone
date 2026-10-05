using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// The hype cycle graph's parity transcript: what today's hand-written code answers for every
/// document of the corpus, frozen as <c>Parity/ghg.transcript.json</c> before any of it is derived
/// from the bundled DISL definition. Every later switch-over must reproduce it byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>: the test project's <c>Fixtures/*.ghg</c>, the module's <c>examples/*/*.ghg</c>
/// and the shipped copies under <c>src/examples/diagrams/gartner-hype-cycle-graph/</c>. A document
/// whose bytes equal one recorded earlier is written as <c>sameAs</c> that one, so the shipped copies
/// cost a line each while they stay identical, and show in full the moment one drifts.
/// </para>
/// <para>
/// <b>Per document</b>: the context menu of every element id, the empty canvas, a placement, a
/// relation gesture and an unknown id, each for a readable and for a read-only document; the property
/// rows of every element (of a sample, in a document of more than <see cref="FullRowsUpTo"/>
/// elements); the findings; the payload of every mapped element; and a scripted edit sequence, each
/// step's answer and the hunks it made to the document's text (Arrange skipped above
/// <see cref="ArrangeUpTo"/> elements).
/// </para>
/// <para>
/// <b>Read-only is the store's unreadable entry</b>, the one read-only case the backend has: the same
/// model, carrying a reason it could not be read, through every provider unchanged.
/// </para>
/// <para>
/// <b>Minted ids are masked.</b> An element added at a placement takes a fresh <see cref="ShortGuid"/>,
/// which the transcript writes as <c>{minted-N}</c> in order of minting, in the splices, the answers
/// and the hashes alike. Every later step addresses it by the real id.
/// </para>
/// </remarks>
internal static class GhgTranscript
{
    /// <summary>The checked-in file's name, in this project's <c>Parity</c> folder.</summary>
    public const string FileName = "ghg.transcript.json";

    /// <summary>A document with more elements than this has its property rows sampled rather than listed for every element.</summary>
    public const int FullRowsUpTo = 40;

    /// <summary>
    /// A document with more elements than this skips the Arrange step of its edit sequence: arranging
    /// technology-trends' 518 entries takes about a minute, which a test run cannot spend on every
    /// gate. Every other document of the corpus is arranged.
    /// </summary>
    public const int ArrangeUpTo = 200;

    private const string TestProject = "EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests";
    private const string Unknown = "parity:unknown";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(GartnerHypecycleGraphReflection.Descriptor);

    /// <summary>The module's folder, <c>src/diagrams/gartner-hype-cycle-graph</c>.</summary>
    public static string ModuleFolder { get; } = FindModuleFolder();

    /// <summary>The checked-in transcript.</summary>
    public static string CheckedInPath => Path.Combine(ModuleFolder, "backend", TestProject, "Parity", FileName);

    private static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The corpus, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    public static IReadOnlyList<string> Corpus()
    {
        static IEnumerable<string> Sorted(IEnumerable<string> paths) =>
            paths.Select(path => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/')).Order(StringComparer.Ordinal);

        var fixtures = Directory.GetFiles(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures"), "*.ghg");
        var examples = Directory.GetDirectories(Path.Combine(ModuleFolder, "examples")).SelectMany(folder => Directory.GetFiles(folder, "*.ghg"));
        var shipped = Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "gartner-hype-cycle-graph")).SelectMany(folder => Directory.GetFiles(folder, "*.ghg"));
        return [.. Sorted(fixtures), .. Sorted(examples), .. Sorted(shipped)];
    }

    /// <summary>The whole transcript, as the bytes the checked-in file must hold.</summary>
    public static async Task<byte[]> GenerateAsync(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await using (var json = new Utf8JsonWriter(stream, TranscriptText.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("module", Diagram.HypeCycleGraph.Origin.Key);
            json.WriteString("generator", $"src/diagrams/gartner-hype-cycle-graph/backend/{TestProject}/Parity/GhgTranscript.cs");
            json.WriteString("regenerate", "ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)");

            // The toolbox is static: it does not read the document, so it is written once.
            json.WriteStartArray("toolbox");
            foreach (var item in new GhgToolboxProvider().Items)
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

        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.GhgTranscript", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var body = Path.Combine(folder, Path.GetFileName(source));
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
        var model = session.Store.GetOrLoad(session.Body).Model;
        List<string> elements =
        [
            .. model.Trends.Select(trend => trend.Id)
                .Concat(model.Triggers.Select(trigger => trigger.Id))
                .Concat(model.Notes.Select(note => note.Id))
                .Concat(model.Influences.Select(influence => influence.Id))
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];
        var trendIds = model.Trends.Select(trend => trend.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var relation = trendIds.Count >= 2 ? GestureIds.Relation(trendIds[0], trendIds[1]) : GestureIds.Relation("a", "b");
        List<string> targets = [.. elements, "", GestureIds.Placement(240, 160), relation, Unknown];

        var sampled = elements.Count > FullRowsUpTo;
        var withRows = sampled ? RowSample(model) : [.. elements, GestureIds.Placement(240, 160), Unknown];

        var menus = new List<IReadOnlyList<string>>();
        var choices = new List<IReadOnlyList<string>>();
        var lines = new List<(string Target, int Menu, int MenuReadOnly, IReadOnlyList<string>? Rows, IReadOnlyList<string>? RowsReadOnly)>();
        foreach (var target in targets)
        {
            var menu = MenuIndex(menus, TranscriptText.MenuLines(await session.Actions.DiscoverAsync(session.Target(target), cancellationToken)));
            var menuReadOnly = MenuIndex(menus, TranscriptText.MenuLines(await session.ReadOnlyActions.DiscoverAsync(session.Target(target), cancellationToken)));
            IReadOnlyList<string>? rows = null;
            IReadOnlyList<string>? rowsReadOnly = null;
            if (withRows.Contains(target, StringComparer.Ordinal))
            {
                rows = [.. (await session.Properties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => TranscriptText.RowLine(row, choices))];
                rowsReadOnly = [.. (await session.ReadOnlyProperties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => TranscriptText.RowLine(row, choices))];
            }

            lines.Add((target, menu, menuReadOnly, rows, rowsReadOnly));
        }

        json.WriteBoolean("rowsSampled", sampled);

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
        foreach ((string target, _, _, IReadOnlyList<string>? rows, IReadOnlyList<string>? rowsReadOnly) in lines)
        {
            if (rows is null)
            {
                continue;
            }

            json.WriteStartObject();
            json.WriteString("target", target);
            json.WritePropertyName("rows");
            WriteLines(json, rows);
            if (TranscriptText.ReadOnlyVariant(rows, rowsReadOnly!) is { } same)
            {
                json.WriteString("rowsReadOnly", same);
            }
            else
            {
                json.WritePropertyName("rowsReadOnly");
                WriteLines(json, rowsReadOnly!);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();

        var problems = await new GhgValidator().ValidateAsync(
            new DiagramValidationRequest(TranscriptText.Decode(bytes), Path.GetFileNameWithoutExtension(session.Body), session.Folder, session.Body, null),
            cancellationToken);
        json.WritePropertyName("findings");
        WriteLines(json, [.. problems.Select(problem => $"line {(problem.Location as DiagramProblemLineLocation)?.Number} | {problem.Severity} | {problem.RuleId} | {problem.Message}")]);

        json.WritePropertyName("payloads");
        WriteLines(json, [.. new GhgElementMapper().Visible(model, DiagramViewport.Unbounded)
            .Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]);
    }

    /// <summary>
    /// The elements whose rows a large document records: per kind the first two and the last, and
    /// the first trend with a dragged boundary and the first showing fewer than four phases.
    /// </summary>
    private static List<string> RowSample(GhgModel model)
    {
        static IEnumerable<string> Ends(IEnumerable<string> ids)
        {
            var list = ids.Where(id => id.Length > 0).ToList();
            return list.Take(2).Concat(list.Skip(2).TakeLast(1));
        }

        return
        [
            .. Ends(model.Trends.Select(trend => trend.Id))
                .Concat(model.Trends.Where(trend => trend.DraggedEnds.Any(end => end is not null)).Select(trend => trend.Id).Take(1))
                .Concat(model.Trends.Where(trend => trend.VisiblePhases < GhgPhases.Count).Select(trend => trend.Id).Take(1))
                .Concat(Ends(model.Triggers.Select(trigger => trigger.Id)))
                .Concat(Ends(model.Notes.Select(note => note.Id)))
                .Concat(Ends(model.Influences.Select(influence => influence.Id)))
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The scripted edit sequence: the property rows, the menus' actions and a direct move, each run
    /// through the real history on a copy of the document, in an order that reaches refusals too.
    /// </summary>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var script = new Script(json, session, cancellationToken);
        var original = await script.StartAsync();

        var model = session.Model;
        var trend = model.Trends.FirstOrDefault(candidate => candidate.Id.Length > 0)?.Id;
        var otherTrend = model.Trends.Where(candidate => candidate.Id.Length > 0).Select(candidate => candidate.Id).Distinct(StringComparer.Ordinal).Skip(1).FirstOrDefault();
        var trigger = model.Triggers.FirstOrDefault(candidate => candidate.Id.Length > 0)?.Id;
        var note = model.Notes.FirstOrDefault(candidate => candidate.Id.Length > 0)?.Id;
        var influence = model.Influences.FirstOrDefault(candidate => candidate.Id.Length > 0)?.Id;

        if (trend is not null)
        {
            await script.SetAsync(trend, GhgContextPropertyProvider.NameProperty, "Parity trend");
            await script.SetAsync(trend, GhgContextPropertyProvider.DescriptionProperty, "Line one\nline two");
            await script.SetAsync(trend, GhgContextPropertyProvider.TagsProperty, "parity, history");
            await script.SetAsync(trend, GhgContextPropertyProvider.StartProperty, "not a month");
            await script.SetAsync(trend, GhgContextPropertyProvider.StopProperty, "2101-13");
            await script.SetAsync(trend, GhgContextPropertyProvider.PhasesProperty, "Peak and Trough");
            await script.SetAsync(trend, GhgContextPropertyProvider.PhasesProperty, "Everything");
            if (session.Model.Trends.FirstOrDefault(candidate => candidate.Id == trend) is { Start: { } start })
            {
                await script.SetAsync(trend, GhgContextPropertyProvider.BoundaryProperties[0], GhgScale.FormatMonth(start + 1));
            }

            await script.ExecuteAsync(trend, GhgContextActionProvider.EvenPhasesActionId);
            await script.SetAsync(trend, GhgContextPropertyProvider.FromAttachmentProperty, "slope/top/0.25");
        }

        if (trigger is not null)
        {
            await script.SetAsync(trigger, GhgContextPropertyProvider.DateProperty, "1990-05");
            await script.SetAsync(trigger, GhgContextPropertyProvider.NameProperty, "Parity trigger");
        }

        if (note is not null)
        {
            await script.SetAsync(note, GhgContextPropertyProvider.TextProperty, "Parity note");
            await script.SetAsync(note, GhgContextPropertyProvider.SizeProperty, SetGhgNoteSizeCommand.Format(240, 96));
            await script.SetAsync(note, GhgContextPropertyProvider.SizeProperty, "huge");
        }

        if (influence is not null)
        {
            await script.SetAsync(influence, GhgContextPropertyProvider.FromAttachmentProperty, "slope/top/0.25");
            await script.SetAsync(influence, GhgContextPropertyProvider.ToAttachmentProperty, "bad");
            await script.SetAsync(influence, GhgContextPropertyProvider.DescriptionProperty, "Parity influence");
        }

        var addedTrend = await script.ExecuteAsync(GestureIds.Placement(240, 160), GhgContextActionProvider.AddTrendActionId);
        await script.ExecuteAsync(GestureIds.Placement(480, 40), GhgContextActionProvider.AddTriggerActionId);
        await script.ExecuteAsync(GestureIds.Placement(720, 300), GhgContextActionProvider.AddNoteActionId);
        await script.ExecuteAsync(Unknown, GhgContextActionProvider.AddTrendActionId);

        if (trend is not null)
        {
            await script.ExecuteAsync(GestureIds.Relation(trend, trend), GhgContextActionProvider.ConnectActionId);
            if (addedTrend is not null)
            {
                await script.ExecuteAsync(
                    GhgGestures.Relation(addedTrend, new GhgEnd("peak", "top", 0.5), trend, new GhgEnd("trough", "bottom", 0.5)),
                    GhgContextActionProvider.ConnectActionId);
            }

            await script.ExecuteAsync(trend, GhgContextActionProvider.RenameActionId);
            await script.CommitAsync(trend, GhgContextActionProvider.RenameActionId, "Parity renamed");
        }

        await script.CommitAsync(Unknown, GhgContextActionProvider.RenameActionId, "Nobody");
        await script.ExecuteAsync(Unknown, GhgContextActionProvider.RemoveActionId);

        if (otherTrend is not null)
        {
            await script.CommandAsync($"move {otherTrend} to (300, 200)", new SetGhgPlacementCommand(session.Body, otherTrend, 300, 200));
        }

        if (influence is not null)
        {
            await script.ExecuteAsync(influence, GhgContextActionProvider.DisconnectActionId);
        }

        foreach (var removed in new[] { trend, trigger, note }.OfType<string>())
        {
            if (await script.ExecuteAsync(removed, GhgContextActionProvider.RemoveActionId) is "confirm")
            {
                await script.CommitAsync(removed, GhgContextActionProvider.RemoveActionId, "");
            }
        }

        if (ElementCount(model) <= ArrangeUpTo)
        {
            await script.ExecuteAsync("", GhgContextActionProvider.ArrangeActionId);
        }

        await script.EndAsync(original);
    }

    private static int ElementCount(GhgModel model) => model.Trends.Count + model.Triggers.Count + model.Notes.Count + model.Influences.Count;

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
            if (directory.Name == "gartner-hype-cycle-graph")
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No gartner-hype-cycle-graph folder above {AppContext.BaseDirectory}. That is a failure, not a skip.");
    }

    /// <summary>One document's store, history and providers, readable and read-only.</summary>
    private sealed class Session : IDisposable
    {
        public Session(string folder, string body)
        {
            Folder = folder;
            Body = body;
            History = new HistoryStackStore(new GhgTestDispatcher(Store));
            var readOnly = new ReadOnlyStore(Store);
            Actions = new GhgContextActionProvider(History, Store);
            ReadOnlyActions = new GhgContextActionProvider(History, readOnly);
            Properties = new GhgContextPropertyProvider(History, Store);
            ReadOnlyProperties = new GhgContextPropertyProvider(History, readOnly);
        }

        public string Folder { get; }

        public string Body { get; }

        public GhgDocumentStore Store { get; } = new();

        public HistoryStackStore History { get; }

        public GhgContextActionProvider Actions { get; }

        public GhgContextActionProvider ReadOnlyActions { get; }

        public GhgContextPropertyProvider Properties { get; }

        public GhgContextPropertyProvider ReadOnlyProperties { get; }

        public GhgModel Model => Store.GetOrLoad(Body).Model;

        public ContextTarget Target(string elementId) =>
            new(ContextScope.DiagramElement, Body, IsContainer: false, SourceId: default, Folder, default, elementId, Diagram.HypeCycleGraph.Origin);

        public void Dispose() => History.Dispose();
    }

    /// <summary>The store's own entries, each marked as unreadable: the backend's one read-only case.</summary>
    private sealed class ReadOnlyStore(IGhgDocumentStore inner) : IGhgDocumentStore
    {
        public event EventHandler<GhgDocumentChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public GhgDocumentEntry GetOrLoad(string path) => inner.GetOrLoad(path) with { Unreadable = "parity: read-only" };

        public DocumentSaveResult Save(string path, GhgBody document) => inner.Save(path, document);

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
            var result = await session.Properties.SetAsync(session.Target(target), propertyId, value, cancellationToken);
            await RecordAsync($"set {Mask(target)} {propertyId} = {JsonSerializer.Serialize(value, TranscriptText.StringOptions)}", result.IsSuccess ? "ok" : $"refused: {result.Error}", mints: false);
        }

        /// <summary>Runs an action; answers "confirm" when it asked for a confirmation, else the minted id of an add, else null.</summary>
        public async Task<string?> ExecuteAsync(string target, string actionId)
        {
            var before = session.Model;
            var result = await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken);
            var minted = await RecordAsync($"execute {actionId} on {Mask(target)}", Describe(result), mints: true, before);
            return result is ContextExecutionRequiresConfirmation ? "confirm" : minted;
        }

        public async Task CommitAsync(string target, string actionId, string value)
        {
            var result = await session.Actions.CommitAsync(session.Target(target), actionId, value, "", cancellationToken);
            await RecordAsync(
                $"commit {actionId} on {Mask(target)} = {JsonSerializer.Serialize(value, TranscriptText.StringOptions)}",
                result.Completed ? "ok" : $"refused: {result.Error}",
                mints: false);
        }

        public async Task CommandAsync(string description, ICommand command)
        {
            var result = await session.History.Get(session.Folder).ExecuteAsync(command, cancellationToken);
            await RecordAsync($"command {Mask(description)}", result.IsSuccess ? "ok" : $"refused: {result.Error}", mints: false);
        }

        private async Task<string?> RecordAsync(string step, string answer, bool mints, GhgModel? before = null)
        {
            var after = await ReadAsync();
            string? minted = null;
            if (mints && before is not null)
            {
                var known = Ids(before);
                foreach (var id in Ids(session.Model).Where(id => !known.Contains(id) && !_minted.ContainsKey(id)))
                {
                    _minted[id] = $"{{minted-{_minted.Count + 1}}}";
                    minted ??= id;
                }
            }

            json.WriteStartObject();
            json.WriteString("do", step);
            json.WriteString("answer", Mask(answer));
            TranscriptText.WriteChange(json, Mask(_text), Mask(after));
            json.WriteEndObject();
            _text = after;
            return minted;
        }

        private async Task<string> ReadAsync() => TranscriptText.Decode(await File.ReadAllBytesAsync(session.Body, cancellationToken));

        private string Mask(string text) =>
            _minted.Aggregate(text, (masked, entry) => masked.Replace(entry.Key, entry.Value, StringComparison.Ordinal));

        private static HashSet<string> Ids(GhgModel model) =>
        [
            .. model.Trends.Select(trend => trend.Id)
                .Concat(model.Triggers.Select(trigger => trigger.Id))
                .Concat(model.Notes.Select(note => note.Id))
                .Concat(model.Influences.Select(influence => influence.Id)),
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
