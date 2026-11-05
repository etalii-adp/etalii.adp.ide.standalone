using System.Globalization;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests.Parity;

/// <summary>
/// The SPARQL query diagram's parity transcript: what today's hand-written code answers for every
/// query of the corpus, frozen as <c>Parity/sparql.transcript.json</c> before the parser, the
/// projection or a provider is changed (sparql-disl-fbl, Requirement 1). Every later step must
/// reproduce it byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b> is <see cref="SparqlCorpus"/>'s. A query whose bytes and registration both equal
/// one recorded earlier is written as <c>sameAs</c> that one, so a shipped copy costs a line while it
/// stays identical; the three shipped copies whose registrations carry a <c>layout:</c> block are
/// recorded in full.
/// </para>
/// <para>
/// <b>Once</b>: the module's registrations, the toolbox it answers (empty), and the two refusals that
/// depend on how a session was opened rather than on the query: a move without a registration and a
/// move without a history.
/// </para>
/// <para>
/// <b>Per query</b>: the elements a session delivers at baseline with an unbounded viewport, in
/// delivery order, each with its id, wire type, position and payload; the property rows of every
/// delivered element and of an unknown id; the selection text of every delivered element; the
/// findings with code, severity, line and sentence; what the action provider offers over all of
/// those targets; and the scripted gestures of Requirement 1.1 with each step's answer, whether the
/// query's bytes are unchanged after it (Requirement 1.4) and the registration's text after it.
/// </para>
/// <para>
/// <b>The culture is pinned</b> to the invariant one while the transcript is generated, because the
/// order of the variable nodes follows the current culture (finding A11): a transcript generated on a
/// machine set to another one would differ. That is the one thing pinned, and it is recorded rather
/// than hidden: the <c>cultures</c> section runs the projection over
/// <c>scope-paths-by-culture.rq</c> under two more cultures and writes the node order each gives.
/// It runs the hand-written projection, <see cref="HandWrittenSparql"/>, because it records what
/// finding A11 found in that code; the derived order is the definition's, by code point, and is the
/// same under every culture (<see cref="SparqlDerivedDrawingTests"/>).
/// </para>
/// <para>
/// <b>Every query is read from a copy in a temporary folder</b>, so that a move writes into a
/// registration nobody else reads and nothing is written into the tree. Nothing the module answers
/// names that folder, and <see cref="Mask"/> replaces it all the same, should a sentence ever carry it.
/// </para>
/// </remarks>
internal static class SparqlTranscript
{
    /// <summary>The checked-in file's name, in this project's <c>Parity</c> folder.</summary>
    private const string FileName = "sparql.transcript.json";

    private const string Unknown = "parity:unknown";

    /// <summary>The query whose variable order the <c>cultures</c> section records (finding A11).</summary>
    private const string CultureQuery = "scope-paths-by-culture.rq";

    /// <summary>The cultures that query is projected under besides the invariant one, each ordering its scope paths otherwise.</summary>
    private static readonly string[] _cultures = ["haw-US", "th-TH"];

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromMessages(SparqlVariablePayload.Descriptor);

    private static readonly ToolOrigin _origin = ServiceCollectionAddSparqlExtension.SparqlOrigin;

    /// <summary>The checked-in transcript.</summary>
    public static string CheckedInPath => Path.Combine(SparqlCorpus.TestFolderPath, "Parity", FileName);

