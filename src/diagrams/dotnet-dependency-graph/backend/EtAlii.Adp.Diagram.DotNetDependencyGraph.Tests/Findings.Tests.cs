using System.Text;
using System.Xml;
using System.Xml.Linq;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// One test per finding of dotnet-dependency-graph-disl-fbl that was known only by reading or is
/// settled by a fixture, each on its fixture under <c>Fixtures/</c>: what today's code does with it
/// beside what FBL, as the library implements it, does with it. B2, B11's new document, B16 and B17
/// are here; B11's registration without a <c>body:</c> header and B14 need the library's internal
/// <c>BodyLocator</c> and <c>OpenRegistration</c>, so they are in the library's own test project, as
/// <c>EtAlii.Adp.Specification.Fbl.Tests/RealFiles/DotNetDependencyGraphFindings.Tests.cs</c>, reading
/// these same fixtures.
/// </summary>
/// <remarks>
/// <para>
/// Each test computes the difference as one sentence, null when FBL's way gives what today's code
/// gives, and holds it to the sentence pinned beside it. Every test was first run asserting the
/// difference was null, and failed with the sentence it now pins (task 1's pull request records
/// each), but B17's, which passed and is reported for striking. So a pinned sentence that changes, or a finding that no longer occurs, fails here, and
/// the later task that changes it says so in its own diff.
/// </para>
/// <para>
/// FBL's side is read through the library, never re-implemented here: a file body is opened by
/// <see cref="PluginBody"/>, and an XML file is read by <see cref="OpenBody"/> through a declared binding. The binding of the .NET dependency graph is
/// the requirements' appendix draft, since none exists yet.
/// </para>
/// </remarks>
public sealed class FindingsTests
{
    private const string PluginId = "net.etalii.adp.dotnet.solution";

    /// <summary>The binding of the requirements' appendix, as drafted against FBL 0.2.</summary>
    private const string DraftBinding = """
        {
          "fbl": "0.2",
          "bindings": {
            "solution": {
              "title": ".NET solution",
              "claims": {
                "extensions": [".slnx", ".sln"],
                "origins": ["dotnet/dependency-graph"],
                "shared": true,
                "registrationOnly": true
              },
              "body": {
                "kind": "file",
                "settle": 250,
                "x-follows": ["the project files the solution names", "Directory.Packages.props above each project"],
                "x-environment": ["NUGET_PACKAGES"]
              },
              "reader": { "plugin": "net.etalii.adp.dotnet.solution", "version": "^0.1.0" },
              "readOnly": "A .NET dependency graph shows the solution as it is and changes nothing in it. Edit the project file this value comes from, and the diagram will follow.",
              "registration": { "createOnFirstPlacement": false },
              "template": { "text": "<Solution>\r\n</Solution>\r\n" }
            }
          }
        }
        """;

    /// <summary>
    /// A declared binding of a project file's package references, the way any <c>xml</c> binding
    /// would select them: by the element names a project file is written with.
    /// </summary>
    private const string ProjectBinding = """
        {
          "fbl": "0.2",
          "bindings": {
            "project": {
              "claims": { "extensions": [".csproj"] },
              "body": { "kind": "file", "family": "xml" },
              "reader": "declared",
              "readOnly": true,
              "elements": [
                {
                  "name": "package",
                  "type": "Package",
                  "at": "/Project/ItemGroup/PackageReference",
                  "id": { "from": { "attribute": "Include" } },
                  "attributes": { "version": { "attribute": "Version" } }
                }
              ]
            }
          }
        }
        """;

    private static string Fixtures { get; } = RepositoryPaths.Under(
        "src", "diagrams", "dotnet-dependency-graph", "backend", "EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests", "Fixtures");

