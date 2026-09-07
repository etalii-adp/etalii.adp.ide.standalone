using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>The computed arrangement, and the properties a stored layout depends on.</summary>
public class DotNetDependencyGraphLayoutTests
{
    private static ProjectNode Project(string name) => new($"project:{name}.csproj", name, $"{name}.csproj", ["net10.0"], "10.0");

    private static DependencyGraphModel GraphOf(IReadOnlyList<ProjectNode> projects, IReadOnlyList<DependsOnEdge> edges, IReadOnlyList<PackageNode>? packages = null) =>
        new(projects, packages ?? [], edges, []);

    [Fact]
    public void AProjectSitsBeyondWhatItReferences_SoDependencyRunsOneWayAcrossTheCanvas()
    {
        // Requirement 6.2: a layout suited to a directed dependency graph. Leaf first, then its
        // dependents further out, so a reader follows the arrows in one direction.

        // Arrange. App -> Library -> Core.
        var graph = GraphOf(
            [Project("App"), Project("Library"), Project("Core")],
            [
                new DependsOnEdge("e1", "project:App.csproj", "project:Library.csproj", DependsOnKind.Project),
                new DependsOnEdge("e2", "project:Library.csproj", "project:Core.csproj", DependsOnKind.Project),
            ]);

        // Act.
        var positions = DotNetDependencyGraphLayout.Compute(graph);

        // Assert.
        Assert.True(positions["project:Core.csproj"].X < positions["project:Library.csproj"].X);
        Assert.True(positions["project:Library.csproj"].X < positions["project:App.csproj"].X);
    }

    [Fact]
    public void PackagesBandBeyondTheLastProjectLayer_RatherThanScatteringThroughIt()
    {
        // Arrange.
        var graph = GraphOf(
            [Project("App")],
            [new DependsOnEdge("e1", "project:App.csproj", "package:Serilog", DependsOnKind.Package)],
            [new PackageNode("package:Serilog", "Serilog", ["4.4.0"], false)]);

        // Act.
        var positions = DotNetDependencyGraphLayout.Compute(graph);

        // Assert.
        Assert.True(positions["package:Serilog"].X > positions["project:App.csproj"].X);
    }

    [Fact]
    public void TheSameGraph_ComputesTheSameArrangementEveryTime()
    {
        // A computed layout that moved between two identical readings would make a stored
        // position look wrong the first time the diagram was reopened - the user's arrangement
        // apparently drifting when nothing had changed. Ordering inside a layer is what makes
        // this true, and it is asserted rather than assumed.

        // Arrange.
        var graph = GraphOf(
            [Project("Zeta"), Project("Alpha"), Project("Mid")],
            [new DependsOnEdge("e1", "project:Mid.csproj", "project:Alpha.csproj", DependsOnKind.Project)]);

        // Act.
        var first = DotNetDependencyGraphLayout.Compute(graph);
        var second = DotNetDependencyGraphLayout.Compute(graph);

        // Assert.
        Assert.Equal(first, second);
    }

    [Fact]
    public void ACycleTerminates_RatherThanHangingTheDiagram()
    {
        // MSBuild refuses a project-reference cycle, so this cannot arrive from a working
        // solution - but the diagram is derived from files that may be wrong, and being wrong
        // must not hang it. Without the visiting set this test does not fail, it never returns.

        // Arrange. A -> B -> A.
        var graph = GraphOf(
            [Project("A"), Project("B")],
            [
                new DependsOnEdge("e1", "project:A.csproj", "project:B.csproj", DependsOnKind.Project),
                new DependsOnEdge("e2", "project:B.csproj", "project:A.csproj", DependsOnKind.Project),
            ]);

        // Act.
        var positions = DotNetDependencyGraphLayout.Compute(graph);

        // Assert.
        Assert.Equal(2, positions.Count);
    }

    [Fact]
    public void EveryNodeGetsAPosition_SoNothingLandsOnTopOfTheOrigin()
    {
        // A node the layout forgot would draw at (0,0) under whatever is really there, which
        // looks like a rendering bug rather than a layout one.

        // Arrange.
        var graph = GraphOf(
            [Project("App"), Project("Library")],
            [new DependsOnEdge("e1", "project:App.csproj", "project:Library.csproj", DependsOnKind.Project)],
            [new PackageNode("package:Serilog", "Serilog", ["4.4.0"], false)]);

        // Act.
        var positions = DotNetDependencyGraphLayout.Compute(graph);

        // Assert.
        Assert.Equal(3, positions.Count);
        Assert.All(graph.Projects.Select(project => project.Id).Concat(graph.Packages.Select(package => package.Id)),
            id => Assert.True(positions.ContainsKey(id), $"{id} has no computed position"));
    }
}
