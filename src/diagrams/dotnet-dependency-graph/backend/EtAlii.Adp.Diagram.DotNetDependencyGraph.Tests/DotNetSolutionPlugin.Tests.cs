using System.Text;
using EtAlii.Adp.Specification.Fbl;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The plugin that reads a solution through the module's binding (dotnet-dependency-graph-disl-fbl, task 5,
/// Requirements 3.1, 3.2, 3.5, 3.6 and 3.8): the elements of each fixture with today's ids, a source for every
/// element with its file and line (Q6), no finding (Q2), the same result from two reads of the same files, and
/// reading that never throws on content.
/// </summary>
/// <remarks>
/// The source tests were seen to fail first against a plugin that delivered every element with no file, no line
/// and no span; the pull request of task 5 records the messages.
/// </remarks>
public sealed class DotNetSolutionPluginTests : IDisposable
{
    private static string Fixtures { get; } = RepositoryPaths.Under(
        "src", "diagrams", "dotnet-dependency-graph", "backend", "EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests", "Fixtures");

    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.DotNetSolutionPlugin");

    public void Dispose() => _folder.Dispose();

    /// <summary>Every solution among the fixtures, relative to <c>Fixtures/</c>.</summary>
    public static TheoryData<string> FixtureSolutions() =>
    [
        .. Directory.EnumerateFiles(Fixtures, "*.*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".sln" or ".slnx")
            .Select(path => Path.GetRelativePath(Fixtures, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal),
    ];

    [Theory]
    [MemberData(nameof(FixtureSolutions))]
    public void TheElementsOfAFixture_AreItsProjectsPackagesAndReferences_WithTodaysIds(string fixture)
    {
        // Act.
        var body = Open(Path.Combine(Fixtures, fixture));
        var model = body.Disl;

        // Assert: one element per project, package and reference of the graph, in that order, under today's ids, and
        // no finding from the plugin or the builder.
        Assert.Equal(
            [
                .. body.Graph.Projects.Select(project => $"Project {project.Id}"),
                .. body.Graph.Packages.Select(package => $"Package {package.Id}"),
                .. body.Graph.Edges.Select(edge => $"{edge.Kind}Reference {edge.Id}"),
            ],
            model.Diagram.Elements.Select(element => $"{element.Type.Name} {element.Id}"));
        Assert.Empty(body.Reading.Findings);
        Assert.False(body.Reading.Unreadable);
        Assert.Empty(model.Findings);
    }

    [Theory]
    [MemberData(nameof(FixtureSolutions))]
    public void EveryElementOfAFixture_CarriesItsFileAndLine(string fixture)
    {
        // Act.
        var body = Open(Path.Combine(Fixtures, fixture));

        // Assert: a project is at its entry in the solution, the body, and everything else in a project file.
        foreach (var element in body.Disl.Diagram.Elements)
        {
            Assert.True(element.Line > 0, $"{element.Id} carries no line.");
            if (element.Type.Name == "Project")
            {
                Assert.Null(element.File);
            }
            else
            {
                Assert.False(string.IsNullOrEmpty(element.File), $"{element.Id} carries no file.");
            }
        }
    }

    [Fact]
    public void AProject_IsAtItsEntryInTheSolution_AReferenceAtItsDeclaration_AndAPackageAtItsFirstDeclaration()
    {
        // Act: a solution whose second project lies outside its folder, with a package versioned above both.
        var body = Open(Path.Combine(Fixtures, "outside", "app", "Outside.slnx"));

        // Assert.
        Assert.Equal(
            [
                "project:Local/Local.csproj at the body, line 2",
                "project:../lib/Lib/Lib.csproj at the body, line 3",
                "package:Outside.Versioned at ../lib/Lib/Lib.csproj, line 6",
                "depends:project:Local/Local.csproj->project:../lib/Lib/Lib.csproj at Local/Local.csproj, line 6",
                "depends:project:../lib/Lib/Lib.csproj->package:Outside.Versioned at ../lib/Lib/Lib.csproj, line 6",
            ],
            Sources(body));
    }

    [Fact]
    public void AWildcardReference_IsAtItsOneDeclaration_ForEveryRelationItYields()
    {
        // Arrange.
        var solution = Tree();

        // Act.
        var body = Open(solution);

        // Assert: in either order, which is the file system's.
        Assert.Equal(
            [
                "depends:project:Host/Host.csproj->project:mods/A/A.csproj at Host/Host.csproj, line 4",
                "depends:project:Host/Host.csproj->project:mods/B/B.csproj at Host/Host.csproj, line 4",
            ],
            Sources(body).Where(line => line.StartsWith("depends:project:Host/Host.csproj->project:", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void APackageDeclaredTwice_IsAtItsFirstDeclarationInReadingOrder()
    {
        // Arrange.
        var solution = Tree();

        // Act.
        var body = Open(solution);

        // Assert: A is read before B, and the first spelling names the package. B's reference, in the other casing,
        // keeps today's id and its own declaration (Q5).
        Assert.Equal(["2.0.0", "3.0.0"], Assert.Single(body.Graph.Packages).Versions);
        Assert.Contains("package:Shared at mods/A/A.csproj, line 3", Sources(body));
        Assert.Contains("depends:project:mods/B/B.csproj->package:shared at mods/B/B.csproj, line 3", Sources(body));
    }

    [Fact]
    public void ASpan_IsTheEntrysStartTag_InTheBytesOfItsFile()
    {
        // Arrange: a character of two bytes before the entries, so a span counted in characters would be off.
        var solution = Tree();

        // Act.
        var body = Open(solution);

        // Assert.
        Assert.Equal("""<Project Path="Host/Host.csproj" />""", Text(solution, Element(body, "Host/Host.csproj")));
        Assert.Equal("""<PackageReference Include="Shared" Version="2.0.0" />""", Text(_folder.Combine("mods", "A", "A.csproj"), Element(body, "Shared")));
        Assert.Equal("""<ProjectReference Include="..\mods\*\*.csproj" />""", Text(_folder.Combine("Host", "Host.csproj"), Element(body, "depends:project:Host/Host.csproj->project:mods/A/A.csproj")));
    }

    [Fact]
    public void AProjectFileInUtf16_HasItsLine_AndNoSpan()
    {
        // Act.
        var body = Open(Path.Combine(Fixtures, "encoding", "Encoding.slnx"));

        // Assert: the reference is read as today; its span would be in bytes of a file that is not UTF-8.
        var reference = Assert.Single(body.Reading.Elements, element => element.Type == "PackageReference");
        Assert.True(reference.Line > 0);
        Assert.Equal(default, reference.OwnSpan);
    }

    [Fact]
    public void TwoReadsOfTheSameFiles_GiveTheSameResult()
    {
        // Arrange.
        var solution = Tree();

        // Act.
        var first = Snapshot(Open(solution));
        var second = Snapshot(Open(solution));

        // Assert.
        Assert.Equal(first, second);
    }

    [Fact]
    public void ABrokenSolution_OpensEmpty_WithItsReason_AndNoFinding()
    {
        // Act.
        var body = Open(Path.Combine(Fixtures, "solution", "Broken.slnx"));

        // Assert.
        Assert.Equal(["The solution file could not be parsed."], body.Graph.Failures.Select(failure => failure.Reason));
        Assert.Empty(body.Reading.Elements);
        Assert.Empty(body.Reading.Findings);
        Assert.False(body.Reading.Unreadable);
        Assert.Empty(body.Disl.Diagram.Elements);
    }

    [Fact]
    public void ASolutionThatDoesNotExist_OpensEmpty_WithTodaysSentence()
    {
        // Act.
        var body = Open(_folder.Combine("Nowhere.slnx"));

        // Assert: never handed to the library, so no missing-body finding either.
        Assert.Equal(["The solution file does not exist."], body.Graph.Failures.Select(failure => failure.Reason));
        Assert.Empty(body.Reading.Elements);
        Assert.Empty(body.Reading.Findings);
    }

    [Fact]
    public void ABrokenProjectFile_CostsThatProjectOnly()
    {
        // Arrange.
        var solution = Tree();

        // Act.
        var body = Open(solution);

        // Assert: the project is drawn with nothing known of it, as today, its failure travels with the graph, and
        // the other projects keep their references.
        var failure = Assert.Single(body.Graph.Failures);
        Assert.Equal(_folder.Combine("Broken", "Broken.csproj"), failure.Path);
        Assert.StartsWith("The project file could not be read: ", failure.Reason, StringComparison.Ordinal);
        Assert.Equal(4, body.Graph.Projects.Count);
        Assert.Empty(Assert.Single(body.Graph.Projects, project => project.Name == "Broken").TargetFrameworks);
        Assert.Equal(4, body.Graph.Edges.Count);
    }

    [Fact]
    public void TheStore_KeepsTheBody_OfTheGraphItHandsOut()
    {
        // Arrange.
        var solution = Tree();
        using var store = new DependencyGraphStore(new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(Path.Combine(Fixtures, "cache")));

        // Act.
        var graph = store.GetOrLoad(solution);
        var body = store.BodyOf(graph);

        // Assert.
        Assert.NotNull(body);
        Assert.Same(graph, body.Graph);
        Assert.Equal(Path.GetFullPath(solution), body.SolutionPath);
        Assert.Null(store.BodyOf(DependencyGraphModel.Empty));
    }

    private static DotNetBody Open(string solution) =>
        DotNetBody.Open(solution, new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(Path.Combine(Fixtures, "cache")));

    /// <summary>Each element's model id and where it is, as one line.</summary>
    private static List<string> Sources(DotNetBody body) =>
    [
        .. body.Disl.Diagram.Elements.Select(element => $"{element.Id} at {element.File ?? "the body"}, line {element.Line}"),
    ];

    /// <summary>The element read with <paramref name="id"/>.</summary>
    private static FblElement Element(DotNetBody body, string id) => Assert.Single(body.Reading.Elements, element => element.Id == id);

    /// <summary>The text of <paramref name="element"/>'s span in the file at <paramref name="path"/>.</summary>
    private static string Text(string path, FblElement element)
    {
        Assert.NotEqual(default, element.OwnSpan);
        return Encoding.UTF8.GetString(File.ReadAllBytes(path).AsSpan(element.OwnSpan.Start, element.OwnSpan.End - element.OwnSpan.Start));
    }

    /// <summary>Everything a reading gives, as lines.</summary>
    private static List<string> Snapshot(DotNetBody body) =>
    [
        .. body.Reading.Elements.Select(element =>
            $"{element.Type} {element.Id} {element.Source}->{element.Target} {element.File}:{element.Line} {element.OwnSpan} "
            + string.Join(";", element.Attributes.Select(attribute => $"{attribute.Key}={Value(attribute.Value)}"))),
        .. body.Graph.Failures.Select(failure => $"{failure.Path} {failure.Reason}"),
    ];

    private static string Value(object? value) => value is IEnumerable<string> list ? $"[{string.Join(",", list)}]" : $"{value}";

    /// <summary>
    /// A solution of four projects: a host whose one wildcard reference yields two, two that declare one package in
    /// two casings, and one project file that is not XML. Its first line holds a character of two bytes.
    /// </summary>
    private string Tree()
    {
        Write("Sol.slnx", """
            <!-- Café -->
            <Solution>
              <Project Path="Host/Host.csproj" />
              <Project Path="mods/A/A.csproj" />
              <Project Path="mods/B/B.csproj" />
              <Project Path="Broken/Broken.csproj" />
            </Solution>
            """);
        Write("Host/Host.csproj", """
            <!-- Café -->
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\mods\*\*.csproj" />
              </ItemGroup>
            </Project>
            """);
        Write("mods/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Shared" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """);
        Write("mods/B/B.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="shared" Version="3.0.0" />
              </ItemGroup>
            </Project>
            """);
        Write("Broken/Broken.csproj", "<Project");
        return _folder.Combine("Sol.slnx");
    }

    private void Write(string relative, string text)
    {
        var path = _folder.Combine([.. relative.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\r\n"));
    }
}
