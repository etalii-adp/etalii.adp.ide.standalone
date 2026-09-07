using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The reader against both serializations of one solution. Every fixture describes the same
/// three projects - one at the root, one nested, one absent - so the formats can be asserted to
/// agree rather than each being asserted on its own terms.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two classic fixtures are GENERATED, not authored</b>, by <c>dotnet new sln</c> and
/// <c>dotnet solution add</c> on SDK 10.0.203, with <c>Missing.csproj</c> present at generation
/// and deleted afterwards so the absent project is named by the tool rather than typed in.
/// Regenerate them the same way rather than editing them.
/// </para>
/// <para>
/// <b>That is not tidiness - a hand-written fixture guards what somebody imagined, and what
/// somebody imagined here omitted the default.</b> <c>dotnet solution add</c> takes
/// <c>--in-root</c> defaulting to <c>False</c>, so it creates a solution folder for a nested
/// project unless told otherwise: the <c>2150E333-8FDC-42A3-9474-1A3956D46DE8</c> line that
/// <c>Both.sln</c> carries is the SDK's ORDINARY OUTPUT, not an exotic shape. <c>Flat.sln</c> is
/// the same solution with <c>--in-root</c>, and the two are asserted to agree - so the reader is
/// held to both shapes the tool actually produces.
/// </para>
/// <para>
/// <b>Classic support has fixtures and no live subject, permanently.</b>
/// <c>dotnet solution migrate</c> goes <c>.sln</c> to <c>.slnx</c> only, there is no reverse,
/// and this repository holds no <c>.sln</c> anywhere - so nothing here exercises the classic
/// path except these files. The module readme says so too, for a reader who meets a green suite
/// and assumes both halves of the format have met a real file.
/// </para>
/// </remarks>
public class SolutionReaderTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "solution", name);

    [Theory]
    [InlineData("Both.slnx")]
    [InlineData("Both.sln")]
    [InlineData("Flat.sln")]
    public void Read_FindsTheProjectsThatExist_InEitherSerialization(string solution)
    {
        // Arrange.
        var reader = new SolutionReader();

        // Act.
        var reading = reader.Read(FixturePath(solution));

        // Assert.
        // Both formats describe the same solution, so both answer the same - which is the
        // point of asserting them against one fixture pair rather than two.
        Assert.Equal(["Alpha", "Beta"], reading.Projects.Select(project => project.Name).Order());
        Assert.Equal(
            ["Alpha.csproj", "nested/Beta.csproj"],
            reading.Projects.Select(project => project.RelativePath).Order());
    }

    [Theory]
    [InlineData("Both.slnx")]
    [InlineData("Both.sln")]
    [InlineData("Flat.sln")]
    public void Read_ReportsANamedProjectThatIsNotThere_AndKeepsTheRest(string solution)
    {
        // Requirement 2.4: the diagram opens with what resolved, and the failure is reported
        // rather than the project silently vanishing from the graph.

        // Arrange.
        var reader = new SolutionReader();

        // Act.
        var reading = reader.Read(FixturePath(solution));

        // Assert.
        var failure = Assert.Single(reading.Failures);
        Assert.Contains("Missing.csproj", failure.Path, StringComparison.Ordinal);
        Assert.NotEqual("", failure.Reason);
        Assert.Equal(2, reading.Projects.Count);
    }

    [Fact]
    public void Read_Classic_SkipsSolutionFolders_RatherThanReportingEachAsAMissingProject()
    {
        // The trap this test exists for: a solution folder is written as a Project(...) line
        // whose second field is a folder name, not a path. A reader taking every such line at
        // face value emits one node per folder and then reports each as a missing file - so
        // the failure list fills with problems no user can act on, and the graph gains nodes
        // that are not projects. The .slnx form has no such ambiguity; only the classic does.

        // Arrange.
        var reader = new SolutionReader();

        // Act.
        var reading = reader.Read(FixturePath("Both.sln"));

        // Assert.
        Assert.DoesNotContain(reading.Projects, project => project.Name == "nested");
        Assert.DoesNotContain(reading.Failures, failure => failure.Path == "nested");
    }

    [Fact]
    public void Read_TheTwoClassicShapesTheSdkProduces_Agree()
    {
        // The pairing that makes the solution-folder test mean something. Both.sln and Flat.sln
        // are the SAME solution written by the same tool, differing only in --in-root: one files
        // the nested project under a solution folder, the other does not. A reader that handled
        // only the flat shape would still pass every other test in this class, because every
        // other assertion holds for the flat file on its own.

        // Arrange.
        var reader = new SolutionReader();

        // Act.
        var withFolders = reader.Read(FixturePath("Both.sln"));
        var flat = reader.Read(FixturePath("Flat.sln"));

        // Assert.
        Assert.Equal(
            flat.Projects.Select(project => project.RelativePath).Order(),
            withFolders.Projects.Select(project => project.RelativePath).Order());
        Assert.Equal(flat.Failures.Count, withFolders.Failures.Count);
    }

    [Fact]
    public void Read_AnAbsentSolution_IsAFailureRatherThanAThrow()
    {
        // Arrange.
        var reader = new SolutionReader();

        // Act.
        var reading = reader.Read(FixturePath("NoSuchSolution.slnx"));

        // Assert.
        Assert.Empty(reading.Projects);
        Assert.Single(reading.Failures);
    }

    [Fact]
    public void Read_AnUnparseableSolution_ReportsTheReason_RatherThanReturningSilence()
    {
        // This test WAS weaker: it asserted only that no projects came back, and passed while
        // the reader swallowed the parse failure entirely - no projects and no reason, which
        // opens an empty diagram claiming the solution contains nothing. That is precisely the
        // failure mode Requirement 2.4 names, and the session's integration test is what caught
        // it. The missing assertion is the one that matters, so it is here now: a reader that
        // says nothing is indistinguishable from a solution that has nothing in it.

        // Arrange.
        var reader = new SolutionReader();

        // Act.
        var reading = reader.Read(FixturePath("Broken.slnx"));

        // Assert.
        Assert.Empty(reading.Projects);
        var failure = Assert.Single(reading.Failures);
        Assert.NotEqual("", failure.Reason);
    }

    [Fact]
    public void Read_ASolutionThatGenuinelyNamesNoProjects_IsNotAFailure()
    {
        // The pairing for the test above, and the reason the parse failure is a null rather
        // than an empty list: "would not parse" and "parsed, and names nothing" are different
        // answers, and an empty solution is not broken.

        // Arrange.
        var reader = new SolutionReader();

        // Act.
        var reading = reader.Read(FixturePath("Empty.slnx"));

        // Assert.
        Assert.Empty(reading.Projects);
        Assert.Empty(reading.Failures);
    }

    [Fact]
    public void Read_Slnx_TakesProjectsAndNotTheFilesFiledBesideThem()
    {
        // A .slnx <Folder> carries <File> entries too - this repository files its
        // .editorconfig and Directory.Packages.props that way. They are not projects, and a
        // reader matching on the Path attribute alone would make nodes of them.

        // Arrange.
        var reader = new SolutionReader();

        // Act.
        var reading = reader.Read(FixturePath("Both.slnx"));

        // Assert.
        Assert.DoesNotContain(reading.Projects, project => project.Name == "Directory.Packages");
        Assert.DoesNotContain(reading.Failures, failure => failure.Path.Contains("Directory.Packages", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_RelativePathsAreForwardSlashed_WhateverTheSolutionWrote()
    {
        // The relative path is what an element id is built from (task 4), and the classic
        // format writes backslashes on every platform. An id that differed by separator would
        // lose a stored position between one platform and another.

        // Arrange.
        var reader = new SolutionReader();

        // Act.
        var reading = reader.Read(FixturePath("Both.sln"));

        // Assert.
        var beta = Assert.Single(reading.Projects, project => project.Name == "Beta");
        Assert.Equal("nested/Beta.csproj", beta.RelativePath);
        Assert.DoesNotContain('\\', beta.RelativePath);
    }
}
