using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests.Parity;

/// <summary>
/// The four parity transcripts of the RDF module: what today's hand-written code answers for every
/// document of the corpus under each reading - the RDF graph, OWL, SHACL and SKOS - frozen as
/// <c>Parity/&lt;reading&gt;.transcript.json</c> before any of it is derived from a bundled DISL definition
/// or read through a plugin (rdf-disl-fbl task 1). Every later task must reproduce them byte for byte,
/// or change them only where that specification names the change.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b> is <see cref="RdfCorpus"/>: every document in all four transcripts, so an entry one
/// reading shows under another is recorded. A document whose bytes equal one recorded earlier is written
/// as <c>sameAs</c> that one.
/// </para>
/// <para>
/// <b>Per document and reading</b>: the context menu and the property rows of every drawn element (of the
/// first 200 on the three largest examples, Requirement 1.1 as amended), the
/// empty id, a placement, a connect gesture and an unknown id; the findings; the payload of every drawn
/// element; and a scripted edit sequence on a copy of the document, opened under that reading's
/// registration when the folder holds one and bare otherwise, then one undo of everything.
/// </para>
/// <para>
/// <b>The edit script runs every action the menus offer</b> on the first drawn resource, the first drawn
/// edge, the empty canvas, a placement and a connect gesture, removals last, after a drag of the first
/// resource and a set of each of its editable rows. A prompt is answered with its own value and
/// <c>Parity</c> after it, or, when it is empty, with a prefix declaration or an absolute IRI, and a
/// confirmation is confirmed. So every command the menus can reach runs at least once, on every document
/// where a menu offers it, without the script naming the commands.
/// </para>
/// <para>
/// <b>Minted ids are masked</b>: a 25-character base-36 token the original text does not hold is written
/// as <c>{minted-N}</c> in order of appearance, so a fresh ShortGuid does not change the transcript.
/// </para>
/// </remarks>
internal static partial class RdfTranscript
{
    private const string Unknown = "parity:unknown";
    private const string TestProject = "EtAlii.Adp.Diagram.Rdf.Tests";

    /// <summary>How many drawn elements' menus and rows a capped document records (Requirement 1.1 as amended).</summary>
    private const int CappedTargets = 200;

    /// <summary>
    /// The three largest examples, on which the menus and the rows are recorded for the first
    /// <see cref="CappedTargets"/> drawn elements only, and the payloads and findings in full (Requirement 1.1,
    /// amended by the user's ruling in chat of 2026-10-22).
    /// </summary>
    private static readonly string[] _capped = ["business-economics.ttl", "geographic-names.ttl", "laureates.ttl"];

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(RdfReflection.Descriptor, ShaclReflection.Descriptor, SkosReflection.Descriptor);

    /// <summary>The four readings, each with its transcript's file name.</summary>
    public static IReadOnlyList<RdfTranscriptReading> Readings { get; } =
    [
        new("rdf", ServiceCollectionAddRdfExtension.RdfOrigin, new RdfToolboxProvider(ServiceCollectionAddRdfExtension.RdfOrigin), new RdfValidator(ServiceCollectionAddRdfExtension.RdfOrigin)),
        new("owl", ServiceCollectionAddRdfExtension.OwlOrigin, new OwlToolboxProvider(ServiceCollectionAddRdfExtension.OwlOrigin), new OwlValidator(ServiceCollectionAddRdfExtension.OwlOrigin)),
        new("shacl", ServiceCollectionAddShaclExtension.ShaclOrigin, new ShaclToolboxProvider(ServiceCollectionAddShaclExtension.ShaclOrigin), new ShaclValidator(ServiceCollectionAddShaclExtension.ShaclOrigin)),
        new("skos", ServiceCollectionAddSkosExtension.SkosOrigin, new SkosToolboxProvider(ServiceCollectionAddSkosExtension.SkosOrigin), new SkosValidator(ServiceCollectionAddSkosExtension.SkosOrigin)),
    ];

    /// <summary>The checked-in transcript of <paramref name="reading"/>.</summary>
    public static string CheckedInPath(RdfTranscriptReading reading) => Path.Combine(RdfCorpus.ParityFolder, $"{reading.Name}.transcript.json");

