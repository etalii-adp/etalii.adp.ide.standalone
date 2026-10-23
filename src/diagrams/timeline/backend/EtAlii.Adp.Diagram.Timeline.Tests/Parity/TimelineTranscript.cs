using System.Globalization;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests.Parity;

/// <summary>
/// The timeline's parity transcript: what today's hand-written code answers for every document of
/// the corpus, frozen as <c>Parity/timeline.transcript.json</c> before any of it is derived from the
/// bundled DISL definition. Every later switch-over must reproduce it byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>: the test project's <c>Fixtures/*.tml</c>, then <c>Fixtures/findings/*.tml</c>
/// (documents that exist to be judged: every finding the rules can raise, interleaved so their order
/// is pinned), the module's <c>examples/*/*.tml</c> and the shipped copies under
/// <c>src/examples/diagrams/timeline/</c>. A document whose bytes equal one recorded earlier is
/// written as <c>sameAs</c> that one.
/// </para>
/// <para>
/// <b>Per document</b>: the context menu of every element and relation id, the empty canvas, a
/// placement, a relation gesture and an unknown id; the property rows of every element and relation;
/// the findings; the payload of every mapped element; and a scripted edit sequence, each step's
/// answer and the hunks it made to the document's text. The sequence drags only an element whose id
/// starts with <c>move</c>, so that adding moves left every other document's record as it was.
/// </para>
/// <para>
/// <b>No read-only variant</b>: no timeline provider reads a read-only state today, so there is
/// nothing a read-only document would answer differently.
/// </para>
/// <para>
/// <b>Minted ids and today's date are masked.</b> An element added by a gesture takes a fresh
/// <see cref="ShortGuid"/>, written as <c>{minted-N}</c> in order of minting. A menu add asks for a
/// begin defaulting to today, written as <c>{today}</c>. The script only grows elements from ones
/// whose times are readable, so no other step depends on the clock.
/// </para>
/// </remarks>
internal static class TimelineTranscript
{
    /// <summary>The checked-in file's name, in this project's <c>Parity</c> folder.</summary>
    private const string FileName = "timeline.transcript.json";

    private const string TestProject = "EtAlii.Adp.Diagram.Timeline.Tests";
    private const string Unknown = "parity:unknown";

    /// <summary>The edit script drags an element whose id starts with this, as nothing else reaches a move.</summary>
    private const string MovedPrefix = "move";

    /// <summary>2026-01-01T00:00:00Z, where the script's placements land.</summary>
    private const double PlacementSeconds = 1_767_225_600;

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(TimelineReflection.Descriptor);

    /// <summary>The module's folder, <c>src/diagrams/timeline</c>.</summary>
    private static string ModuleFolder { get; } = RepositoryPaths.Under("src", "diagrams", "timeline");

    /// <summary>The checked-in transcript.</summary>
    public static string CheckedInPath => Path.Combine(ModuleFolder, "backend", TestProject, "Parity", FileName);

