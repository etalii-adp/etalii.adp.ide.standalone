using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The reader against both serializations of one solution. The fixture pair describes the same
/// three projects - one at the root, one nested, one absent - so the two formats can be
/// asserted to agree rather than each being asserted on its own terms.
/// </summary>
public class SolutionReaderTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "solution", name);

    [Theory]
    [InlineData("Both.slnx")]
    [InlineData("Both.sln")]
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