    /// <summary>The whole transcript, as the bytes the checked-in file must hold.</summary>
    public static async Task<byte[]> GenerateAsync(CancellationToken cancellationToken)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            return await WriteAsync(cancellationToken);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static async Task<byte[]> WriteAsync(CancellationToken cancellationToken)
    {
        await using var provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddSparql()
            .BuildServiceProvider();

        using var stream = new MemoryStream();
        await using (var json = new Utf8JsonWriter(stream, TranscriptText.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("module", _origin.Key);
            json.WriteString("generator", $"src/diagrams/sparql/backend/EtAlii.Adp.Diagram.Sparql.Tests/Parity/{nameof(SparqlTranscript)}.cs");
            json.WriteString("regenerate", $"{TranscriptHarness.RegenerateVariable}=1 dotnet test --project <this test project> (then review the diff)");
            json.WriteString("culture", "invariant; see cultures");

            json.WritePropertyName("registrations");
            json.WriteLines(Registrations());

            // The toolbox is answered by origin from the providers the module registers, and it registers none.
            json.WritePropertyName("toolbox");
            json.WriteLines([.. provider.GetServices<IDiagramToolboxProvider>()
                .Where(toolbox => toolbox.Origin == _origin)
                .SelectMany(toolbox => toolbox.Items)
                .Select(item => $"{item.Id} | {item.Label} | {item.Icon} | drops {item.DropActionId} | {item.Description}")]);

            json.WriteStartArray("refusals");
            await WriteSessionRefusalsAsync(json, provider, cancellationToken);
            json.WriteEndArray();

            json.WriteStartArray("documents");
            var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var relative in SparqlCorpus.Documents())
            {
                await WriteDocumentAsync(json, provider, relative, recorded, cancellationToken);
            }

            json.WriteEndArray();

            json.WriteStartObject("cultures");
            await WriteCulturesAsync(json, cancellationToken);
            json.WriteEndObject();

            json.WriteEndObject();
            await json.FlushAsync(cancellationToken);
        }

        stream.Write("\r\n"u8);
        return stream.ToArray();
    }

    /// <summary>Every service the module registers, as <c>service =&gt; implementation</c>, in registration order.</summary>
    private static List<string> Registrations() =>
    [
        .. new ServiceCollection().AddSparql().Select(descriptor =>
            $"{descriptor.ServiceType.Name} => {descriptor.ImplementationType?.Name ?? (descriptor.ImplementationFactory is not null ? "factory" : "instance")} ({descriptor.Lifetime})"),
    ];

    /// <summary>A move without a registration and one without a history, asked of <c>groups.rq</c>: neither depends on the query.</summary>
    private static async Task WriteSessionRefusalsAsync(Utf8JsonWriter json, ServiceProvider provider, CancellationToken cancellationToken)
    {
        using var folder = TestFolder.Create(nameof(SparqlTranscript));
        var body = Path.Combine(folder.Path, "groups.rq");
        File.Copy(Path.Combine(SparqlCorpus.TestFolderPath, "Fixtures", "groups.rq"), body);
        var registration = Path.Combine(folder.Path, "query.adp");
        await File.WriteAllTextAsync(registration, BareRegistration("groups.rq"), cancellationToken);
        var documents = provider.GetRequiredService<ISparqlDocumentStore>();
        var mapper = provider.GetRequiredService<SparqlElementMapper>();
        var history = provider.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(folder.Path));

        await using (var session = new SparqlSession(body, registrationPath: null, documents, mapper, history))
        {
            await WriteStepAsync(json, "move var:x to (10, 10) without a registration", body, registration, folder.Path,
                () => session.MoveElementToAsync("var:x", 10, 10, cancellationToken), cancellationToken);
        }