    /// <summary>The whole transcript of <paramref name="reading"/>, as the bytes the checked-in file must hold.</summary>
    public static async Task<byte[]> GenerateAsync(RdfTranscriptReading reading, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await using (var json = new Utf8JsonWriter(stream, TranscriptText.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("module", reading.Origin.Key);
            json.WriteString("generator", $"src/diagrams/rdf/backend/{TestProject}/Parity/RdfTranscript.cs");
            json.WriteString("regenerate", "ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)");

            // The toolbox is static: it does not read the document, so it is written once.
            json.WriteStartArray("toolbox");
            foreach (var item in reading.Toolbox.Items)
            {
                json.WriteStringValue($"{item.Id} | {item.Label} | {item.Icon} | drops {item.DropActionId} | {item.Description}");
            }

            json.WriteEndArray();

            json.WriteStartArray("documents");
            var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string relative, string? registration) in RdfCorpus.Cases(reading.Origin.Key))
            {
                await WriteDocumentAsync(json, reading, relative, registration, recorded, cancellationToken);
            }

            json.WriteEndArray();
            json.WriteEndObject();
            await json.FlushAsync(cancellationToken);
        }

        stream.Write("\r\n"u8);
        return stream.ToArray();
    }

    private static async Task WriteDocumentAsync(Utf8JsonWriter json, RdfTranscriptReading reading, string relative, string? registration, Dictionary<string, string> recorded, CancellationToken cancellationToken)
    {
        var source = Path.Combine(RdfCorpus.SourceFolder, relative);
        var exists = File.Exists(source);
        var bytes = exists ? await File.ReadAllBytesAsync(source, cancellationToken) : [];

        // Two copies are the same document only with the same registration under this reading.
        var sha = TranscriptText.Sha256([.. bytes, .. registration is null ? [] : await File.ReadAllBytesAsync(registration, cancellationToken)]);

        json.WriteStartObject();
        json.WriteString("document", relative);
        if (!exists)
        {
            json.WriteBoolean("bodyMissing", true);
        }

        json.WriteString("registration", registration is null ? null : Path.GetFileName(registration));
        json.WriteString("sha256", sha);
        if (recorded.TryGetValue(sha, out var first))
        {
            json.WriteString("sameAs", first);
            json.WriteEndObject();
            return;
        }

        recorded[sha] = relative;

        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.RdfTranscript", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var body = Path.Combine(folder, Path.GetFileName(source));
            if (exists)
            {
                await File.WriteAllBytesAsync(body, bytes, cancellationToken);
            }

            string? copied = null;
            if (registration is not null)
            {
                copied = Path.Combine(folder, Path.GetFileName(registration));
                File.Copy(registration, copied);
            }

            await using var session = new Session(folder, body, copied, reading);
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
        var drawn = await session.DrawnAsync(cancellationToken);
        var ids = drawn.Select(element => element.Id).Distinct(StringComparer.Ordinal).ToList();
        var capped = _capped.Contains(Path.GetFileName(session.Body), StringComparer.Ordinal);
        if (capped)
        {
            json.WriteString("menusAndRows", $"the first {CappedTargets} of {ids.Count} drawn elements (Requirement 1.1 as amended)");
        }
        var resources = ids.Where(id => id.StartsWith("res:", StringComparison.Ordinal)).ToList();
        var relation = resources.Count >= 2 ? GestureIds.Relation(resources[0], resources[1]) : GestureIds.Relation("res:http://example.org/a", "res:http://example.org/b");
        List<string> targets = [.. capped ? ids.Take(CappedTargets) : ids, "", GestureIds.Placement(240, 160), relation, Unknown];

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
        foreach ((string target, _, IReadOnlyList<string> rows) in lines.Where(line => line.Rows.Count > 0))
        {
            json.WriteStartObject();
            json.WriteString("target", target);
            json.WritePropertyName("rows");
            json.WriteLines(rows);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        var problems = await session.Reading.Validator.ValidateAsync(
            new DiagramValidationRequest(TranscriptText.Decode(bytes), Path.GetFileNameWithoutExtension(session.Body), ProjectRoot.From(session.Folder), session.Body, session.Registration),
            cancellationToken);
        json.WritePropertyName("findings");
        json.WriteLines([.. problems.Select(problem => $"{Location(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}")]);

        json.WritePropertyName("payloads");
        json.WriteLines([.. drawn.Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]);
    }

    private static string Location(DiagramProblemLocation? location) => location switch
    {
        DiagramProblemLineLocation line => $"line {line.Number}",
        DiagramProblemElementLocation element => $"element {TranscriptText.Quoted(element.Id)}",
        null => "nowhere",
        _ => location.ToString(),
    };

    /// <summary>The scripted edit sequence: every action the menus offer on a few targets, every editable row, then one undo of everything.</summary>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var script = new Script(json, session, cancellationToken);
        var original = await script.StartAsync();

        var drawn = (await session.DrawnAsync(cancellationToken)).Select(element => element.Id).Distinct(StringComparer.Ordinal).ToList();
        var resource = drawn.FirstOrDefault(id => id.StartsWith("res:", StringComparison.Ordinal));
        var other = drawn.FirstOrDefault(id => id.StartsWith("res:", StringComparison.Ordinal) && id != resource);
        var edge = drawn.FirstOrDefault(id => id.StartsWith("edge:", StringComparison.Ordinal));

        if (resource is not null)
        {
            await script.MoveAsync(resource);
            foreach (var row in await session.Properties.DescribeAsync(session.Target(resource), cancellationToken))
            {
                if (row.ReadOnlyReason is not { Length: > 0 })
                {
                    await script.SetAsync(resource, row.Id, "Parity value");
                }
            }
        }

        List<string> targets = [GestureIds.Placement(240, 160), ""];
        if (resource is not null && other is not null) targets.Add(GestureIds.Relation(resource, other));
        if (edge is not null) targets.Add(edge);
        if (resource is not null) targets.Add(resource);
        foreach (var target in targets)
        {
            await script.RunMenuAsync(target);
        }

        await script.EndAsync(original);
    }

    /// <summary>One document's store, history, providers and diagram session, under one reading.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly IDiagramSession _diagram;

        public Session(string folder, string body, string? registration, RdfTranscriptReading reading)
        {
            Folder = folder;
            Body = body;
            Registration = registration;
            Reading = reading;
            _services = new ServiceCollection()
                .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
                .AddCommands().AddHierarchyCommandHandlers()
                .AddRdf()
                .BuildServiceProvider();
            Store = _services.GetRequiredService<IRdfDocumentStore>();
            History = _services.GetRequiredService<IHistoryStackStore>();
            Actions = new RdfContextActionProvider(History, Store);
            Properties = new RdfContextPropertyProvider(History, Store);
            _diagram = _services.GetServices<IDiagramSessionFactory>()
                .Single(candidate => candidate.Origin == reading.Origin)
                .Open(ShortGuid.NewShortGuid(), ProjectRoot.From(folder), body, registration);
        }

        public string Folder { get; }

        public string Body { get; }

        public string? Registration { get; }

        public RdfTranscriptReading Reading { get; }

        private IRdfDocumentStore Store { get; }

        public IHistoryStackStore History { get; }

        public RdfContextActionProvider Actions { get; }

        public RdfContextPropertyProvider Properties { get; }

        /// <summary>Opens the document, as the diagram's first open does.</summary>
        public async Task ReadAsync(CancellationToken cancellationToken) => await Store.GetOrLoadAsync(Body, cancellationToken);

        /// <summary>What the diagram session sends for the document as it is now: the elements its baseline adds, none when it adds none.</summary>
        public async Task<IReadOnlyList<DiagramElement>> DrawnAsync(CancellationToken cancellationToken) =>
            [.. (await _diagram.BaselineAsync(cancellationToken)).OfType<DiagramAddDelta>().SelectMany(add => add.Elements)];

        /// <summary>A drag of <paramref name="elementId"/> to (<paramref name="x"/>, <paramref name="y"/>), answered by the session; empty when it was recorded.</summary>
        public async Task<string> MoveAsync(string elementId, double x, double y, CancellationToken cancellationToken) => await _diagram.MoveElementToAsync(elementId, x, y, cancellationToken);

        public ContextTarget Target(string elementId) =>
            ContextTarget.ForElement(Body, elementId, ProjectRoot.From(Folder), origin: Reading.Origin);

        public async ValueTask DisposeAsync()
        {
            await _diagram.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    /// <summary>Runs the steps of one document's edit sequence and writes each as it goes.</summary>
    private sealed partial class Script(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        private readonly Dictionary<string, string> _minted = new(StringComparer.Ordinal);
        private string _original = "";
        private string _text = "";
        private string _registration = "";

        public async Task<string> StartAsync()
        {
            _original = _text = await ReadAsync();
            _registration = await ReadRegistrationAsync();
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

            await RecordAsync($"set {Mask(target)} {propertyId} = {TranscriptText.Quoted(value)}", answer);
        }

        /// <summary>A drag of <paramref name="target"/> 40 to the right of where the session draws it.</summary>
        public async Task MoveAsync(string target)
        {
            var drawn = (await session.DrawnAsync(cancellationToken)).First(element => element.Id == target);
            string answer;
            try
            {
                var refusal = await session.MoveAsync(target, drawn.X + 40, drawn.Y, cancellationToken);
                answer = refusal.Length == 0 ? "ok" : $"refused: {refusal}";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"move {Mask(target)} by 40, 0", answer);
        }

        /// <summary>Runs every action <paramref name="target"/>'s menu offers, removals last, each prompt answered and each confirmation confirmed.</summary>
        public async Task RunMenuAsync(string target)
        {
            var groups = await session.Actions.DiscoverAsync(session.Target(target), cancellationToken);
            var actions = groups.SelectMany(group => group.Actions).Where(action => action.Available)
                .OrderBy(action => IsRemoval(action.Id) ? 1 : 0)
                .Select(action => action.Id)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            foreach (var actionId in actions)
            {
                await ExecuteAsync(target, actionId);
            }
        }

        private async Task ExecuteAsync(string target, string actionId)
        {
            ContextExecutionResult result;
            try
            {
                result = await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RecordAsync($"execute {actionId} on {Mask(target)}", Thrown(exception));
                return;
            }

            await RecordAsync($"execute {actionId} on {Mask(target)}", Describe(result));
            switch (result)
            {
                case ContextExecutionRequiresInput input:
                    await CommitAsync(target, actionId, Answer(actionId, input.Request));
                    break;
                case ContextExecutionRequiresConfirmation:
                    await CommitAsync(target, actionId, "");
                    break;
            }
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

        /// <summary>
        /// What a prompt is answered with: its own value with <c>Parity</c> after it, or, for an empty one, a
        /// prefix declaration where it asks for one and an absolute IRI anywhere else, which every term field reads.
        /// </summary>
        private static string Answer(string actionId, ContextInputRequest request) =>
            request.InitialValue.Length > 0 ? $"{request.InitialValue}Parity"
            : actionId == RdfContextActionProvider.AddPrefixActionId ? "parity: http://example.org/parity#"
            : "<http://example.org/parity>";

        private static bool IsRemoval(string actionId) => actionId.Contains("remove", StringComparison.Ordinal) || actionId.Contains("disconnect", StringComparison.Ordinal);

        /// <summary>A step today's code throws on, recorded rather than ending the transcript.</summary>
        private static string Thrown(Exception exception) => $"throws {exception.GetType().Name}: {exception.Message}";

        private async Task RecordAsync(string step, string answer)
        {
            var after = await ReadAsync();
            foreach (Match token in ShortGuid().Matches(after))
            {
                if (!_original.Contains(token.Value, StringComparison.Ordinal) && !_minted.ContainsKey(token.Value))
                {
                    _minted[token.Value] = $"{{minted-{_minted.Count + 1}}}";
                }
            }

            json.WriteStartObject();
            json.WriteString("do", step);
            json.WriteString("answer", Mask(answer));
            json.WriteChange(Mask(_text), Mask(after));
            var registration = await ReadRegistrationAsync();
            if (!string.Equals(registration, _registration, StringComparison.Ordinal))
            {
                json.WriteStartObject("registrationChange");
                json.WriteChange(Mask(_registration), Mask(registration));
                json.WriteEndObject();
            }

            json.WriteEndObject();
            _text = after;
            _registration = registration;
        }

        private async Task<string> ReadAsync() =>
            File.Exists(session.Body) ? TranscriptText.Decode(await File.ReadAllBytesAsync(session.Body, cancellationToken)) : "";

        private async Task<string> ReadRegistrationAsync() =>
            session.Registration is { } registration && File.Exists(registration) ? TranscriptText.Decode(await File.ReadAllBytesAsync(registration, cancellationToken)) : "";

        private string Mask(string text) =>
            _minted.Aggregate(text, (masked, entry) => masked.Replace(entry.Key, entry.Value, StringComparison.Ordinal));

        private static string Describe(ContextExecutionResult result) => result switch
        {
            ContextExecutionCompleted => "ok",
            ContextExecutionFailed failed => $"refused: {failed.Message}",
            ContextExecutionRequiresInput input => $"asks: {input.Request}",
            ContextExecutionRequiresConfirmation confirmation => $"confirms: {confirmation.Request}",
            _ => result.ToString(),
        };

        [GeneratedRegex("\\b[0-9a-z]{25}\\b")]
        private static partial Regex ShortGuid();
    }
}

/// <summary>One reading the transcripts are recorded under: its origin, toolbox and validator.</summary>
/// <param name="Name">The transcript's name: <c>rdf</c>, <c>owl</c>, <c>shacl</c> or <c>skos</c>.</param>
internal sealed record RdfTranscriptReading(string Name, ToolOrigin Origin, IDiagramToolboxProvider Toolbox, IDiagramValidator Validator)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}
