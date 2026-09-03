using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Every file in every module's <c>examples/</c> tree, compared byte-for-byte against its
/// replica under <c>src/examples/</c> (small-refinements Requirement 2.4 and its
/// Non-Functional Reliability clause). structure.md has always said a module example and its
/// replica must never drift, but until this test nothing enforced it: a replica that stopped
/// parsing was caught by <see cref="ExampleRegistrationTests"/>, while one that kept parsing
/// while saying something different was not - which is exactly how the c4 rename and a
/// committed position drag drifted unnoticed before this test's first run.
/// </summary>
/// <remarks>
/// The one tolerated difference is a <c>body:</c> header line inside an <c>.adp</c>
/// registration, which is path-relative by design and may legitimately differ between the two
/// layouts. Everything else - content, line endings, file presence - must match exactly. The
/// walk is live rather than a hard-coded list, so an example a later spec adds is covered on
/// arrival.
/// </remarks>
public class ExampleReplicationTests
{
    /// <summary>
    /// The <c>src</c> folder, found by walking up from the test binary rather than by counting
    /// <c>..</c> segments - the count changes with the build layout, the folder name does not.
    /// </summary>
    private static string SourceRoot { get; } = Locate();

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "diagrams");
            if (Directory.Exists(candidate))
            {
                return IoPath.Combine(directory.FullName, "src");
            }
        }

        throw new InvalidOperationException("The src/diagrams folder was not found above the test binary.");
    }

    public static TheoryData<string> EveryModuleExampleFile()
    {
        var data = new TheoryData<string>();
        foreach (var kind in new[] { "diagrams", "editors" })
        {
            foreach (var module in Directory.EnumerateDirectories(IoPath.Combine(SourceRoot, kind)))
            {
                var examples = IoPath.Combine(module, "examples");
                if (!Directory.Exists(examples))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(examples, "*", SearchOption.AllDirectories))
                {
                    data.Add(IoPath.GetRelativePath(SourceRoot, file));
                }
            }
        }

        return data;
    }

    [Fact]
    public void TheWalk_FindsTheTrackedExampleFiles()
    {
        // Arrange and act.
        var found = EveryModuleExampleFile().Count();

        // Assert: 122 tracked example files at the time of writing. The count moving up is
        // fine - it moving DOWN unexpectedly is what this fact catches, since a vanished
        // example silently shrinks the theory below rather than failing it.
        Assert.True(found >= 122, $"only {found} module example files found; the walk lost some");
    }

    [Theory]
    [MemberData(nameof(EveryModuleExampleFile))]
    public void EveryModuleExampleFile_MatchesItsReplicaByteForByte(string relativePath)
    {
        // Arrange.
        // A module's diagrams/<module>/examples/<rest> replicates to examples/diagrams/<module>/<rest>,
        // and an editor's likewise under examples/editors/ - the tree a user actually opens.
        var modulePath = IoPath.Combine(SourceRoot, relativePath);
        var replicaPath = IoPath.Combine(SourceRoot, ReplicaOf(relativePath));

        // Act.
        Assert.True(File.Exists(replicaPath), $"{relativePath} has no replica at {ReplicaOf(relativePath)}");
        var moduleBytes = File.ReadAllBytes(modulePath);
        var replicaBytes = File.ReadAllBytes(replicaPath);

        // Assert.
        if (moduleBytes.SequenceEqual(replicaBytes))
        {
            return;
        }

        // The single tolerated difference: a body: header inside an .adp registration. Anything
        // else that differs is drift, and the fix goes in the example - never in this test.
        Assert.True(
            string.Equals(IoPath.GetExtension(modulePath), ".adp", StringComparison.OrdinalIgnoreCase),
            $"{relativePath} differs from its replica");
        var moduleLines = File.ReadAllLines(modulePath);
        var replicaLines = File.ReadAllLines(replicaPath);
        Assert.True(moduleLines.Length == replicaLines.Length, $"{relativePath} differs from its replica in line count");
        foreach (var (moduleLine, replicaLine) in moduleLines.Zip(replicaLines))
        {
            if (moduleLine == replicaLine)
            {
                continue;
            }

            var bothAreBodyHeaders = moduleLine.TrimStart().StartsWith("body:", StringComparison.OrdinalIgnoreCase)
                && replicaLine.TrimStart().StartsWith("body:", StringComparison.OrdinalIgnoreCase);
            Assert.True(bothAreBodyHeaders, $"{relativePath} differs from its replica beyond a body: header: '{moduleLine}' vs '{replicaLine}'");
        }
    }

    private static string ReplicaOf(string relativePath)
    {
        var segments = relativePath.Replace(IoPath.DirectorySeparatorChar, '/').Split('/');

        // diagrams/<module>/examples/<rest> or editors/<editor>/examples/<rest>.
        return string.Join("/", new[] { "examples", segments[0], segments[1] }.Concat(segments[3..]));
    }
}
