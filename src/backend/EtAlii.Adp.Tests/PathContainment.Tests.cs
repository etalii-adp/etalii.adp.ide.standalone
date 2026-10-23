using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Tests;

/// <summary>
/// Containment is the one check standing between a project-relative path and the rest of the
/// disk. It compared case-insensitively everywhere, so on Linux a sibling folder differing from
/// the root only in case counted as inside, and it compared the path as written, so a symbolic
/// link inside the root that leads out of it counted as inside too (D6 of the 2026-10-10
/// analysis; behaviour changes B-10 and B-11).
/// </summary>
public sealed class PathContainmentTests : IDisposable
{
    private readonly TestFolder _scratch = TestFolder.Create("path-containment");

    private string Root => IoPath.Combine(_scratch.Path, "project");

    private string Outside => IoPath.Combine(_scratch.Path, "elsewhere");

    public PathContainmentTests()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Outside);
    }

    [Fact]
    public void AFileUnderTheRoot_IsInside() =>
        Assert.True(PathContainment.IsInside(Root, IoPath.Combine(Root, "notes", "a.txt")));

    [Fact]
    public void ASiblingThatStartsWithTheRootsName_IsNotInside() =>
        Assert.False(PathContainment.IsInside(Root, IoPath.Combine(_scratch.Path, "project-other", "a.txt")));

    [Fact]
    public void APathThatClimbsOutWithDots_IsNotInside() =>
        Assert.False(PathContainment.IsInside(Root, IoPath.Combine(Root, "..", "elsewhere", "a.txt")));

    [Fact]
    public void ARootDifferingOnlyInCase_IsInsideOnlyWhereTheFileSystemIgnoresCase()
    {
        // Arrange: the same folder name in upper case, which on Linux is another folder.
        var variant = IoPath.Combine(_scratch.Path, "PROJECT", "a.txt");

        // Act.
        var inside = PathContainment.IsInside(Root, variant);

        // Assert.
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), inside);
    }

    [Fact]
    public void ALinkInsideTheRootThatLeadsOut_IsNotInside()
    {
        // Arrange.
        File.WriteAllText(IoPath.Combine(Outside, "secret.txt"), "");
        var link = IoPath.Combine(Root, "escape");
        CreateLinkOrSkip(link, Outside);

        // Act.
        var inside = PathContainment.IsInside(Root, IoPath.Combine(link, "secret.txt"));

        // Assert.
        Assert.False(inside);
    }

    [Fact]
    public void ALinkInsideTheRootThatStaysInside_IsInside()
    {
        // Arrange.
        var target = Directory.CreateDirectory(IoPath.Combine(Root, "real")).FullName;
        var link = IoPath.Combine(Root, "alias");
        CreateLinkOrSkip(link, target);

        // Act.
        var inside = PathContainment.IsInside(Root, IoPath.Combine(link, "a.txt"));

        // Assert.
        Assert.True(inside);
    }

    public void Dispose() => _scratch.Dispose();

    // Windows lets only an elevated process or one in developer mode create a symbolic link.
    private static void CreateLinkOrSkip(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException && OperatingSystem.IsWindows())
        {
            Assert.Skip($"This process may not create a symbolic link: {exception.Message}");
        }
    }
}