    internal static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The corpus, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    internal static IReadOnlyList<string> Corpus()
    {
        var fixtureFolder = Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures");
        var fixtures = Directory.GetFiles(fixtureFolder, "*.tml");
        var findings = Directory.GetFiles(Path.Combine(fixtureFolder, "findings"), "*.tml");
        var examples = Directory.GetDirectories(Path.Combine(ModuleFolder, "examples")).SelectMany(folder => Directory.GetFiles(folder, "*.tml"));
        var shipped = Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "timeline")).SelectMany(folder => Directory.GetFiles(folder, "*.tml"));
        return [.. Sorted(fixtures), .. Sorted(findings), .. Sorted(examples), .. Sorted(shipped)];

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
            json.WriteString("module", Diagram.Timeline.Origin.Key);
            json.WriteString("generator", $"src/diagrams/timeline/backend/{TestProject}/Parity/TimelineTranscript.cs");
            json.WriteString("regenerate", "ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)");

            // The toolbox is static: it does not read the document, so it is written once.
            json.WriteStartArray("toolbox");
            foreach (var item in TimelineDefinition.Definition.Toolbox)
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

        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.TimelineTranscript", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var body = Path.Combine(folder, Path.GetFileName(source));
            await File.WriteAllBytesAsync(body, bytes, cancellationToken);
            using var session = new Session(folder, body);
            await session.ReadAsync(cancellationToken);
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
        List<string> ids =
        [
            .. model.Elements.Select(element => element.Id)
                .Concat(model.Connections.Select(connection => connection.Id))
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];
        var elementIds = model.Elements.Select(element => element.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var relation = elementIds.Count >= 2 ? GestureIds.Relation(elementIds[0], elementIds[1]) : GestureIds.Relation("a", "b");
        var placement = GestureIds.RowPlacement(PlacementSeconds, 2);
        List<string> targets = [.. ids, "", placement, relation, Unknown];

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

        var problems = await new TimelineValidator().ValidateAsync(
            new DiagramValidationRequest(TranscriptText.Decode(bytes), Path.GetFileNameWithoutExtension(session.Body), ProjectRoot.From(session.Folder), session.Body, null),
            cancellationToken);
        json.WritePropertyName("findings");
        json.WriteLines([.. problems.Select(problem => $"{Location(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}")]);

        json.WritePropertyName("payloads");
        json.WriteLines([.. new TimelineElementMapper().Visible(model, DiagramViewport.Unbounded)
            .Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]);
    }

    private static string Location(DiagramProblemLocation? location) => location switch
    {
        DiagramProblemLineLocation line => $"line {line.Number}",
        DiagramProblemElementLocation element => $"element {TranscriptText.Quoted(element.Id)}",
        null => "nowhere",
        _ => location.ToString(),
    };

    /// <summary>
    /// The scripted edit sequence: the property rows, the menus' actions, their dialogs' commits and
    /// the relation gesture, each run through the real history on a copy of the document, in an order
    /// that reaches refusals too.
    /// </summary>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var script = new Script(json, session, cancellationToken);
        var original = await script.StartAsync();

        var model = session.Model;
        var named = model.Elements.Where(element => element.Id.Length > 0).ToList();
        var period = named.FirstOrDefault(element => element.IsPeriod)?.Id;
        var moment = named.FirstOrDefault(element => !element.IsPeriod)?.Id;
        var readable = named.FirstOrDefault(element => element.Begin.IsReadable && element.End is null or { IsReadable: true })?.Id;
        var other = named.Select(element => element.Id).Distinct(StringComparer.Ordinal).FirstOrDefault(id => id != readable);
        var connection = model.Connections.FirstOrDefault(candidate => candidate.Id.Length > 0)?.Id;

        // A drag, only of the elements whose id asks for one: a document that names none is
        // recorded as it was before moves were scripted (Fixtures/readme.md, dotnet-times.tml).
        foreach (var moved in named.Select(element => element.Id).Where(id => id.StartsWith(MovedPrefix, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal))
        {
            await script.MoveAsync(moved, rows: 1, seconds: 0);
            await script.MoveAsync(moved, rows: 0, seconds: 86_400);
        }

        if (period is not null)
        {
            await script.SetAsync(period, TimelineContextPropertyProvider.LabelProperty, "Parity element");
            await script.SetAsync(period, TimelineContextPropertyProvider.LabelProperty, "Line one\nline two");
            await script.SetAsync(period, TimelineContextPropertyProvider.BeginProperty, "not a time");
            await script.SetAsync(period, TimelineContextPropertyProvider.BeginProperty, "2026-01-05T10:00:00");
            await script.SetAsync(period, TimelineContextPropertyProvider.BeginProperty, "2026-01-05");
            await script.SetAsync(period, TimelineContextPropertyProvider.EndProperty, "2025-12-01");
            await script.SetAsync(period, TimelineContextPropertyProvider.EndProperty, "2026-02-01");
            await script.SetAsync(period, TimelineContextPropertyProvider.RowProperty, "three");
            await script.SetAsync(period, TimelineContextPropertyProvider.RowProperty, "3");
            await script.SetAsync(period, TimelineContextPropertyProvider.FromProperty, "nobody");
            await script.ExecuteAsync(period, TimelineContextActionProvider.GiveEndActionId);
            await script.ExecuteAsync(period, TimelineContextActionProvider.RemoveEndActionId);
        }

        if (moment is not null)
        {
            await script.SetAsync(moment, TimelineContextPropertyProvider.EndProperty, "2026-03-01");
            await script.ExecuteAsync(moment, TimelineContextActionProvider.RemoveEndActionId);
            await script.ExecuteAsync(moment, TimelineContextActionProvider.GiveEndActionId);
            await script.ValidateAsync(moment, TimelineContextActionProvider.GiveEndActionId, "whenever");
            await script.ValidateAsync(moment, TimelineContextActionProvider.GiveEndActionId, "1900-01-01");
            await script.ValidateAsync(moment, TimelineContextActionProvider.GiveEndActionId, "2100-01-01");
            await script.CommitAsync(moment, TimelineContextActionProvider.GiveEndActionId, "2100-01-01");
        }

        if (connection is not null)
        {
            await script.SetAsync(connection, TimelineContextPropertyProvider.LabelProperty, "Parity relation");
            await script.SetAsync(connection, TimelineContextPropertyProvider.FromProperty, "nobody");
            await script.SetAsync(connection, TimelineContextPropertyProvider.BeginProperty, "2026-01-01");
            await script.ExecuteAsync(connection, TimelineContextActionProvider.RelabelActionId);
            await script.CommitAsync(connection, TimelineContextActionProvider.RelabelActionId, "Relabelled");
            await script.CommitAsync(connection, TimelineContextActionProvider.RenameActionId, "Not for a relation");
        }

        if (readable is not null)
        {
            await script.ExecuteAsync(readable, TimelineContextActionProvider.AddAfterActionId);
            await script.ExecuteAsync(readable, TimelineContextActionProvider.AddBelowActionId);
            await script.ExecuteAsync(readable, TimelineContextActionProvider.RenameActionId);
            await script.CommitAsync(readable, TimelineContextActionProvider.RenameActionId, "Parity renamed");
            await script.CommitAsync(readable, TimelineContextActionProvider.DisconnectActionId, "");
        }

        var placement = GestureIds.RowPlacement(PlacementSeconds, 2);
        var added = await script.ExecuteAsync(placement, TimelineContextActionProvider.AddElementActionId);
        await script.ExecuteAsync(GestureIds.RowPlacement(PlacementSeconds + 86_400, 4), TimelineContextActionProvider.AddMomentActionId);
        await script.ExecuteAsync("", TimelineContextActionProvider.AddElementActionId);
        await script.ExecuteAsync("", TimelineContextActionProvider.AddMomentActionId);
        await script.CommitAsync("", TimelineContextActionProvider.AddElementActionId, "2026-02-01");
        await script.CommitAsync(readable ?? "", TimelineContextActionProvider.AddMomentActionId, "2026-02-02T08:30:00");
        await script.CommitAsync("", TimelineContextActionProvider.AddMomentActionId, "garbage");

        if (readable is not null)
        {
            if (other is not null)
            {
                await script.ExecuteAsync(GestureIds.Relation(readable, other), TimelineContextActionProvider.ConnectActionId);
            }

            await script.ExecuteAsync(GestureIds.Relation(readable, GestureIds.RowPlacement(PlacementSeconds + 864_000, 6)), TimelineContextActionProvider.ConnectActionId);
            await script.ExecuteAsync(GestureIds.Relation(GestureIds.RowPlacement(PlacementSeconds - 864_000, 7), readable), TimelineContextActionProvider.ConnectActionId);
            await script.ExecuteAsync(GestureIds.Relation(GestureIds.RowPlacement(PlacementSeconds, 8), Unknown), TimelineContextActionProvider.ConnectActionId);
            await script.ExecuteAsync(GestureIds.Relation(Unknown, readable), TimelineContextActionProvider.ConnectActionId);
        }

        if (added is not null)
        {
            await script.SetAsync(added, TimelineContextPropertyProvider.BeginProperty, "2026-01-02");
        }

        await script.ExecuteAsync(Unknown, TimelineContextActionProvider.RelabelActionId);
        await script.ExecuteAsync(Unknown, TimelineContextActionProvider.RemoveActionId);
        await script.CommitAsync(Unknown, TimelineContextActionProvider.RenameActionId, "Nobody");
        await script.SetAsync(Unknown, TimelineContextPropertyProvider.LabelProperty, "Nobody");

        if (connection is not null)
        {
            await script.ExecuteAsync(connection, TimelineContextActionProvider.DisconnectActionId);
        }

        foreach (var removed in new[] { readable, moment, period }.OfType<string>().Distinct(StringComparer.Ordinal))
        {
            if (await script.ExecuteAsync(removed, TimelineContextActionProvider.RemoveActionId) is "confirm")
            {
                await script.CommitAsync(removed, TimelineContextActionProvider.RemoveActionId, "");
            }
        }

        await script.ExecuteAsync("", TimelineContextActionProvider.ArrangeActionId);

        await script.EndAsync(original);
    }

    /// <summary>One document's store, history and providers.</summary>
    private sealed class Session : IDisposable
    {
        public Session(string folder, string body)
        {
            Folder = folder;
            Body = body;
            History = TestHistory.For(s => { s.AddTimeline(); s.AddSingleton<ITimelineDocumentStore>(Store); });
            Actions = new TimelineContextActionProvider(History, Store);
            Properties = new TimelineContextPropertyProvider(History, Store);
        }

        public string Folder { get; }

        public string Body { get; }

        private TimelineDocumentStore Store { get; } = new(new FileWriter());

        public IHistoryStackStore History { get; }

        public TimelineContextActionProvider Actions { get; }

        public TimelineContextPropertyProvider Properties { get; }

        /// <summary>Opens the document, as the diagram's first open does; the readings and edits read what it holds.</summary>
        public async Task ReadAsync(CancellationToken cancellationToken) => await Store.GetOrLoadAsync(Body, cancellationToken);

        public TimelineModel Model => Store.Get(Body)!.Model;

        public ContextTarget Target(string elementId) =>
            ContextTarget.ForElement(Body, elementId, ProjectRoot.From(Folder), origin: Diagram.Timeline.Origin);

        public void Dispose() => (History as IDisposable)?.Dispose();
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

            var stack = session.History.Get(ProjectRoot.From(session.Folder));
            var undone = 0;
            var thrown = "";
            try
            {
                while (stack.CanUndo && (await stack.UndoAsync(cancellationToken)).IsSuccess)
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

            await RecordAsync($"set {Mask(target)} {propertyId} = {TranscriptText.Quoted(value)}", answer, mints: false);
        }

        /// <summary>
        /// Drags an element by <paramref name="rows"/> rows and <paramref name="seconds"/> seconds from
        /// where it is drawn, as the session turns a drop into a placement: the mapper's position, the
        /// mapper's placement of the new one, one command.
        /// </summary>
        public async Task MoveAsync(string target, int rows, double seconds)
        {
            var model = session.Model;
            var element = model.Elements.First(candidate => string.Equals(candidate.Id, target, StringComparison.Ordinal));
            var drawn = new TimelineElementMapper().Visible(model, DiagramViewport.Unbounded).First(candidate => string.Equals(candidate.Id, target, StringComparison.Ordinal));
            (string begin, string? end, int row) = TimelineElementMapper.Placement(element, drawn.X + seconds, drawn.Y + (rows * TimelineRows.Height));
            string answer;
            try
            {
                var result = await session.History.Get(ProjectRoot.From(session.Folder)).ExecuteAsync(new SetTimelinePlacementCommand(session.Body, target, begin, end, row), cancellationToken);
                answer = result.IsSuccess ? "ok" : $"refused: {result.Error}";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"move {Mask(target)} by {rows} rows and {seconds.ToString(CultureInfo.InvariantCulture)} seconds", answer, mints: false);
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

            await RecordAsync($"validate {actionId} on {Mask(target)} = {TranscriptText.Quoted(value)}", answer, mints: false);
        }

        /// <summary>Runs an action; answers "confirm" when it asked for a confirmation, else the minted id of an add, else null.</summary>
        public async Task<string?> ExecuteAsync(string target, string actionId)
        {
            var before = session.Model;
            ContextExecutionResult result;
            try
            {
                result = await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RecordAsync($"execute {actionId} on {Mask(target)}", Thrown(exception), mints: true, before);
                return null;
            }

            var minted = await RecordAsync($"execute {actionId} on {Mask(target)}", Describe(result), mints: true, before);
            return result is ContextExecutionRequiresConfirmation ? "confirm" : minted;
        }

        public async Task CommitAsync(string target, string actionId, string value)
        {
            var before = session.Model;
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

            await RecordAsync($"commit {actionId} on {Mask(target)} = {TranscriptText.Quoted(value)}", answer, mints: true, before);
        }

        /// <summary>
        /// A step today's code throws on, recorded rather than ending the transcript: what it throws is
        /// the answer a user would meet, and the steps after it still run.
        /// </summary>
        private static string Thrown(Exception exception) => $"throws {exception.GetType().Name}: {exception.Message}";

        private async Task<string?> RecordAsync(string step, string answer, bool mints, TimelineModel? before = null)
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
            json.WriteChange(Mask(_text), Mask(after));
            json.WriteEndObject();
            _text = after;
            return minted;
        }

        private async Task<string> ReadAsync() => TranscriptText.Decode(await File.ReadAllBytesAsync(session.Body, cancellationToken));

        private string Mask(string text) =>
            _minted.Aggregate(text, (masked, entry) => masked.Replace(entry.Key, entry.Value, StringComparison.Ordinal));

        private static HashSet<string> Ids(TimelineModel model) =>
        [
            .. model.Elements.Select(element => element.Id)
                .Concat(model.Connections.Select(connection => connection.Id)),
        ];

        private static string Describe(ContextExecutionResult result) => result switch
        {
            ContextExecutionCompleted => "ok",
            ContextExecutionFailed failed => $"refused: {failed.Message}",
            ContextExecutionRequiresInput input => $"asks: {(input.Request.Title.StartsWith("Add ", StringComparison.Ordinal) ? MaskToday(input.Request.ToString()) : input.Request.ToString())}",
            ContextExecutionRequiresConfirmation confirmation => $"confirms: {confirmation.Request}",
            _ => result.ToString(),
        };

        /// <summary>A menu add's default begin is today; the transcript writes it as <c>{today}</c>.</summary>
        private static string MaskToday(string text) =>
            text.Replace(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "{today}", StringComparison.Ordinal);
    }
}
