using System.Globalization;
using System.Text;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Layout;
using EtAlii.Adp.Specification.Disl;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.HelmChart.Tests.Parity;

/// <summary>
/// The Helm chart diagram's parity transcript: what today's hand-written code answers for every
/// chart folder of the corpus, frozen as <c>Parity/helm-chart.transcript.json</c> before the reader,
/// the rule set or the property provider is changed (helm-chart-disl-fbl, Requirement 1). Every
/// later step must reproduce it byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>: every chart folder under the test project's <c>Fixtures/</c>, the module's
/// <c>examples/</c> and the shipped copies under <c>src/examples/diagrams/helm-chart/</c>, each in
/// ordinal order. A folder whose files equal one recorded earlier, path for path and byte for byte,
/// is written as <c>sameAs</c> that one, so a shipped copy costs a line while it stays identical
/// and shows in full the moment one file of it drifts. A folder that differs from one recorded
/// earlier only in its registration (the shipped copies carry other stored positions) is written as
/// <c>sameChartAs</c> that one, with only what a registration changes: the delivered elements, the
/// moves and the registration after them.
/// </para>
/// <para>
/// <b>Per chart folder</b>: the elements a session delivers at baseline, in delivery order, each
/// with its id, wire type, position and payload; the computed boxes; the property rows of every
/// node, every relation, the empty id and an unknown one, and the answer to a property write; the
/// findings with code, severity, file, line and sentence, in order; and the session's answers to a
/// reparent, to a move of a node, of a relation and of an unknown id (with a history and without),
/// with the registration's text after those moves.
/// </para>
/// <para>
/// <b>One change was regenerated on purpose</b> (task 6; the ruling of 2026-10-16, Requirement 1.4): where two
/// dependencies of one effective name delivered a <c>Declares</c>, <c>Resolves</c> or <c>Configures</c> twice with one
/// id and the same content, the repeat is no longer delivered, so it is neither an element nor a target of rows here.
/// The relations are listed as the session delivers them (<see cref="HelmElementMapper.Relations"/>).
/// </para>
/// <para>
/// <b>Every folder is read through the binding</b> (task 9): by the module's store, which opens it with the
/// library's folder body and the module's plugin, as the host does. The store is closed before the copy is.
/// </para>
/// <para>
/// <b>No edit sequence over chart files</b> (Requirement 1.5): nothing here writes a chart file,
/// because nothing in the module can. The one write is a move, which core writes into the
/// registration, and that is done in a copy.
/// </para>
/// <para>
/// <b>Every folder is read from a copy in a temporary folder</b>, so that the moves write into a
/// registration nobody else reads, and so that the three empty directories of the
/// <c>empty-directories</c> fixture, which git cannot store, can be made (<see cref="EmptyDirectories"/>).
/// Every path the module reports is relative to the chart folder, so where the copy lies cannot
/// show; <see cref="Mask"/> replaces it all the same, should a sentence ever carry it.
/// </para>
/// <para>
/// <b>Nothing else is masked</b>: nothing the module answers depends on a clock, a random number or
/// the machine. YamlDotNet's sentences stay as they are, which is finding B9 on record.
/// </para>
/// </remarks>
public sealed class HelmTranscript() : TranscriptAdapter("helm-chart", HelmCorpus.TestProject, "helm-chart.transcript.json")
{
    private const string Unknown = "parity:unknown";

    /// <summary>The registration a folder without one is given in its copy: the bare MIME line core writes.</summary>
    private const string BareRegistration = "helm/chart\r\n";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromMessages(Wire.HelmElementPayload.Descriptor);

