using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The derivation, tested without a filesystem: readings in, graph out. That boundary is what
/// lets the id scheme be pinned by construction rather than through a disk.
/// </summary>
public class DependencyGraphTests
{
    private static SolutionProject Project(string relativePath, string name) =>
        new(name, relativePath, Path.GetFullPath(Path.Combine("/solution", relativePath)));

    private static ProjectReading Reading(
        IReadOnlyList<PackageReferenceReading>? packages = null,
        IReadOnlyList<ProjectReferenceReading>? projects = null,
        IReadOnlyList<string>? frameworks = null) =>
        new(frameworks ?? ["net10.0"], projects ?? [], packages ?? [], []);

    [Fact]
    public void APackageReferencedAtOneVersionByManyProjects_IsOneElementWithManyEdges()
    {
        // Requirement 3.4: the diagram shows sharing rather than repeating it.

        // Arrange.
        var alpha = Project("Alpha.csproj", "Alpha");
        var beta = Project("Beta.csproj", "Beta");
        var solution = new SolutionReading([alpha, beta], []);
        var readings = new Dictionary<string, ProjectReading>
        {
            [alpha.AbsolutePath] = Reading([new PackageReferenceReading("Serilog", "4.4.0")]),
            [beta.AbsolutePath] = Reading([new PackageReferenceReading("Serilog", "4.4.0")]),
        };

        // Act.
        var graph = new DependencyGraph().Derive(solution, readings);

        // Assert.
        var package = Assert.Single(graph.Packages);
        Assert.Equal("package:Serilog", package.Id);
        Assert.False(package.HasVersionConflict);
        Assert.Equal(2, graph.Edges.Count(edge => edge.ToElementId == package.Id));
    }

    [Fact]
    public void APackageReferencedAtTwoVersions_IsOneElementMarkedAsAConflict_CarryingBoth()
    {
        // Requirement 3.5 forbids collapsing SILENTLY. Collapsing loudly is what it asks for:
        // one element, both versions on it, marked - which is more useful than two disconnected
        // boxes, because a version conflict is precisely the thing a reader wants surfaced.

        // Arrange.
        var alpha = Project("Alpha.csproj", "Alpha");
        var beta = Project("Beta.csproj", "Beta");
        var solution = new SolutionReading([alpha, beta], []);
        var readings = new Dictionary<string, ProjectReading>
        {
            [alpha.AbsolutePath] = Reading([new PackageReferenceReading("Serilog", "4.4.0")]),
            [beta.AbsolutePath] = Reading([new PackageReferenceReading("Serilog", "3.1.0")]),
        };

        // Act.
        var graph = new DependencyGraph().Derive(solution, readings);

        // Assert.
        var package = Assert.Single(graph.Packages);
        Assert.True(package.HasVersionConflict);
        Assert.Equal(["3.1.0", "4.4.0"], package.Versions);
    }

    [Fact]
    public void APackageWhoseVersionChanges_KeepsItsElementId()
    {
        // THE GUARDING TEST of this module's load-bearing design decision, and the reason the
        // package id carries no version.
        //
        // A stored position in the .adp's layout: block is bound to an element id. If the id
        // included the version, then bumping a package - the single commonest change a solution
        // undergoes - would change the id, and the stored position would bind to an element
        // that no longer exists: dropped on the next write, and the user's arrangement lost on
        // the most frequent edit there is. That is Requirement 7.2 breached at exactly the
        // moment it matters most.
        //
        // The cost of excluding the version is paid in Requirement 3.5, and paid deliberately:
        // two versions become one marked element rather than two boxes (see the test above).
        //
        // Seen to fail before being trusted, against an id built as package:<id>@<version>.

        // Arrange. The same solution, read before and after a version bump, and nothing else
        // changed - the id must not move.
        var alpha = Project("Alpha.csproj", "Alpha");
        var solution = new SolutionReading([alpha], []);
        var before = new Dictionary<string, ProjectReading>
        {
            [alpha.AbsolutePath] = Reading([new PackageReferenceReading("Serilog", "4.4.0")]),
        };
        var after = new Dictionary<string, ProjectReading>
        {
            [alpha.AbsolutePath] = Reading([new PackageReferenceReading("Serilog", "5.0.0")]),
        };

        // Act.
        var graph = new DependencyGraph();
        var idBefore = Assert.Single(graph.Derive(solution, before).Packages).Id;
        var idAfter = Assert.Single(graph.Derive(solution, after).Packages).Id;

        // Assert.
        Assert.Equal(idBefore, idAfter);
        Assert.DoesNotContain("4.4.0", idBefore, StringComparison.Ordinal);
        Assert.DoesNotContain("5.0.0", idAfter, StringComparison.Ordinal);
    }

