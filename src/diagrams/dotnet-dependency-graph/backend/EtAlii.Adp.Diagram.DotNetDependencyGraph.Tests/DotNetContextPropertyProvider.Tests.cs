using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The property grid's answers for a selected element. Driven through a stub store, so the
/// provider is tested against a graph rather than against a disk.
/// </summary>
public class DotNetContextPropertyProviderTests
{
    private sealed class StubStore(DependencyGraphModel graph) : IDependencyGraphStore
    {
        public DependencyGraphModel GetOrLoad(string diagramPath) => graph;
    }

    private static ContextTarget TargetOf(string elementId) =>
        new(ContextScope.DiagramElement, @"C:\solution\Solution.slnx", IsContainer: false, ShortGuid.NewShortGuid(), ElementId: elementId);

    private static async Task<IReadOnlyList<ContextPropertyDefinition>> Describe(DependencyGraphModel graph, string elementId) =>
        await new DotNetContextPropertyProvider(new StubStore(graph)).DescribeAsync(TargetOf(elementId), CancellationToken.None);

    private static DependencyGraphModel GraphOf(ProjectNode? project = null, PackageNode? package = null) =>
        new(project is null ? [] : [project], package is null ? [] : [package], [], []);

    [Fact]
    public async Task EveryRow_IsReadOnly_AndSaysWhyAndWhatToEditInstead()
    {
        // Property-grid Requirement 4 at 100%, and Requirement 4.3: presented as a value that
        // cannot be edited, never as an editor that rejects input. The reason is the house
        // shape - cause first, then remedy.

        // Arrange.
        var graph = GraphOf(
            new ProjectNode("project:A.csproj", "A", "A.csproj", ["net10.0"], "10.0"),
            new PackageNode("package:Serilog", "Serilog", ["4.4.0"], false, "Structured logging"));

        // Act.
        var projectRows = await Describe(graph, "project:A.csproj");
        var packageRows = await Describe(graph, "package:Serilog");

        // Assert.
        Assert.NotEmpty(projectRows);
        Assert.NotEmpty(packageRows);
        Assert.All(projectRows.Concat(packageRows), row =>
        {
            ArgumentNullException.ThrowIfNull(row);

            Assert.NotEqual("", row.ReadOnlyReason);
            Assert.False(row.IsEditable);
        });
    }

    [Fact]
    public async Task AProject_ShowsItsNameFrameworkAndDotNetVersion()
    {
        // Requirement 4.1.

        // Arrange.
        var graph = GraphOf(new ProjectNode("project:src/A/A.csproj", "A", "src/A/A.csproj", ["net10.0"], "10.0"));

        // Act.
        var rows = await Describe(graph, "project:src/A/A.csproj");

        // Assert.
        Assert.Equal("A", Assert.Single(rows, row => row.Id == "dotnet.name").Value);
        Assert.Equal("net10.0", Assert.Single(rows, row => row.Id == "dotnet.target-frameworks").Value);
        Assert.Equal("10.0", Assert.Single(rows, row => row.Id == "dotnet.dotnet-version").Value);
    }

    [Fact]
    public async Task AProjectWithNoDiscoverableDotNetVersion_StillShowsTheRow_SayingSo()
    {
        // Requirement 4.4, and the one place this provider departs from the ansible precedent:
        // that one drops a row it has nothing to put in. Here the absence must be VISIBLE,
        // because a missing row and a missing value read very differently to a reader wondering
        // whether the diagram simply failed to look.

        // Arrange.
        var graph = GraphOf(new ProjectNode("project:A.csproj", "A", "A.csproj", ["netstandard2.0"], null));

        // Act.
        var rows = await Describe(graph, "project:A.csproj");

        // Assert.
        var row = Assert.Single(rows, candidate => candidate.Id == "dotnet.dotnet-version");
        Assert.Equal(DotNetContextPropertyProvider.NotDiscoverable, row.Value);
        Assert.NotEqual("", row.Value);
    }

    [Fact]
    public async Task APackage_ShowsItsIdVersionAndDescription()
    {
        // Requirements 4.2 and 5.1.

        // Arrange.
        var graph = GraphOf(package: new PackageNode("package:Serilog", "Serilog", ["4.4.0"], false, "Structured logging"));

        // Act.
        var rows = await Describe(graph, "package:Serilog");

        // Assert.
        Assert.Equal("Serilog", Assert.Single(rows, row => row.Id == "dotnet.package-id").Value);
        Assert.Equal("4.4.0", Assert.Single(rows, row => row.Id == "dotnet.package-version").Value);
        Assert.Equal("Structured logging", Assert.Single(rows, row => row.Id == "dotnet.package-description").Value);
    }

