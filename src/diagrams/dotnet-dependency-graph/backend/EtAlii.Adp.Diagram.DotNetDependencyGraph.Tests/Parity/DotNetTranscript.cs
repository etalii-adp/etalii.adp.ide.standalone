using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.DotNetDependencyGraph.Wire;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests.Parity;

/// <summary>
/// The .NET dependency graph's parity transcript: what the module answers for every solution of the
/// corpus and for a set of hand-built graphs, frozen as <c>Parity/dotnet.transcript.json</c> before
/// any of it was derived from the bundled DISL definition. Every switch-over must reproduce it byte
/// for byte; after the switch-over it is the oracle the hand-written values were deleted in favour of.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>: the solutions among the test project's <c>Fixtures/</c>, the module's
/// <c>examples/</c> and the shipped copies under <c>src/examples/diagrams/dotnet-dependency-graph/</c>.
/// Package descriptions are read from the test project's fixture NuGet cache, never the machine's,
/// so the transcript does not depend on what happens to be restored where it runs.
/// </para>
/// <para>
/// <b>Per solution</b>: the derived graph (projects, packages, references and the failures that
/// travel with it), the payload and computed position of every element, the property rows of every
/// element id, the empty id and an unknown one, the rows asked of a path that is not a solution, the
/// answer to a property write, and the session's answers to a reparent and to a move without a history.
/// </para>
/// <para>
/// <b>The hand-built graphs</b> cover what no solution of the corpus reaches: a description that is
/// present, empty or absent, a version conflict of each kind, a package with no known version,
/// multi-targeting across two .NET versions, a framework naming none, and an ambient package.
/// </para>
/// <para>
/// <b>The module's registrations</b> are written too, so the toolbox and context menus this type
/// does not offer stay recorded as absent: it registers no toolbox, context-action or validation
/// provider, and the transcript fails the day one appears unannounced.
/// </para>
/// </remarks>
public sealed partial class DotNetTranscript() : TranscriptAdapter("dotnet-dependency-graph", DotNetCorpus.TestProject, "dotnet.transcript.json")
{
    private const string Unknown = "parity:unknown";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromMessages(DependencyElementPayload.Descriptor);

    public override string Origin => Diagram.DependencyGraph.Origin.Key;

    /// <summary>None: the module registers no toolbox, which <see cref="Registrations"/> records instead.</summary>
    protected override IReadOnlyList<ToolboxItemDefinition>? Toolbox => null;

    public override string DocumentsName => "solutions";

    public override string DocumentName => "solution";

    public override IReadOnlyList<TranscriptDocument> Corpus() => [.. DotNetCorpus.Solutions().Select(OnDisk)];

    public override Task WriteBeforeDocumentsAsync(Utf8JsonWriter json, CancellationToken cancellationToken)
    {
        json.WritePropertyName("registrations");
        json.WriteLines(Registrations());
        return Task.CompletedTask;
    }

