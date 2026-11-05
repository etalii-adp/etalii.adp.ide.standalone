using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>
/// The module's shipped examples, opened by the module's own code (modular-text-editors
/// Requirement 10.4). Live walk: a file added to <c>examples/</c> is covered on arrival,
/// and one nothing opens cannot exist here.
/// </summary>
public class ExamplesTests
{
    private static string ExamplesRoot { get; } = RepositoryPaths.Under("src", "editors", "xml", "examples");

    /// <summary>The combined project's replica of this module's examples (structure.md's replication rule).</summary>
    private static string ReplicaRoot { get; } = RepositoryPaths.Under("src", "examples", "editors", "xml");

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
        // Arrange and act: 11 files at the time of writing; shrinking unexpectedly is the bug.
        Assert.True(EveryExampleFile().Count >= 11, "the examples walk lost files");
    }

    [Theory]
    [MemberData(nameof(EveryExampleFile))]
    public async Task EveryExample_OpensInThisEditor_AndRoundTripsByteIdentically(string relativePath)
    {
        // Arrange: a temp copy, so the unchanged-save proof never writes into the source tree.
        var source = IoPath.Combine(ExamplesRoot, relativePath);
        using var folder = TestFolder.Create("xml-example");
        var temp = folder.Combine(IoPath.GetFileName(relativePath));
        var original = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(temp, original, TestContext.Current.CancellationToken);

        // Act: the module's own session opens it, and what it read is written back the way
        // every editor's save writes - the shared save command's TextFileBuffer.
        await using var session = new XmlEditorSession(temp);
        Assert.Equal("", session.Refusal);
        var opened = TextFileBuffer.Open(temp);
        Assert.NotNull(opened.Buffer);
        var error = await opened.Buffer.SaveAsync(session.Content, new FileWriter(), TestContext.Current.CancellationToken);

        // Assert (xml-editor Requirements 8.1 and 8.5: everything plain guarantees, this module guarantees).
        Assert.Equal("", error);
        Assert.Equal(original, await File.ReadAllBytesAsync(temp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheLatin1Example_DeclaresTheEncodingItIsStoredIn()
    {
        // Arrange and act: Requirement 8.5's example in an encoding other than UTF-8. Its bytes
        // are the ASCII range ISO-8859-1 and UTF-8 share, which the family's buffer opens.
        var bytes = await File.ReadAllBytesAsync(IoPath.Combine(ExamplesRoot, "latin1.xml"), TestContext.Current.CancellationToken);
        var text = System.Text.Encoding.Latin1.GetString(bytes);

        // Assert.
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>", text, StringComparison.Ordinal);
        Assert.All(bytes, value => Assert.True(value < 0x80));
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
        Assert.True(File.Exists(replica), $"{relativePath} has no replica under src/examples/editors/xml");
        Assert.Equal(
            await File.ReadAllBytesAsync(moduleCopy, TestContext.Current.CancellationToken),
            await File.ReadAllBytesAsync(replica, TestContext.Current.CancellationToken));
    }
}