    [Fact]
    public async Task AnUncachedPackage_SaysSoExplicitly_RatherThanShowingAnEmptyDescription()
    {
        // Requirement 5.3: a missing description is never an error - and never a blank field
        // that could equally mean the package has no description of its own.

        // Arrange.
        var graph = GraphOf(package: new PackageNode("package:Obscure", "Obscure", ["1.0.0"], false));

        // Act.
        var rows = await Describe(graph, "package:Obscure");

        // Assert.
        Assert.Equal(DotNetContextPropertyProvider.NotCached, Assert.Single(rows, row => row.Id == "dotnet.package-description").Value);
    }

    [Fact]
    public async Task APackageInConflict_ShowsBothVersionsAndSaysThatTheyConflict()
    {
        // Requirement 3.5 surfaced where a reader will actually meet it. A version list with
        // two entries states the fact; the conflict row states what the fact means.

        // Arrange.
        var graph = GraphOf(package: new PackageNode("package:Serilog", "Serilog", ["3.1.0", "4.4.0"], true));

        // Act.
        var rows = await Describe(graph, "package:Serilog");

        // Assert.
        Assert.Equal("3.1.0, 4.4.0", Assert.Single(rows, row => row.Id == "dotnet.package-version").Value);
        Assert.Single(rows, row => row.Id == "dotnet.package-version-conflict");
    }

    [Fact]
    public async Task APackageNotInConflict_HasNoConflictRow()
    {
        // The pairing for the test above: an assertion that a row appears proves nothing about
        // the condition unless something proves it stays away when the condition is false.

        // Arrange.
        var graph = GraphOf(package: new PackageNode("package:Serilog", "Serilog", ["4.4.0"], false));

        // Act.
        var rows = await Describe(graph, "package:Serilog");

        // Assert.
        Assert.DoesNotContain(rows, row => row.Id == "dotnet.package-version-conflict");
    }

    [Fact]
    public async Task SetAsync_Refuses_EvenThoughTheResolverAlreadyWould()
    {
        // Unreachable through the grid, and written to refuse anyway: a provider that would
        // accept a write if the resolver ever changed is a trap rather than a design.

        // Arrange.
        var provider = new DotNetContextPropertyProvider(new StubStore(DependencyGraphModel.Empty));

        // Act.
        var result = await provider.SetAsync(TargetOf("project:A.csproj"), "dotnet.name", "Renamed", CancellationToken.None);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.NotEqual("", result.Error);
    }

    [Fact]
    public async Task AnElementTheGraphDoesNotHold_DescribesNothing()
    {
        // Arrange, act.
        var rows = await Describe(DependencyGraphModel.Empty, "project:Gone.csproj");

        // Assert.
        Assert.Empty(rows);
    }

    /// <summary>Counts which files the provider asked the store to read.</summary>
    private sealed class CountingStore : IDependencyGraphStore
    {
        public List<string> Asked { get; } = [];

        public DependencyGraphModel GetOrLoad(string diagramPath)
        {
            Asked.Add(diagramPath);
            return DependencyGraphModel.Empty;
        }
    }

    [Fact]
    public async Task AnElementOfAnotherTypesDiagram_IsNotAnswered_AndItsFileIsNeverRead()
    {
        // The resolver consults every provider in this scope for every element selection in any
        // diagram, so the first question is whether the file is ours. Unasked, a Wardley element
        // made this provider parse tea.owm as a solution ("named 0 projects") in a gate log.
        // "No rows" alone would pass against that defect - an unknown id finds nothing either way -
        // so what is asserted is that the file was never read.

        // Arrange.
        var store = new CountingStore();
        var foreign = new ContextTarget(
            ContextScope.DiagramElement, @"C:\project\wardley-project\tea.owm", IsContainer: false, ShortGuid.NewShortGuid(), ElementId: "component-1");

        // Act.
        var rows = await new DotNetContextPropertyProvider(store).DescribeAsync(foreign, CancellationToken.None);

        // Assert.
        Assert.Empty(rows);
        Assert.Empty(store.Asked);
    }

    [Theory]
    [InlineData(@"C:\solution\Solution.sln")]
    [InlineData(@"C:\solution\Solution.slnx")]
    [InlineData(@"C:\solution\LEGACY.SLN")]
    public async Task EitherSolutionFormat_IsStillAnswered(string solutionPath)
    {
        // The other half: the ownership question must not turn away the module's own files, in
        // either serialization or either case - and it shows the counting store counts.

        // Arrange.
        var store = new CountingStore();
        var own = new ContextTarget(
            ContextScope.DiagramElement, solutionPath, IsContainer: false, ShortGuid.NewShortGuid(), ElementId: "project:A.csproj");

        // Act.
        await new DotNetContextPropertyProvider(store).DescribeAsync(own, CancellationToken.None);

        // Assert.
        Assert.Equal([solutionPath], store.Asked);
    }
}
