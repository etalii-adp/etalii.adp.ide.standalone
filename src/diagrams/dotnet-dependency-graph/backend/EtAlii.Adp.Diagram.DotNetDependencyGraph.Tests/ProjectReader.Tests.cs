using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The reader against a centrally managed project tree, because that is what this repository
/// is: the fixture nests a project under two <c>Directory.Packages.props</c> files so the
/// upward walk, the nearest-wins rule and the version-on-the-reference case are all exercised
/// by one project rather than by three contrived ones.
/// </summary>
public class ProjectReaderTests
{
    private static string FixturePath(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "Fixtures", "cpm", .. parts]);

    private static ProjectReading ReadNested() => new ProjectReader().Read(FixturePath("nested", "Nested.csproj"));

    [Fact]
    public void Read_TakesMultiTargetedFrameworks_NotJustTheSingularForm()
    {
        // A reader knowing only <TargetFramework> reports nothing for a multi-targeted project,
        // and "no frameworks" is indistinguishable from "declares none" in the grid.

        // Arrange, act.
        var reading = ReadNested();

        // Assert.
        Assert.Equal(["net10.0", "net8.0"], reading.TargetFrameworks);
    }

    [Fact]
    public void Read_ResolvesAVersionlessReference_FromCentralPackageManagement()
    {
        // The normal case in this repository, not an edge case: the .csproj names the package
        // and the version lives in a Directory.Packages.props further up the tree.

        // Arrange, act.
        var reading = ReadNested();

        // Assert.
        var serilog = Assert.Single(reading.PackageReferences, package => package.PackageId == "Serilog");
        Assert.Equal("4.4.0", serilog.Version);
    }

    [Fact]
    public void Read_TakesTheNearestCentralVersion_AsMsBuildDoes()
    {
        // Two props files name "Shadowed"; the one beside the project wins. A walk that read
        // the outermost file first would report a version the build does not use - a wrong
        // answer that looks exactly as confident as a right one.

        // Arrange, act.
        var reading = ReadNested();

        // Assert.
        var shadowed = Assert.Single(reading.PackageReferences, package => package.PackageId == "Shadowed");
        Assert.Equal("2.0.0", shadowed.Version);
    }

    [Fact]
    public void Read_PrefersTheVersionWrittenOnTheReference_OverTheCentralOne()
    {
        // Arrange, act.
        var reading = ReadNested();

        // Assert.
        Assert.Equal("9.9.9", Assert.Single(reading.PackageReferences, package => package.PackageId == "Pinned").Version);
    }

    [Fact]
    public void Read_TakesAVersionWrittenAsAChildElement_NotOnlyAsAnAttribute()
    {
        // Both are legal MSBuild, and a reader that knew only the attribute would call this
        // version undiscoverable while it is written plainly in the file.

        // Arrange, act.
        var reading = ReadNested();

        // Assert.
        Assert.Equal("3.3.3", Assert.Single(reading.PackageReferences, package => package.PackageId == "ChildVersion").Version);
    }

    [Fact]
    public void Read_KeepsAReferenceWhoseVersionIsNotDiscoverable_AsAnEdgeWithNoVersion()
    {
        // The correctness rule: an edge is never invented and never silently omitted. Dropping
        // this reference would misrepresent the project; an edge with an unknown version is the
        // truth. Null rather than "" - not discoverable is not the same as blank.

        // Arrange, act.
        var reading = ReadNested();

        // Assert.
        var unknowable = Assert.Single(reading.PackageReferences, package => package.PackageId == "Unknowable");
        Assert.Null(unknowable.Version);
    }

    [Fact]
    public void Read_ResolvesAProjectReferenceToAFullPath_SoTwoSpellingsAreOneNode()
    {
        // Arrange, act.
        var reading = ReadNested();

        // Assert.
        var reference = Assert.Single(reading.ProjectReferences);
        Assert.Equal(FixturePath("Root.csproj"), reference.AbsolutePath);
        Assert.Equal(@"..\Root.csproj", reference.Include);
    }

    [Fact]
    public void Read_AnUnreadableProject_IsAFailureRatherThanAThrow()
    {
        // Omit, report, continue: one bad project file costs the graph that project, never the
        // diagram (the design's error scenario 3).

        // Arrange.
        var reader = new ProjectReader();

        // Act.
        var reading = reader.Read(FixturePath("Broken.csproj"));

        // Assert.
        Assert.Single(reading.Failures);
        Assert.Empty(reading.PackageReferences);
        Assert.Empty(reading.ProjectReferences);
    }
}