    [Fact]
    public void AProjectId_IsItsPathRatherThanItsName_SoARenamedAssemblyKeepsItsPlace()
    {
        // The other half of Requirement 6.7: what an id is made of decides what survives a
        // refresh. Path-based means renaming the assembly keeps the box where the user put it.

        // Arrange.
        var project = Project("src/Alpha/Alpha.csproj", "Alpha");
        var solution = new SolutionReading([project], []);
        var readings = new Dictionary<string, ProjectReading> { [project.AbsolutePath] = Reading() };

        // Act.
        var graph = new DependencyGraph().Derive(solution, readings);

        // Assert.
        Assert.Equal("project:src/Alpha/Alpha.csproj", Assert.Single(graph.Projects).Id);
    }

    [Fact]
    public void AProjectReferenceOutsideTheSolution_IsReportedRatherThanDrawnOrDropped()
    {
        // The correctness rule from both directions: the module does not invent a node the
        // solution does not contain, and does not omit the declaration silently either.

        // Arrange.
        var alpha = Project("Alpha.csproj", "Alpha");
        var solution = new SolutionReading([alpha], []);
        var readings = new Dictionary<string, ProjectReading>
        {
            [alpha.AbsolutePath] = Reading(projects: [new ProjectReferenceReading(@"..\Outside\Outside.csproj", Path.GetFullPath("/elsewhere/Outside.csproj"))]),
        };

        // Act.
        var graph = new DependencyGraph().Derive(solution, readings);

        // Assert.
        Assert.Single(graph.Projects);
        Assert.Empty(graph.Edges);
        Assert.Contains(graph.Failures, failure => failure.Path.Contains("Outside", StringComparison.Ordinal));
    }

    [Fact]
    public void AProjectReferenceInsideTheSolution_IsADirectedEdgeToThatProject()
    {
        // Requirement 3.2.

        // Arrange.
        var alpha = Project("Alpha.csproj", "Alpha");
        var beta = Project("Beta.csproj", "Beta");
        var solution = new SolutionReading([alpha, beta], []);
        var readings = new Dictionary<string, ProjectReading>
        {
            [alpha.AbsolutePath] = Reading(projects: [new ProjectReferenceReading("Beta.csproj", beta.AbsolutePath)]),
            [beta.AbsolutePath] = Reading(),
        };

        // Act.
        var graph = new DependencyGraph().Derive(solution, readings);

        // Assert.
        var edge = Assert.Single(graph.Edges);
        Assert.Equal("project:Alpha.csproj", edge.FromElementId);
        Assert.Equal("project:Beta.csproj", edge.ToElementId);
        Assert.Equal(DependsOnKind.Project, edge.Kind);
    }

    [Fact]
    public void AVersionThatCouldNotBeDiscovered_MarksAConflictWithoutPretendingToBeAVersion()
    {
        // One project pins a version and another's cannot be resolved: the projects may well
        // disagree, and the reader should see that. The undiscoverable one must not appear in
        // the version list, because a null is not a version.

        // Arrange.
        var alpha = Project("Alpha.csproj", "Alpha");
        var beta = Project("Beta.csproj", "Beta");
        var solution = new SolutionReading([alpha, beta], []);
        var readings = new Dictionary<string, ProjectReading>
        {
            [alpha.AbsolutePath] = Reading([new PackageReferenceReading("Serilog", "4.4.0")]),
            [beta.AbsolutePath] = Reading([new PackageReferenceReading("Serilog", null)]),
        };

        // Act.
        var graph = new DependencyGraph().Derive(solution, readings);

        // Assert.
        var package = Assert.Single(graph.Packages);
        Assert.Equal(["4.4.0"], package.Versions);
        Assert.True(package.HasVersionConflict);
    }

    [Fact]
    public void TheDotNetVersion_IsAbsentRatherThanGuessed_WhenTheFrameworkDoesNotNameOne()
    {
        // Requirement 4.4: an absence shown explicitly, never a value invented to fill a field.

        // Arrange.
        var standard = Project("Standard.csproj", "Standard");
        var modern = Project("Modern.csproj", "Modern");
        var solution = new SolutionReading([standard, modern], []);
        var readings = new Dictionary<string, ProjectReading>
        {
            [standard.AbsolutePath] = Reading(frameworks: ["netstandard2.0"]),
            [modern.AbsolutePath] = Reading(frameworks: ["net10.0", "net8.0"]),
        };

        // Act.
        var graph = new DependencyGraph().Derive(solution, readings);

        // Assert.
        Assert.Null(Assert.Single(graph.Projects, project => project.Name == "Standard").DotNetVersion);
        Assert.Equal("10.0, 8.0", Assert.Single(graph.Projects, project => project.Name == "Modern").DotNetVersion);
    }

    [Fact]
    public void OneProjectDeclaringAPackageTwice_IsOneEdge()
    {
        // Two ItemGroups, or one per target framework, is one dependency.

        // Arrange.
        var alpha = Project("Alpha.csproj", "Alpha");
        var solution = new SolutionReading([alpha], []);
        var readings = new Dictionary<string, ProjectReading>
        {
            [alpha.AbsolutePath] = Reading([
                new PackageReferenceReading("Serilog", "4.4.0"),
                new PackageReferenceReading("Serilog", "4.4.0"),
            ]),
        };

        // Act.
        var graph = new DependencyGraph().Derive(solution, readings);

        // Assert.
        Assert.Single(graph.Edges);
    }