    [Fact]
    public void B2_TheModelLiesInFilesTheSolutionLeadsTo_WhichNeitherKindOfSubjectHandsAPlugin()
    {
        // Arrange.
        var solution = Fixture("outside", "app", "Outside.slnx");
        var folder = Path.GetDirectoryName(solution)!;
        var graph = Graph(solution);

        // Today's reading draws on the solution, the project files it names and the versions file
        // above them; the version 3.1.4 is written nowhere but in that versions file.
        var package = Assert.Single(graph.Packages);
        Assert.Equal(["3.1.4"], package.Versions);
        List<string> drawnOn =
        [
            solution,
            .. new SolutionReader().Read(solution).Projects.Select(project => project.AbsolutePath),
            Fixture("outside", "Directory.Packages.props"),
        ];
        var outside = drawnOn
            .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
            .Where(relative => relative.StartsWith("../", StringComparison.Ordinal))
            .ToList();

        // Act: the file body FBL has for a solution hands the plugin what it hands it.
        var plugin = new RecordingPlugin();
        _ = PluginBody.Open(File.ReadAllBytes(solution), Binding(DraftBinding, "solution"), plugin, "Outside.slnx");
        var handed = plugin.Handed;

        var observed = handed.Count == drawnOn.Count && outside.Count == 0
            ? null
            : $"today's reading of Outside.slnx draws on {drawnOn.Count} files, {outside.Count} of them outside the solution's folder ({string.Join(", ", outside)}); "
              + $"a file body hands the plugin {handed.Count} file ({string.Join(", ", handed.Select(file => file.Length == 0 ? "the body" : file))}), "
              + "and a folder subject's globs are relative to the solution's folder";

        // Assert.
        Shows("B2", observed,
            "today's reading of Outside.slnx draws on 4 files, 2 of them outside the solution's folder (../lib/Lib/Lib.csproj, ../Directory.Packages.props); "
            + "a file body hands the plugin 1 file (the body), and a folder subject's globs are relative to the solution's folder");
    }

    [Fact]
    public void B11_ANewDocument_IsTheSlnxSerializationUnderItsOwnExtension()
    {
        // Arrange: the new document exactly as core creates it, the base name plus the primary
        // extension holding the module's empty document, beside a registration without a body: header.
        // Resolved by task 9 (Q4, ruled 2026-10-11): the primary became .slnx and the fixture, which was
        // New.sln, became New.slnx with the same bytes. Until then this pinned "core creates New.sln, which
        // the registration opens, holding the .slnx serialization <Solution>\r\n</Solution>\r\n; the reader
        // chooses the classic pattern by its extension and reads 0 projects and 0 failures".
        var factory = new ServiceCollection().AddDotNetDependencyGraph().BuildServiceProvider()
            .GetServices<IDiagramDocumentFactory>().Single(candidate => candidate.Origin == Diagram.DependencyGraph.Origin);
        var content = factory.CreateEmptyDocument("New");
        var sibling = Fixture("new-document", "New" + Diagram.DependencyGraph.Extension);
        var registration = Fixture("new-document", "New.adp");
        Assert.Equal(Encoding.UTF8.GetBytes(content), File.ReadAllBytes(sibling));
        Assert.Equal(AdpRegistration.Compose(Diagram.DependencyGraph.Origin.MimeType), File.ReadAllText(registration));

        // Act.
        var body = DiagramFilePair.BodyOf(registration, Catalog, Fixtures)!.Value.Path;
        var serialization = SerializationOf(content);
        var reading = new SolutionReader().Read(body);

        var observed = Path.GetExtension(body) == serialization
            ? null
            : $"core creates {Path.GetFileName(body)}, which the registration opens, holding the {serialization} serialization {Visible(content)}; "
              + $"the reader chooses the classic pattern by its extension and reads {reading.Projects.Count} projects and {reading.Failures.Count} failures";

        // Assert: no longer a difference.
        Shows("B11", observed, pinned: null);
    }

