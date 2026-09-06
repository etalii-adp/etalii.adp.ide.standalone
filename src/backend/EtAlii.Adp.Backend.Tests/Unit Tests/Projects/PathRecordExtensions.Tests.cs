using EtAlii.Adp.Projects;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The wire-path-to-absolute-path reconstruction (github-build-pipeline task 4.3's first
/// Linux CI run found this): segments cannot carry the Unix root, so <c>/tmp/x</c> arrives
/// as <c>["tmp","x"]</c> and used to recombine relative - failing every project add on the
/// runner while Windows, whose root lives inside its <c>"C:"</c> segment, never noticed.
/// </summary>
public class PathRecordExtensionsTests
{
    [Fact]
    public void RootlessSegments_GetThePlatformRootBack()
    {
        // Arrange and act: what a Unix absolute path looks like after the split.
        var absolute = new[] { "tmp", "adp-tests", "sample-project" }.AbsolutePath();

        // Assert: rooted again, with the segments intact behind the root.
        Assert.True(IoPath.IsPathRooted(absolute), $"'{absolute}' is not rooted");
        Assert.EndsWith(
            IoPath.Combine("tmp", "adp-tests", "sample-project"),
            absolute,
            StringComparison.Ordinal);
        Assert.StartsWith(IoPath.DirectorySeparatorChar.ToString(), absolute, StringComparison.Ordinal);
    }

    [Fact]
    public void RootedSegments_ComeBackUntouched()
    {
        // Arrange: a first segment carrying the platform's own root - "C:\" here, "/" there -
        // the shape whose recombination is already rooted and must not be decorated further.
        var root = IoPath.GetPathRoot(IoPath.GetTempPath())!;

        // Act.
        var absolute = new[] { root, "adp-tests" }.AbsolutePath();

        // Assert.
        Assert.Equal(IoPath.Combine(root, "adp-tests"), absolute);
    }
}