        await using (var session = new SparqlSession(body, registration, documents, mapper, history: null))
        {
            await WriteStepAsync(json, "move var:x to (10, 10) without a history", body, registration, folder.Path,
                () => session.MoveElementToAsync("var:x", 10, 10, cancellationToken), cancellationToken);
        }
    }

    private static async Task WriteDocumentAsync(
        Utf8JsonWriter json, ServiceProvider provider, string relative, Dictionary<string, string> recorded, CancellationToken cancellationToken)
    {
        var source = SparqlCorpus.Full(relative);
        var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
        var sourceRegistration = SparqlCorpus.RegistrationOf(source);
        var registrationText = sourceRegistration is null
            ? BareRegistration(Path.GetFileName(source))
            : TranscriptText.Decode(await File.ReadAllBytesAsync(sourceRegistration, cancellationToken));

        json.WriteStartObject();
        json.WriteString("document", relative);
        json.WriteString("sha256", TranscriptText.Sha256(bytes));
        json.WriteString("registration", sourceRegistration is null
            ? $"none; the copy is given {TranscriptText.Quoted(registrationText)}"
            : $"{Path.GetFileName(sourceRegistration)} {TranscriptText.Quoted(registrationText)}");

        var key = $"{TranscriptText.Sha256(bytes)}|{Path.GetFileName(source)}|{registrationText}";
        if (recorded.TryGetValue(key, out var first))
        {
            json.WriteString("sameAs", first);
            json.WriteEndObject();
            return;
        }

        recorded[key] = relative;

        using (var folder = TestFolder.Create(nameof(SparqlTranscript)))
        {
            var body = Path.Combine(folder.Path, Path.GetFileName(source));
            await File.WriteAllBytesAsync(body, bytes, cancellationToken);
            var registration = Path.Combine(folder.Path, sourceRegistration is null ? "query.adp" : Path.GetFileName(sourceRegistration));
            await File.WriteAllTextAsync(registration, registrationText, cancellationToken);
            await WriteReadingAsync(json, provider, folder.Path, body, registration, cancellationToken);
        }

        json.WriteEndObject();
    }

    private static async Task WriteReadingAsync(
        Utf8JsonWriter json, ServiceProvider provider, string folder, string body, string registration, CancellationToken cancellationToken)
    {
        var documents = provider.GetRequiredService<ISparqlDocumentStore>();
        var mapper = provider.GetRequiredService<SparqlElementMapper>();
        var history = provider.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(folder));

        // What a session delivers at baseline, in delivery order, with an unbounded viewport over the registration's stored positions.
        List<DiagramElement> elements;
        await using (var session = new SparqlSession(body, registration, documents, mapper, history))
        {
            elements = [.. (await session.BaselineAsync(cancellationToken)).OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements)];
        }

        json.WritePropertyName("payloads");
        json.WriteLines([.. elements.Select(element => Mask(TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes), folder))]);

        List<string> targets = [.. elements.Select(element => element.Id), Unknown];
        var properties = new SparqlContextPropertyProvider(documents);
        json.WriteStartArray("rows");
        foreach (var target in targets)
        {
            var rows = await properties.DescribeAsync(Target(body, folder, target), cancellationToken);
            json.WriteStartObject();
            json.WriteString("target", target);
            json.WritePropertyName("rows");
            json.WriteLines([.. rows.Select(row => Mask(row.RowLine(), folder))]);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WritePropertyName("selection");
        json.WriteLines(await SelectionAsync(elements, folder, body, documents, mapper, cancellationToken));

        var text = await File.ReadAllTextAsync(body, cancellationToken);
        var problems = await new SparqlValidator(_origin).ValidateAsync(
            new DiagramValidationRequest(text, Path.GetFileNameWithoutExtension(body), ProjectRoot.From(folder), body, registration),
            cancellationToken);
        json.WritePropertyName("findings");
        json.WriteLines([.. problems.Select(problem => Mask(FindingLine(problem), folder))]);

        var actions = new SparqlContextActionProvider();
        var groups = 0;
        foreach (var target in targets)
        {
            groups += (await actions.DiscoverAsync(Target(body, folder, target), cancellationToken)).Count;
        }

        json.WriteString("actions", $"{targets.Count} targets asked, {groups} groups offered");

        json.WriteStartArray("gestures");
        await WriteGesturesAsync(json, elements, folder, body, registration, documents, mapper, history, cancellationToken);
        json.WriteEndArray();
    }

    /// <summary>
    /// The scripted sequence of Requirement 1.1: a move of a variable, of a region and of a concrete
    /// term, each undone again; the five refused moves; a refused re-parenting; a refused property
    /// write; and a refused action. A kind the query does not draw is written as absent.
    /// </summary>
    private static async Task WriteGesturesAsync(
        Utf8JsonWriter json,
        List<DiagramElement> elements,
        string folder,
        string body,
        string registration,
        ISparqlDocumentStore documents,
        SparqlElementMapper mapper,
        IHistoryStack history,
        CancellationToken cancellationToken)
    {
        var variable = First(elements, element => element.Type == SparqlElementMapper.VariableType && element.Id.StartsWith("var:", StringComparison.Ordinal));
        var region = First(elements, element => element.Type == SparqlElementMapper.RegionType);
        var term = First(elements, element => element.Type == SparqlElementMapper.TermType && !element.Id.StartsWith("sub:", StringComparison.Ordinal));
        var anonymous = First(elements, element => element.Id.StartsWith("anon:", StringComparison.Ordinal));
        var edge = First(elements, element => element.Type == SparqlElementMapper.EdgeType);
        var annotation = First(elements, element => element.Type == SparqlElementMapper.AnnotationType);
        var truncation = First(elements, element => element.Type == SparqlElementMapper.TruncationType);

        await using var session = new SparqlSession(body, registration, documents, mapper, history);
        await session.BaselineAsync(cancellationToken);

        var moved = 0;
        foreach ((string kind, string? id, double x, double y) in (List<(string, string?, double, double)>)
                 [("variable", variable, 320, 200), ("region", region, 640.5, 480), ("concrete term", term, -100, 20.25)])
        {
            if (id is null)
            {
                json.WriteStartObject();
                json.WriteString("step", $"move the first {kind}");
                json.WriteString("answer", $"not asked: the query draws no {kind}");
                json.WriteEndObject();
                continue;
            }

            var answer = await WriteStepAsync(json, $"move {id} to ({Number(x)}, {Number(y)})", body, registration, folder,
                () => session.MoveElementToAsync(id, x, y, cancellationToken), cancellationToken);
            moved += answer.Length == 0 ? 1 : 0;
        }

        for (var undo = 0; undo < moved; undo++)
        {
            await WriteStepAsync(json, "undo", body, registration, folder,
                async () => (await history.UndoAsync(cancellationToken)) is { IsSuccess: false } failed ? failed.Error : "", cancellationToken);
        }

        foreach ((string kind, string? id) in (List<(string, string?)>)
                 [("anonymous variable", anonymous), ("edge", edge), ("annotation", annotation), ("header band", SparqlElementMapper.HeaderId), ("truncation banner", truncation)])
        {
            if (id is null)
            {
                json.WriteStartObject();
                json.WriteString("step", $"move the first {kind}");
                json.WriteString("answer", $"not asked: the query draws no {kind}");
                json.WriteEndObject();
                continue;
            }

            await WriteStepAsync(json, $"move {id} to (10, 10)", body, registration, folder,
                () => session.MoveElementToAsync(id, 10, 10, cancellationToken), cancellationToken);
        }

        var child = variable ?? term ?? SparqlElementMapper.HeaderId;
        var parent = region ?? Unknown;
        await WriteStepAsync(json, $"reparent {child} under {parent}", body, registration, folder,
            () => session.MoveElementAsync(child, parent, 0, cancellationToken), cancellationToken);

        var properties = new SparqlContextPropertyProvider(documents);
        var rowId = (await properties.DescribeAsync(Target(body, folder, child), cancellationToken)).FirstOrDefault()?.Id ?? HandWrittenSparql.QueryFormProperty;
        await WriteStepAsync(json, $"write {rowId} of {child}", body, registration, folder, async () =>
        {
            var result = await properties.SetAsync(Target(body, folder, child), rowId, "Renamed", cancellationToken);
            return result.IsSuccess ? "" : result.Error;
        }, cancellationToken);

        var actions = new SparqlContextActionProvider();
        await WriteStepAsync(json, $"execute action parity:action on {child}", body, registration, folder, async () =>
            await actions.ExecuteAsync(Target(body, folder, child), "parity:action", cancellationToken) switch
            {
                ContextExecutionFailed failed => failed.Message,
                var other => $"not refused: {other.GetType().Name}",
            }, cancellationToken);
    }

    /// <summary>
    /// One step: its answer, whether the query's bytes are the same after it as before it, and the
    /// registration's text after it.
    /// </summary>
    private static async Task<string> WriteStepAsync(
        Utf8JsonWriter json, string step, string body, string registration, string folder, Func<Task<string>> act, CancellationToken cancellationToken)
    {
        var before = await File.ReadAllBytesAsync(body, cancellationToken);
        var answer = await act();
        var after = await File.ReadAllBytesAsync(body, cancellationToken);

        json.WriteStartObject();
        json.WriteString("step", step);
        json.WriteString("answer", answer.Length == 0 ? "accepted" : $"refused: {Mask(answer, folder)}");
        json.WriteBoolean("bodyUnchanged", before.AsSpan().SequenceEqual(after));
        json.WriteString("registration", File.Exists(registration) ? TranscriptText.Decode(await File.ReadAllBytesAsync(registration, cancellationToken)) : "none");
        json.WriteEndObject();
        return answer;
    }

    /// <summary>What selecting each delivered element resolves to: its path and its text, or why it is refused.</summary>
    private static async Task<List<string>> SelectionAsync(
        List<DiagramElement> elements, string folder, string body, ISparqlDocumentStore documents, SparqlElementMapper mapper, CancellationToken cancellationToken)
    {
        var resolver = new SparqlContextSourceResolver(new DiagramFileRouter(new TestDiagramDefinitionCatalog(Diagram.Sparql)), documents, mapper);
        var parent = new ContextResolvedLevel(
            new ContextSource(),
            [Path.GetFileName(body)],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, body, IsContainer: false, SourceId: default, ProjectRoot.From(folder)),
            new ContextLevelDetail(),
            null!);

        var lines = new List<string>();
        foreach (var id in elements.Select(element => element.Id).Append(Unknown))
        {
            var resolution = await resolver.ResolveAsync(
                ShortGuid.NewShortGuid(), ProjectRoot.From(folder), new ContextSource { ElementId = new ElementId { Value = id } }, [], parent, cancellationToken);
            lines.Add(Mask(resolution switch
            {
                ResolvedContextLevel resolved => $"{id} => [{string.Join("/", resolved.Level.RelativePath)}] | {TranscriptText.Quoted(resolved.Level.Detail.Element.Text)}",
                RejectedContextLevel rejected => $"{id} => rejected: {rejected.Reason}",
                _ => $"{id} => {resolution.GetType().Name}",
            }, folder));
        }

        return lines;
    }

    /// <summary>
    /// The variable nodes the hand-written projection gives <c>scope-paths-by-culture.rq</c>, in order,
    /// under the invariant culture and under each of <see cref="_cultures"/> (finding A11).
    /// </summary>
    private static async Task WriteCulturesAsync(Utf8JsonWriter json, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(SparqlCorpus.TestFolderPath, "Fixtures", CultureQuery), cancellationToken);
        json.WriteString("document", CultureQuery);
        var current = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in (string[])["", .. _cultures])
            {
                CultureInfo.CurrentCulture = name.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(name);
                json.WritePropertyName(name.Length == 0 ? "invariant" : name);
                json.WriteLines([.. HandWrittenSparql.Project(SparqlParser.Parse(text)).Nodes
                    .Where(node => node.Kind == SparqlNodeKind.Variable)
                    .Select(node => $"{node.Id} | {node.ScopePath}")]);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
        }
    }

    private static string? First(List<DiagramElement> elements, Func<DiagramElement, bool> predicate) =>
        elements.FirstOrDefault(predicate)?.Id;

    private static string BareRegistration(string bodyName) => $"w3c/sparql\r\nbody: {bodyName}\r\n";

    private static ContextTarget Target(string body, string folder, string elementId) =>
        ContextTarget.ForElement(body, elementId, ProjectRoot.From(folder), origin: _origin);

    private static string FindingLine(DiagramProblem problem)
    {
        var where = problem.Location is DiagramProblemLineLocation line
            ? $"line {line.Number.ToString(CultureInfo.InvariantCulture)}"
            : problem.Location?.ToString() ?? "-";
        return $"{problem.RuleId} | {problem.Severity} | {where} | {TranscriptText.Quoted(problem.Message)}";
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The copy's absolute location, should a sentence carry it: the one thing in a reading that differs per run.</summary>
    private static string Mask(string text, string folder) => text.Replace(folder, "{copy}", StringComparison.Ordinal);
}
