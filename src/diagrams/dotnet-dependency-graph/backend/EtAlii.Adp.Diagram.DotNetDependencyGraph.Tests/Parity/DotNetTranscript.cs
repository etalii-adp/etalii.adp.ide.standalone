using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.DotNetDependencyGraph.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
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
internal static partial class DotNetTranscript
{
    /// <summary>The checked-in file's name, in this project's <c>Parity</c> folder.</summary>
    private const string FileName = "dotnet.transcript.json";

    private const string TestProject = "EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests";
    private const string Unknown = "parity:unknown";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromMessages(DependencyElementPayload.Descriptor);

    /// <summary>The module's folder, <c>src/diagrams/dotnet-dependency-graph</c>.</summary>
    private static string ModuleFolder { get; } = FindModuleFolder();

    /// <summary>The checked-in transcript.</summary>
    public static string CheckedInPath => Path.Combine(ModuleFolder, "backend", TestProject, "Parity", FileName);

    private static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    private static string FixtureCache => Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures", "cache");

    /// <summary>The corpus, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    private static IReadOnlyList<string> Corpus()
    {
        static IEnumerable<string> Solutions(string folder) =>
            Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(path => Path.GetExtension(path) is ".sln" or ".slnx")
                    .Select(path => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/'))
                    .Order(StringComparer.Ordinal)
                : [];

        return
        [
            .. Solutions(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures")),
            .. Solutions(Path.Combine(ModuleFolder, "examples")),
            .. Solutions(Path.Combine(SourceFolder, "examples", "diagrams", "dotnet-dependency-graph")),
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
            json.WriteString("generator", $"src/diagrams/dotnet-dependency-graph/backend/{TestProject}/Parity/DotNetTranscript.cs");
            json.WriteString("regenerate", "ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)");

            json.WritePropertyName("registrations");
            WriteLines(json, Registrations());

            json.WriteStartArray("solutions");
            var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var relative in Corpus())
            {
                await WriteSolutionAsync(json, relative, recorded, cancellationToken);
            }

            json.WriteEndArray();

            json.WriteStartArray("graphs");
            foreach ((string name, DependencyGraphModel graph) in HandBuiltGraphs())
            {
                json.WriteStartObject();
                json.WriteString("graph", name);
                await WriteGraphAsync(json, graph, @"C:\parity\Parity.slnx", cancellationToken);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
            await json.FlushAsync(cancellationToken);
        }

        stream.Write("\r\n"u8);
        return stream.ToArray();
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

    private static async Task WriteSolutionAsync(Utf8JsonWriter json, string relative, Dictionary<string, string> recorded, CancellationToken cancellationToken)
    {
        var path = Path.Combine(SourceFolder, relative);
        var sha = TranscriptText.Sha256(await File.ReadAllBytesAsync(path, cancellationToken));

        json.WriteStartObject();
        json.WriteString("solution", relative);
        json.WriteString("sha256", sha);

        // A shipped copy of an example is written as the one it is the same as, so it costs a line
        // while it stays identical. Equal bytes alone are not enough: a solution is the files it
        // names too, so the projects are compared through the graph they give.
        var store = new DependencyGraphStore(new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(FixtureCache));
        var graph = store.GetOrLoad(path);
        var key = $"{sha}|{Fingerprint(graph)}";
        if (recorded.TryGetValue(key, out var first))
        {
            json.WriteString("sameAs", first);
            json.WriteEndObject();
            return;
        }

        recorded[key] = relative;
        await WriteGraphAsync(json, graph, path, cancellationToken);

        // The session's answers that do not depend on a history: a reparent is refused whatever the
        // element, and a move without a history is refused as read-only.
        var session = new DotNetDependencyGraphSession(path, store, new DependencyElementMapper(), registrationPath: null, history: null, watch: false);
        await using (session)
        {
            var first2 = graph.Projects.FirstOrDefault()?.Id ?? Unknown;
            json.WriteStartArray("session");
            json.WriteStringValue($"baseline => {session.Baseline().Sum(delta => delta is DiagramAddDelta add ? add.Elements.Count : 0)} elements added");
            json.WriteStringValue($"reparent {first2} => {await session.MoveElementAsync(first2, Unknown, 0, cancellationToken)}");
            json.WriteStringValue($"move {first2} to (10, 20) => {await session.MoveElementToAsync(first2, 10, 20, cancellationToken)}");
            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    private static async Task WriteGraphAsync(Utf8JsonWriter json, DependencyGraphModel graph, string solutionPath, CancellationToken cancellationToken)
    {
        json.WritePropertyName("projects");
        WriteLines(json, [.. graph.Projects.Select(project => $"{project.Id} | {project.Name} | {project.RelativePath} | [{string.Join(", ", project.TargetFrameworks)}] | {TranscriptText.Quoted(project.DotNetVersion)}")]);

        json.WritePropertyName("packages");
        WriteLines(json, [.. graph.Packages.Select(package =>
            $"{package.Id} | {package.PackageId} | [{string.Join(", ", package.Versions)}] | conflict {package.HasVersionConflict} | dependents {package.DependentProjectCount} | ambient {package.IsAmbient} | {TranscriptText.Quoted(package.Description)}")]);

        json.WritePropertyName("references");
        WriteLines(json, [.. graph.Edges.Select(edge => $"{edge.Id} | {edge.Kind} | {edge.FromElementId} -> {edge.ToElementId}")]);

        json.WritePropertyName("failures");
        WriteLines(json, [.. graph.Failures.Select(failure => Mask($"{failure.Path} | {failure.Reason}"))]);

        json.WritePropertyName("payloads");
        WriteLines(json, [.. new DependencyElementMapper().Elements(graph)
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
            WriteLines(json, [.. new DependencyElementMapper().Elements(graph, stored)
                .Where(element => element.Id == graph.Projects[0].Id)
                .Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]);
        }

        var provider = new DotNetContextPropertyProvider(new FixedStore(graph));
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
            WriteLines(json, [.. rows.Select(TranscriptText.RowLine)]);
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

    private static ContextTarget Target(string path, string elementId) =>
        new(ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, Path.GetDirectoryName(path) ?? "", default, elementId, Diagram.DependencyGraph.Origin);

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
    private static IEnumerable<(string Name, DependencyGraphModel Graph)> HandBuiltGraphs()
    {
        static ProjectNode Project(string path, IReadOnlyList<string> frameworks, string? version) =>
            new($"project:{path}", Path.GetFileNameWithoutExtension(path), path, frameworks, version);

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
    }

    private static string Mask(string text)
    {
        var masked = text.Replace(SourceFolder, "{src}", StringComparison.Ordinal);
        return SourcePath().Replace(masked, match => match.Value.Replace('\\', '/'));
    }

    [GeneratedRegex(@"\{src\}[^\s'""]*")]
    private static partial Regex SourcePath();

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
            if (directory.Name == "dotnet-dependency-graph" && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No dotnet-dependency-graph folder above {AppContext.BaseDirectory}. That is a failure, not a skip.");
    }

    /// <summary>A store that holds one graph, whatever it is asked for: the provider's view of a store.</summary>
    private sealed class FixedStore(DependencyGraphModel graph) : IDependencyGraphStore
    {
        public DependencyGraphModel GetOrLoad(string diagramPath) => graph;
    }
}