    [Fact]
    public void B16_AProjectFileInUtf16_OpensTodayAndIsUnreadableAsAnFblBody()
    {
        // Arrange.
        var project = Fixture("encoding", "Utf16", "Utf16.csproj");

        // Act.
        var today = Packages(Graph(Fixture("encoding", "Encoding.slnx")));
        var model = OpenBody.Open(File.ReadAllBytes(project), Binding(ProjectBinding, "project")).Model;
        var read = Packages(model);

        var observed = !model.Unreadable && read == today
            ? null
            : $"today's reader reads Utf16.csproj as [{today}]; "
              + $"a declared xml binding reads it {(model.Unreadable ? "as unreadable" : $"as [{read}]")}: {string.Join("; ", model.Findings.Select(finding => $"{finding.Code} {finding.Message}"))}";

        // Assert.
        Shows("B16", observed,
            "today's reader reads Utf16.csproj as [Encoded.Package 1.2.3]; a declared xml binding reads it as unreadable: std.unparseable The body is not valid UTF-8.");
    }

    [Fact]
    public void B17_AProjectFileWithADefaultNamespace_IsReadByItsElementNames()
    {
        // Arrange.
        var project = Fixture("namespace", "Old", "Old.csproj");

        // Act.
        var today = Packages(Graph(Fixture("namespace", "Namespace.slnx")));
        var model = OpenBody.Open(File.ReadAllBytes(project), Binding(ProjectBinding, "project")).Model;
        var read = Packages(model);

        var observed = !model.Unreadable && read == today
            ? null
            : $"today's reader matches by local name and reads Old.csproj as [{today}]; "
              + $"a declared xml binding reads it {(model.Unreadable ? "as unreadable" : $"as [{read}]")}";

        // Assert: no difference. The library selects the elements by the names they are written with, and
        // a default namespace adds no prefix to them, so the reading agrees with the readers' match by
        // local name and the finding cannot be shown on this fixture (reported for striking).
        Shows("B17", observed, null);
    }

    private static TestDiagramDefinitionCatalog Catalog { get; } = new(Diagram.DependencyGraph);

    private static string Fixture(params string[] segments) => Path.Combine([Fixtures, .. segments]);

    /// <summary>
    /// Holds a finding's difference to the one pinned: <paramref name="observed"/> is null when FBL's
    /// way gives what today's code gives.
    /// </summary>
    private static void Shows(string finding, string? observed, string? pinned)
    {
        if (pinned is not null)
        {
            Assert.True(observed is not null, $"{finding} no longer occurs on its fixture; strike or update its pinned sentence.");
        }

        Assert.True(observed == pinned, $"{finding} has changed.\nPinned:   {pinned ?? "no difference"}\nObserved: {observed}");
    }

    private static DependencyGraphModel Graph(string solution)
    {
        using var store = new DependencyGraphStore(new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(Fixture("cache")));
        return store.GetOrLoad(solution);
    }

    private static string Packages(DependencyGraphModel graph) =>
        string.Join(", ", graph.Packages.Select(package => $"{package.PackageId} {string.Join("/", package.Versions)}"));

    private static string Packages(FblModel model) =>
        string.Join(", ", model.Elements.Select(element => $"{element.Id} {element.Attributes.GetValueOrDefault("version")}"));

    /// <summary>The serialization a solution's text is written in: the XML one, else the classic one.</summary>
    private static string SerializationOf(string text)
    {
        try
        {
            return XDocument.Parse(text).Root?.Name.LocalName == "Solution" ? Diagram.DocumentExtension : Diagram.AlternateDocumentExtension;
        }
        catch (XmlException)
        {
            return Diagram.AlternateDocumentExtension;
        }
    }

    private static string Visible(string text) => text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static FblBinding Binding(string json, string name)
    {
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        return document!.Bindings[name];
    }

    /// <summary>A plugin that reads nothing and records the files it is handed.</summary>
    private sealed class RecordingPlugin : IPersistencePlugin
    {
        public string Id => PluginId;

        public List<string> Handed { get; } = [];

        public PluginReadResult Read(PluginReadRequest request)
        {
            Handed.AddRange(request.Files.Select(file => file.RelativePath));
            return new PluginReadResult([], [], false);
        }

        public PluginPlanResult Plan(PluginPlanRequest request) => new PluginPlanResult.Refused("Read only.");

        public byte[] Template(PluginTemplateRequest request) => [];

        public IReadOnlyList<string> Watch(PluginReadResult last) => [];
    }
}