    /// <summary>
    /// A solution of <paramref name="projectCount"/> projects, the first
    /// <paramref name="everywhereOn"/> of which reference "Everywhere" and the first two of which
    /// reference "Occasional".
    /// </summary>
    private static DependencyGraphModel GraphOf(int projectCount, int everywhereOn)
    {
        var projects = Enumerable.Range(0, projectCount).Select(index => Project($"P{index:D3}.csproj", $"P{index:D3}")).ToArray();
        var readings = new Dictionary<string, ProjectReading>();

        for (var index = 0; index < projectCount; index++)
        {
            var packages = new List<PackageReferenceReading>();
            if (index < everywhereOn)
            {
                packages.Add(new PackageReferenceReading("Everywhere", "1.0.0"));
            }

            if (index < 2)
            {
                packages.Add(new PackageReferenceReading("Occasional", "1.0.0"));
            }

            readings[projects[index].AbsolutePath] = Reading(packages);
        }

        return new DependencyGraph().Derive(new SolutionReading(projects, []), readings);
    }

    [Fact]
    public void APackageMostProjectsReference_IsMarkedAmbient_AndOneFewProjectsReferenceIsNot()
    {
        // THE SCALE ANSWER, and it is a measurement rather than a preference. Against this
        // repository's own EtAlii.Adp.slnx (104 projects, 17 packages, 427 edges of which 155
        // are package references) the package degrees are 26 26 26 19 18 14 6 6 4 2 2 1 1 1 1 1
        // 1: FOUR package nodes carry 97 of the 155 package edges, 63% of them, and they
        // discriminate nothing - an edge on 26 of 104 projects says "this is a test project",
        // which the project's own name already says. The node count was never the problem;
        // nothing here is too big, only too uniformly connected.

        // Arrange, act. 100 projects: the threshold is 15 of them.
        var graph = GraphOf(projectCount: 100, everywhereOn: 40);

        // Assert.
        var everywhere = Assert.Single(graph.Packages, package => package.PackageId == "Everywhere");
        Assert.True(everywhere.IsAmbient);
        Assert.Equal(40, everywhere.DependentProjectCount);

        var occasional = Assert.Single(graph.Packages, package => package.PackageId == "Occasional");
        Assert.False(occasional.IsAmbient);
        Assert.Equal(2, occasional.DependentProjectCount);
    }

    [Fact]
    public void TheAmbientThresholdIsAShareOfTheSolution_NotAFixedCount()
    {
        // The pairing that makes the test above mean something: a fixed count would mark the
        // same package in both of these, and a rule that ignored the solution's size would call
        // a package on 20 of 25 projects ordinary while calling one on 20 of 1000 ambient.
        // Degree relative to the subject is the whole idea, and it is why this is not a curated
        // list of build and test package names - a name list needs maintaining and is wrong on
        // the first repository that is not this one.

        // Arrange, act. The same 20 dependents, in a small solution and a large one.
        var small = GraphOf(projectCount: 25, everywhereOn: 20);
        var large = GraphOf(projectCount: 1000, everywhereOn: 20);

        // Assert.
        Assert.True(Assert.Single(small.Packages, package => package.PackageId == "Everywhere").IsAmbient);
        Assert.False(Assert.Single(large.Packages, package => package.PackageId == "Everywhere").IsAmbient);
    }

    [Fact]
    public void ASmallSolution_HidesNothing_HoweverManyOfItsProjectsShareAPackage()
    {
        // Below a handful of dependents there is no crowd to disappear into. On the share alone
        // a four-project solution referencing one package everywhere would hide it - which is
        // not background, it is the graph. The shipped four-project example is exactly this
        // shape, and it must draw complete.

        // Arrange, act.
        var graph = GraphOf(projectCount: 4, everywhereOn: 4);

        // Assert.
        Assert.All(graph.Packages, package => Assert.False(package.IsAmbient));
    }

    [Fact]
    public void AnAmbientPackage_IsStillInTheGraph_WithEveryEdgeItHad()
    {
        // NOT A LIMIT AND NOT A TRUNCATION. Ambient is a marking the canvas reads to decide what
        // to draw by default; the graph keeps every node and every edge, which is what makes the
        // canvas's "show them" restore rather than re-derive. A derivation that dropped them
        // would silently show part of its subject, which is worse than a diagram that is
        // awkward to read.

        // Arrange, act.
        var graph = GraphOf(projectCount: 100, everywhereOn: 40);

        // Assert.
        var everywhere = Assert.Single(graph.Packages, package => package.PackageId == "Everywhere");
        Assert.True(everywhere.IsAmbient);
        Assert.Equal(40, graph.Edges.Count(edge => edge.ToElementId == everywhere.Id));
    }
}