    /// <summary>
    /// The empty directories a fixture is about, by fixture name: git stores files and no directory
    /// without one, so the generator makes these in the copy. They are what finding B4 is about (a
    /// <c>crds/</c> with no file, a directory under <c>charts/</c> with no <c>Chart.yaml</c>, and a
    /// deeper entry that is a directory), and the fixture's readme lists them.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> EmptyDirectories = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["empty-directories"] = ["crds", "charts/hollow", "charts/holder/charts/empty"],
    };

    /// <summary>The charts recorded in full so far, by the hash of their chart files alone, for <c>sameChartAs</c>.</summary>
    private readonly Dictionary<string, string> _charts = new(StringComparer.Ordinal);

    public override string Origin => Diagram.HelmCharts.Origin.Key;

    /// <summary>None: a Helm chart diagram offers no toolbox, and its transcript has no field for one.</summary>
    protected override IReadOnlyList<ToolboxItemDefinition>? Toolbox => null;

    public override string DocumentsName => "charts";

    public override string DocumentName => "chart";

    /// <summary>Every chart folder, read as its manifest with the registrations: so a folder whose files equal one recorded earlier, path for path and byte for byte, is <c>sameAs</c> it.</summary>
    public override IReadOnlyList<TranscriptDocument> Corpus() =>
        [.. HelmCorpus.Charts().Select(relative => new TranscriptDocument(relative, cancellationToken => ManifestAsync(Path.Combine(SourceFolder, relative), registrations: true, cancellationToken)))];

    public override async Task WriteDocumentAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, string folder, CancellationToken cancellationToken)
    {
        var source = Path.Combine(SourceFolder, document.Name);
        var chartSha = TranscriptText.Sha256(await ManifestAsync(source, registrations: false, cancellationToken));
        var sameChart = _charts.GetValueOrDefault(chartSha);
        if (sameChart is null)
        {
            _charts[chartSha] = document.Name;
        }
        else
        {
            json.WriteString("sameChartAs", sameChart);
        }

        TestFolder.CopyTree(source, folder);
        if (EmptyDirectories.TryGetValue(Path.GetFileName(source), out var empty))
        {
            json.WritePropertyName("emptyDirectoriesMade");
            json.WriteLines(empty);
            foreach (var directory in empty)
            {
                Directory.CreateDirectory(Path.Combine(folder, directory));
            }
        }

        var registration = Directory.GetFiles(folder, "*.adp").Order(StringComparer.Ordinal).FirstOrDefault();
        json.WriteString("registration", registration is null ? $"none; the copy is given {Quoted(BareRegistration)}" : Path.GetFileName(registration));
        if (registration is null)
        {
            registration = Path.Combine(folder, "helm-chart.adp");
            await File.WriteAllTextAsync(registration, BareRegistration, cancellationToken);
        }

        // Services of its own: a history is kept by the copy's folder, so no two charts ever shared one.
        await using var provider = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddHelmCharts().BuildServiceProvider();
        await WriteReadingAsync(json, folder, registration, provider.GetRequiredService<IHistoryStackStore>(), full: sameChart is null, cancellationToken);
    }

    private static async Task WriteReadingAsync(
        Utf8JsonWriter json, string folder, string registration, IHistoryStackStore histories, bool full, CancellationToken cancellationToken)
    {
        using var store = new HelmChartStore();
        var reading = store.GetOrLoad(folder, registration).Current;
        var diagram = reading.Disl.Diagram;

        json.WriteBoolean("isChart", reading.IsChart);

        // What a session delivers at baseline, in delivery order, over the folder's own stored positions.
        List<DiagramElement> elements;
        await using (var session = new HelmSession(folder, registration, store, new HelmElementMapper()))
        {
            elements = [.. (await session.BaselineAsync(cancellationToken)).OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements)];
        }

        json.WritePropertyName("elements");
        json.WriteLines([.. elements.Select(element => Mask(TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes), folder))]);

        if (full)
        {
            await WriteAnswersAsync(json, folder, registration, diagram, store, cancellationToken);
        }

        json.WritePropertyName("moves");
        json.WriteLines(await MovesAsync(folder, registration, store, diagram, histories, cancellationToken));

        json.WritePropertyName("registrationAfterMoves");
        json.WriteLines([.. Lines(await File.ReadAllTextAsync(registration, cancellationToken))]);
    }

    /// <summary>What does not depend on the registration: the computed boxes, the rows, the refused write and the findings.</summary>
    private static async Task WriteAnswersAsync(
        Utf8JsonWriter json, string folder, string registration, DislDiagram diagram, IHelmChartStore store, CancellationToken cancellationToken)
    {
        json.WritePropertyName("boxes");
        json.WriteLines([.. HelmLayout.Compute(diagram).Select(pair => $"{pair.Key} | {Box(pair.Value)}")]);

        var properties = new HelmContextPropertyProvider(store);
        List<string> targets =
        [
            .. diagram.Nodes.Select(node => node.Id),
            .. HelmElementMapper.Relations(diagram).Select(relation => relation.Id),
            "",
            Unknown,
        ];

        json.WriteStartArray("rows");
        foreach (var target in targets)
        {
            var rows = await properties.DescribeAsync(Target(registration, folder, target), cancellationToken);
            json.WriteStartObject();
            json.WriteString("target", target);
            json.WritePropertyName("rows");
            json.WriteLines([.. rows.Select(row => Mask(row.RowLine(), folder))]);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        var write = await properties.SetAsync(Target(registration, folder, targets[0]), "helm.name", "Renamed", cancellationToken);
        json.WriteString("write", write.IsSuccess ? "ok" : $"refused: {Mask(write.Error, folder)}");

        var problems = await new HelmValidator().ValidateAsync(
            new DiagramValidationRequest("helm/chart", Path.GetFileNameWithoutExtension(registration), ProjectRoot.From(folder), registration, registration) { SubjectFolder = folder },
            cancellationToken);
        json.WritePropertyName("findings");
        json.WriteLines([.. problems.Select(problem => Mask(FindingLine(problem), folder))]);
    }

    /// <summary>
    /// The session's answers to a reparent and to a move of a node, of a relation and of an unknown
    /// id: first without a history, which makes the diagram read-only, then with the project's history.
    /// </summary>
    private static async Task<List<string>> MovesAsync(
        string folder, string registration, IHelmChartStore store, DislDiagram diagram, IHistoryStackStore histories, CancellationToken cancellationToken)
    {
        var node = diagram.Nodes.FirstOrDefault()?.Id;
        var relation = HelmElementMapper.Relations(diagram).FirstOrDefault()?.Id;
        List<(string What, string? Id)> moves = [("node", node), ("relation", relation), ("unknown id", Unknown)];

        var lines = new List<string>();
        foreach (var history in new[] { false, true })
        {
            await using var session = new HelmSession(folder, registration, store, new HelmElementMapper(), history ? histories.Get(ProjectRoot.From(folder)) : null);
            var label = history ? "with a history" : "without a history";
            lines.Add($"{label}: reparent {node ?? Unknown} under {Unknown} => {Answer(await session.MoveElementAsync(node ?? Unknown, Unknown, 0, cancellationToken))}");
            foreach ((string what, string? id) in moves)
            {
                lines.Add(id is null
                    ? $"{label}: move a {what} => there is none"
                    : $"{label}: move {what} {id} to (10.5, -20.25) => {Answer(await session.MoveElementToAsync(id, 10.5, -20.25, cancellationToken))}");
            }
        }

        return [.. lines.Select(line => Mask(line, folder))];

        static string Answer(string answer) => answer.Length == 0 ? "accepted" : $"refused: {answer}";
    }

    private static ContextTarget Target(string registration, string folder, string elementId) =>
        ContextTarget.ForElement(registration, elementId, ProjectRoot.From(folder), origin: Diagram.HelmCharts.Origin);

    private static string FindingLine(DiagramProblem problem)
    {
        var where = problem.Location is DiagramProblemFileLocation located
            ? $"{located.RelativePath}:{located.Line.ToString(CultureInfo.InvariantCulture)}"
            : problem.Location?.ToString() ?? "-";
        return $"{problem.RuleId} | {problem.Severity} | {where} | {Quoted(problem.Message)}";
    }

    private static string Box(Rect box) =>
        string.Create(CultureInfo.InvariantCulture, $"{box.X:R}, {box.Y:R}, {box.Width:R} x {box.Height:R}");

    private static string Quoted(string text) => TranscriptText.Quoted(text);

    /// <summary>The copy's absolute location, should a sentence carry it: the one thing in a reading that differs per run.</summary>
    private static string Mask(string text, string folder) => text.Replace(folder, "{copy}", StringComparison.Ordinal);

    /// <summary>A text's lines without their endings, so a registration written with either convention reads the same.</summary>
    private static IEnumerable<string> Lines(string text)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        return text.EndsWith('\n') ? lines[..^1] : lines;
    }

    /// <summary>
    /// A folder's files, path for path and byte for byte, in ordinal order of their relative paths,
    /// with or without the registrations beside <c>Chart.yaml</c>: whose hash makes two folders the
    /// same for <c>sameAs</c>, and the same chart for <c>sameChartAs</c>.
    /// </summary>
    private static async Task<byte[]> ManifestAsync(string folder, bool registrations, CancellationToken cancellationToken)
    {
        var manifest = new StringBuilder();
        foreach ((string full, string relative) in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                     .Select(path => (Full: path, Relative: Path.GetRelativePath(folder, path).Replace('\\', '/')))
                     .Where(path => registrations || !(path.Relative.EndsWith(".adp", StringComparison.Ordinal) && !path.Relative.Contains('/', StringComparison.Ordinal)))
                     .OrderBy(path => path.Relative, StringComparer.Ordinal))
        {
            manifest.Append(relative).Append(' ').Append(TranscriptText.Sha256(await File.ReadAllBytesAsync(full, cancellationToken))).Append('\n');
        }

        return TranscriptText.Encode(manifest.ToString());
    }
}
