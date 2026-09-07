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
    public void ALayerTallerThanTheWrapPoint_WrapsIntoColumnsRatherThanOneRibbon()
    {
        // THE SCALE ANSWER, and it is a measurement rather than a preference. Against this
        // repository's own EtAlii.Adp.slnx the layer distribution is 1/3/7/4/3/2/4/66/14 -
        // sixty-six diagram modules share one depth, because each references the same core and
        // nothing references them. Stacked, that layer stood 5,850 units tall against a
        // 3,080-wide diagram: the unreadable ribbon the requirement names. Wrapped, the whole
        // graph measures 5,280 x 1,440 and nothing is hidden - which is why this is a wrap
        // rather than the limit the task also offered, since a derived diagram that silently
        // shows part of its subject is worse than one that is awkward to read.

        // Arrange. One layer, comfortably past the wrap point.
        var count = DotNetDependencyGraphLayout.LayerWrapAt + 5;
        var projects = Enumerable.Range(0, count).Select(index => Project($"P{index:D3}")).ToArray();

        // Act.
        var positions = DotNetDependencyGraphLayout.Compute(GraphOf(projects, []));

        // Assert.
        var tallestColumn = positions.Values.GroupBy(position => position.X).Max(column => column.Count());
        Assert.True(
            tallestColumn <= DotNetDependencyGraphLayout.LayerWrapAt,
            $"A column holds {tallestColumn} boxes, past the wrap point of {DotNetDependencyGraphLayout.LayerWrapAt}.");
        Assert.True(positions.Values.Select(position => position.X).Distinct().Count() > 1, "The layer did not wrap at all.");
    }

    [Fact]
    public void AWrappedLayer_DoesNotDrawOverTheLayerAfterIt()
    {
        // The pairing that matters: wrapping only helps if the extra columns push the next
        // layer along. Without that, a wrapped layer overlaps its successor and the readability
        // fix trades a tall ribbon for boxes drawn on top of each other.

        // Arrange. A wide first layer, and a project that depends on one of its members.
        var wide = Enumerable.Range(0, DotNetDependencyGraphLayout.LayerWrapAt + 5).Select(index => Project($"P{index:D3}")).ToArray();
        var dependent = Project("Zzz");

        // Act.
        var positions = DotNetDependencyGraphLayout.Compute(GraphOf(
            [.. wide, dependent],
            [new DependsOnEdge("e1", dependent.Id, wide[0].Id, DependsOnKind.Project)]));

        // Assert.
        var widest = wide.Max(project => positions[project.Id].X);
        Assert.True(
            positions[dependent.Id].X > widest,
            $"The dependent sits at {positions[dependent.Id].X}, not clear of the wrapped layer ending at {widest}.");
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