    /// <summary>
    /// A shipped copy of an example is written as the one it is the same as, so it costs a line
    /// while it stays identical. Equal bytes alone are not enough: a solution is the files it
    /// names too, so the projects are compared through the graph they give. And the extension is
    /// part of it: the reader chooses its pattern by extension, so the same bytes under another
    /// one are another document (finding B11's new document was Empty.slnx's bytes under .sln until
    /// task 9 made it a .slnx, which is now recorded as the same document as Empty.slnx).
    /// </summary>
    public override async Task<string?> WriteIdentityAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, CancellationToken cancellationToken)
    {
        var sha = await base.WriteIdentityAsync(json, document, bytes, cancellationToken);
        using var store = Store();
        return $"{sha}|{Path.GetExtension(document.Name)}|{Fingerprint(store.GetOrLoad(Path.Combine(SourceFolder, document.Name)))}";
    }

    public override async Task WriteDocumentAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, string folder, CancellationToken cancellationToken)
    {
        var path = Path.Combine(SourceFolder, document.Name);
        using var store = Store();
        var graph = store.GetOrLoad(path);
        await WriteGraphAsync(json, store.BodyOf(graph), path, cancellationToken);

        // The session's answers that do not depend on a history: a reparent is refused whatever the
        // element, and a move without a history is refused as read-only.
        var session = new DotNetDependencyGraphSession(path, store, new DependencyElementMapper(), registrationPath: null, history: null, watch: false);
        await using (session)
        {
            var first = graph.Projects.FirstOrDefault()?.Id ?? Unknown;
            json.WriteStartArray("session");
            json.WriteStringValue($"baseline => {(await session.BaselineAsync(cancellationToken)).Sum(delta => delta is DiagramAddDelta add ? add.Elements.Count : 0)} elements added");
            json.WriteStringValue($"reparent {first} => {await session.MoveElementAsync(first, Unknown, 0, cancellationToken)}");
            json.WriteStringValue($"move {first} to (10, 20) => {await session.MoveElementToAsync(first, 10, 20, cancellationToken)}");
            json.WriteEndArray();
        }
    }

    public override async Task WriteAfterDocumentsAsync(Utf8JsonWriter json, CancellationToken cancellationToken)
    {
        json.WriteStartArray("graphs");
        foreach ((string name, DependencyGraphModel graph) in HandBuiltGraphs())
        {
            json.WriteStartObject();
            json.WriteString("graph", name);
            await WriteGraphAsync(json, DotNetBody.Of(graph), @"C:\parity\Parity.slnx", cancellationToken);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>Every service the module registers, as <c>service =&gt; implementation</c>, in registration order.</summary>
    private static List<string> Registrations()
    {
        var services = new ServiceCollection().AddDotNetDependencyGraph();
        return
        [
            .. services.Select(descriptor =>
                $"{descriptor.ServiceType.Name} => {descriptor.ImplementationType?.Name ?? (descriptor.ImplementationFactory is not null ? "factory" : "instance")} ({descriptor.Lifetime})"),
        ];
    }

    /// <summary>A store that reads package descriptions from the fixture cache.</summary>
    private static DependencyGraphStore Store() => new(new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(DotNetCorpus.FixtureCache));

    private static async Task WriteGraphAsync(Utf8JsonWriter json, DotNetBody body, string solutionPath, CancellationToken cancellationToken)
    {
        var graph = body.Graph;
        json.WritePropertyName("projects");
        json.WriteLines([.. graph.Projects.Select(project => $"{project.Id} | {project.Name} | {project.RelativePath} | [{string.Join(", ", project.TargetFrameworks)}] | {TranscriptText.Quoted(project.DotNetVersion)}")]);

        json.WritePropertyName("packages");
        json.WriteLines([.. graph.Packages.Select(package =>
            $"{package.Id} | {package.PackageId} | [{string.Join(", ", package.Versions)}] | conflict {package.HasVersionConflict} | dependents {package.DependentProjectCount} | ambient {package.IsAmbient} | {TranscriptText.Quoted(package.Description)}")]);

        json.WritePropertyName("references");
        json.WriteLines([.. graph.Edges.Select(edge => $"{edge.Id} | {edge.Kind} | {edge.FromElementId} -> {edge.ToElementId}")]);

        json.WritePropertyName("failures");
        json.WriteLines([.. graph.Failures.Select(failure => Mask($"{failure.Path} | {failure.Reason}"))]);

        json.WritePropertyName("payloads");
        json.WriteLines([.. new DependencyElementMapper().Elements(graph)
            .Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]);

        // A stored position wins over the computed one, element by element; an id matching nothing is not applied.
        if (graph.Projects.Count > 0)
        {
            var stored = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal)
            {
                [graph.Projects[0].Id] = new(10.5, -20),
                ["project:gone.csproj"] = new(1, 1),
            };
            json.WritePropertyName("payloadsWithAStoredPosition");
            json.WriteLines([.. new DependencyElementMapper().Elements(graph, stored)
                .Where(element => element.Id == graph.Projects[0].Id)
                .Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]);
        }

        var provider = new DotNetContextPropertyProvider(new FixedStore(body));
        List<string> targets =
        [
            .. graph.Projects.Select(project => project.Id),
            .. graph.Packages.Select(package => package.Id),
            .. graph.Edges.Select(edge => edge.Id),
            "",
            Unknown,
        ];

        json.WriteStartArray("rows");
        foreach (var target in targets)
        {
            var rows = await provider.DescribeAsync(Target(solutionPath, target), cancellationToken);
            json.WriteStartObject();
            json.WriteString("target", target);
            json.WritePropertyName("rows");
            json.WriteLines([.. rows.Select(TranscriptText.RowLine)]);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        // The provider answers only for a solution: the same element id asked of another file is nobody's.
        var notASolution = Path.ChangeExtension(solutionPath, ".adp");
        var first = targets[0];
        json.WriteString("rowsOfAnotherFile", $"{first} of a .adp => {(await provider.DescribeAsync(Target(notASolution, first), cancellationToken)).Count} rows");

        var write = await provider.SetAsync(Target(solutionPath, first), "dotnet.name", "Renamed", cancellationToken);
        json.WriteString("write", write.IsSuccess ? "ok" : $"refused: {write.Error}");
    }

    // The transcript names its solutions without a folder, so their project has no root.
    private static ContextTarget Target(string path, string elementId) =>
        ContextTarget.ForElement(path, elementId, Path.GetDirectoryName(path) is { Length: > 0 } folder ? ProjectRoot.From(folder) : default, origin: Diagram.DependencyGraph.Origin);

    /// <summary>What a graph holds, so two solutions with equal bytes but different projects are told apart.</summary>
    private static string Fingerprint(DependencyGraphModel graph) =>
        TranscriptText.Sha256(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",
            graph.Projects.Select(project => $"{project.Id}|{string.Join(";", project.TargetFrameworks)}")
                .Concat(graph.Packages.Select(package => $"{package.Id}|{string.Join(";", package.Versions)}|{package.Description}"))
                .Concat(graph.Edges.Select(edge => edge.Id))
                .Concat(graph.Failures.Select(failure => $"{failure.Path}|{failure.Reason}")))));

    /// <summary>
    /// Graphs no solution of the corpus gives, built by hand: each case the rows, the payloads and the
    /// layout distinguish.
    /// </summary>
    internal static IEnumerable<(string Name, DependencyGraphModel Graph)> HandBuiltGraphs()
    {
        yield return ("descriptions and conflicts", new DependencyGraphModel(
            [
                Project("src/App/App.csproj", ["net10.0", "net8.0"], "10.0, 8.0"),
                Project("src/Lib/Lib.csproj", ["netstandard2.0"], null),
                Project("src/None/None.csproj", [], null),
                Project("tests/App.Tests/App.Tests.csproj", ["net472", "net9.0"], "9.0"),
            ],
            [
                new PackageNode("package:Described", "Described", ["1.0.0"], false, "A package with a description.\nOver two lines.", 1),
                new PackageNode("package:EmptyDescription", "EmptyDescription", ["2.0.0"], false, "", 1),
                new PackageNode("package:Undescribed", "Undescribed", ["3.0.0"], false, null, 1),
                new PackageNode("package:TwoVersions", "TwoVersions", ["1.0.0", "1.1.0"], true, null, 2),
                new PackageNode("package:KnownAndUnknown", "KnownAndUnknown", ["4.0.0"], true, null, 2),
                new PackageNode("package:Unknowable", "Unknowable", [], false, null, 1),
            ],
            [
                new DependsOnEdge("depends:project:src/App/App.csproj->project:src/Lib/Lib.csproj", "project:src/App/App.csproj", "project:src/Lib/Lib.csproj", DependsOnKind.Project),
                new DependsOnEdge("depends:project:tests/App.Tests/App.Tests.csproj->project:src/App/App.csproj", "project:tests/App.Tests/App.Tests.csproj", "project:src/App/App.csproj", DependsOnKind.Project),
                new DependsOnEdge("depends:project:src/App/App.csproj->package:Described", "project:src/App/App.csproj", "package:Described", DependsOnKind.Package),
                new DependsOnEdge("depends:project:src/Lib/Lib.csproj->package:TwoVersions", "project:src/Lib/Lib.csproj", "package:TwoVersions", DependsOnKind.Package),
                new DependsOnEdge("depends:project:src/App/App.csproj->package:TwoVersions", "project:src/App/App.csproj", "package:TwoVersions", DependsOnKind.Package),
            ],
            [new SolutionFailure("Missing.csproj", "The solution names this project, but the file is not there.")]));

        // Seven projects all referencing one package: at the floor of five it is ambient.
        var many = Enumerable.Range(1, 7).Select(index => Project($"p{index}/P{index}.csproj", ["net10.0"], "10.0")).ToList();
        yield return ("an ambient package", new DependencyGraphModel(
            many,
            [
                new PackageNode("package:xunit.v3", "xunit.v3", ["3.0.0"], false, "The test framework.", 7, true),
                new PackageNode("package:Serilog", "Serilog", ["4.4.0"], false, null, 1),
            ],
            [
                .. many.Select(project => new DependsOnEdge($"depends:{project.Id}->package:xunit.v3", project.Id, "package:xunit.v3", DependsOnKind.Package)),
                new DependsOnEdge("depends:project:p1/P1.csproj->package:Serilog", "project:p1/P1.csproj", "package:Serilog", DependsOnKind.Package),
                new DependsOnEdge("depends:project:p2/P2.csproj->project:p1/P1.csproj", "project:p2/P2.csproj", "project:p1/P1.csproj", DependsOnKind.Project),
            ],
            []));

        yield return ("empty", DependencyGraphModel.Empty);
        yield break;

        static ProjectNode Project(string path, IReadOnlyList<string> frameworks, string? version) => new($"project:{path}", Path.GetFileNameWithoutExtension(path), path, frameworks, version);
    }

    private static string Mask(string text)
    {
        var masked = text.Replace(DotNetCorpus.SourceFolder, "{src}", StringComparison.Ordinal);
        return SourcePath().Replace(masked, match => match.Value.Replace('\\', '/'));
    }

    [GeneratedRegex(@"\{src\}[^\s'""]*")]
    private static partial Regex SourcePath();

    /// <summary>A store that holds one body, whatever it is asked for: the provider's view of a store.</summary>
    private sealed class FixedStore(DotNetBody body) : IDependencyGraphStore
    {
        public DependencyGraphModel GetOrLoad(string diagramPath) => body.Graph;

        public DotNetBody BodyOf(DependencyGraphModel graph) => body;
    }
}
