using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Markdown.Tests;

/// <summary>
/// The module's shipped examples, opened by the module's own code (modular-text-editors
/// Requirement 10.4). Live walk: a file added to <c>examples/</c> is covered on arrival,
/// and one nothing opens cannot exist here.
/// </summary>
public class ExamplesTests
{
    private static string ExamplesRoot { get; } = Locate("src", "editors", "markdown", "examples");

    /// <summary>The combined project's replica of this module's examples (structure.md's replication rule).</summary>
    private static string ReplicaRoot { get; } = Locate("src", "examples", "editors", "markdown");

    private static string Locate(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine([directory.FullName, .. segments]);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"The {string.Join('/', segments)} folder was not found above the test binary.");
    }

    public static TheoryData<string> EveryExampleFile()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(ExamplesRoot, "*", SearchOption.AllDirectories))
        {
            data.Add(IoPath.GetRelativePath(ExamplesRoot, file));
        }

        return data;
    }

    [Fact]
    public void TheWalk_FindsTheTrackedExampleSet()
    {
        // Arrange and act: 2 files at the time of writing; shrinking unexpectedly is the bug.
        Assert.True(EveryExampleFile().Count >= 2, "the examples walk lost files");
    }

    [Theory]
    [MemberData(nameof(EveryExampleFile))]
    public async Task EveryExample_OpensInThisEditor_AndRoundTripsByteIdentically(string relativePath)
    {
        // Arrange: a temp copy, so the unchanged-save proof never writes into the source tree.
        var source = IoPath.Combine(ExamplesRoot, relativePath);
        var temp = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"), IoPath.GetFileName(relativePath));
        Directory.CreateDirectory(IoPath.GetDirectoryName(temp)!);
        try
        {
            var original = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(temp, original, TestContext.Current.CancellationToken);

            // Act: the module's own session opens it, and what it read is written back the way
            // every editor's save writes - the shared save command's TextFileBuffer.
            await using var session = new MarkdownEditorSession(temp);
            Assert.Equal("", session.Refusal);
            var opened = TextFileBuffer.Open(temp);
            Assert.NotNull(opened.Buffer);
            var error = await opened.Buffer.SaveAsync(session.Content, TestContext.Current.CancellationToken);

            // Assert (Requirement 10.2: everything plain guarantees, this module guarantees).
            Assert.Equal("", error);
            Assert.Equal(original, await File.ReadAllBytesAsync(temp, TestContext.Current.CancellationToken));
        }
        finally
        {
            TestFolder.TryDelete(IoPath.GetDirectoryName(temp)!);
        }
    }

    [Fact]
    public void TheGuide_IsHeadingRich()
    {
        // Arrange and act: what makes this file this module's example rather than plain's -
        // several heading levels, and a fenced `#` that must not read as one.
        var content = File.ReadAllText(IoPath.Combine(ExamplesRoot, "guide.md"));

        // Assert.
        Assert.Contains("# Field guide", content);
        Assert.Contains("## Reading the preview", content);
        Assert.Contains("### Emphasis", content);
        Assert.Contains("```bash", content);
    }

    [Theory]
    [MemberData(nameof(EveryExampleFile))]
    public async Task EveryExample_HasAByteIdenticalReplicaInTheCombinedProject(string relativePath)
    {
        // Arrange: structure.md's must-never-drift rule; byte-identical because a text
        // editor's example has no project-relative `body:` header to rewrite.
        var moduleCopy = IoPath.Combine(ExamplesRoot, relativePath);
        var replica = IoPath.Combine(ReplicaRoot, relativePath);

        // Act and assert.
        Assert.True(File.Exists(replica), $"{relativePath} has no replica under src/examples/editors/markdown");
        Assert.Equal(
            await File.ReadAllBytesAsync(moduleCopy, TestContext.Current.CancellationToken),
            await File.ReadAllBytesAsync(replica, TestContext.Current.CancellationToken));
    }
}
