using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Tests;

/// <summary>A project root and a path within it as values, normalized once (code-structure FR-059, T170).</summary>
public class ProjectRootTests
{
    private static readonly bool _namesIgnoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    [Fact]
    public void From_MakesThePathAbsolute_AndDropsATrailingSeparator()
    {
        // Arrange.
        using var folder = TestFolder.Create("project-root");
        var written = folder.Path + IoPath.DirectorySeparatorChar + "a" + IoPath.DirectorySeparatorChar + ".." + IoPath.DirectorySeparatorChar;

        // Act.
        var root = ProjectRoot.From(written);

        // Assert.
        Assert.Equal(IoPath.GetFullPath(folder.Path).TrimEnd(IoPath.DirectorySeparatorChar), root.FullPath);
    }

    [Fact]
    public void From_KeepsAVolumesRoot_AsItsOwnSeparator()
    {
        // Arrange.
        var volume = IoPath.GetPathRoot(IoPath.GetFullPath("."))!;

        // Act and assert.
        Assert.Equal(volume, ProjectRoot.From(volume).FullPath);
    }

    [Fact]
    public void TwoRoots_AreEqual_AsTheFileSystemComparesNames()
    {
        // Arrange.
        using var folder = TestFolder.Create("project-root");
        var lower = ProjectRoot.From(folder.Path.ToLowerInvariant());
        var upper = ProjectRoot.From(folder.Path.ToUpperInvariant());

        // Act and assert: one folder on Windows and macOS, two on Linux.
        Assert.Equal(_namesIgnoreCase, lower == upper);
        Assert.Equal(_namesIgnoreCase, lower.GetHashCode() == upper.GetHashCode());
        Assert.Equal(ProjectRoot.From(folder.Path + IoPath.DirectorySeparatorChar), ProjectRoot.From(folder.Path));
    }

    [Fact]
    public void Contains_TheRootItselfAndWhatIsUnderIt_ButNotItsNeighbour()
    {
        // Arrange.
        using var folder = TestFolder.Create("project-root");
        var root = ProjectRoot.From(folder.Combine("project"));

        // Act and assert.
        Assert.True(root.Contains(folder.Combine("project")));
        Assert.True(root.Contains(folder.Combine("project", "a", "b.txt")));
        Assert.False(root.Contains(folder.Combine("project-other", "b.txt")));
        Assert.False(root.Contains(folder.Combine("b.txt")));
    }

    [Fact]
    public void Resolve_ARelativePath_IsWithinTheRoot_AndKnowsItsRelativePath()
    {
        // Arrange.
        using var folder = TestFolder.Create("project-root");
        var root = ProjectRoot.From(folder.Path);

        // Act.
        var path = root.Resolve(IoPath.Combine("models", ".", "a.adp"));

        // Assert.
        Assert.NotNull(path);
        Assert.Equal(root, path.Value.Root);
        Assert.Equal(IoPath.Combine(root.FullPath, "models", "a.adp"), path.Value.FullPath);
        Assert.Equal(IoPath.Combine("models", "a.adp"), path.Value.RelativePath);
    }

    [Fact]
    public void Resolve_APathThatClimbsOut_IsRefused()
    {
        // Arrange.
        using var folder = TestFolder.Create("project-root");
        var root = ProjectRoot.From(folder.Combine("project"));

        // Act and assert.
        Assert.Null(root.Resolve(IoPath.Combine("..", "elsewhere.txt")));
        Assert.Null(root.Resolve(folder.Combine("elsewhere.txt")));
        Assert.NotNull(root.Resolve(IoPath.Combine("a", "..", "kept.txt")));
    }

    [Fact]
    public void Resolve_ThroughALinkThatLeadsOut_IsRefused()
    {
        // Arrange: a link inside the project to a folder outside it.
        using var folder = TestFolder.Create("project-root");
        Directory.CreateDirectory(folder.Combine("project"));
        Directory.CreateDirectory(folder.Combine("outside"));
        try
        {
            Directory.CreateSymbolicLink(folder.Combine("project", "out"), folder.Combine("outside"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"This machine may not create symbolic links: {exception.Message}");
        }

        var root = ProjectRoot.From(folder.Combine("project"));

        // Act and assert.
        Assert.Null(root.Resolve(IoPath.Combine("out", "secret.txt")));
    }

    [Fact]
    public void TheDefaultRoot_ResolvesNothing()
    {
        // Act and assert.
        Assert.Null(default(ProjectRoot).Resolve("a.txt"));
        Assert.Equal("", default(ProjectRoot).FullPath);
    }

    [Fact]
    public void TwoPaths_AreEqual_AsTheFileSystemComparesNames()
    {
        // Arrange.
        using var folder = TestFolder.Create("project-root");
        var root = ProjectRoot.From(folder.Path);

        // Act.
        var lower = root.Resolve("a.txt");
        var upper = root.Resolve("A.TXT");

        // Assert.
        Assert.Equal(_namesIgnoreCase, lower == upper);
    }
}
