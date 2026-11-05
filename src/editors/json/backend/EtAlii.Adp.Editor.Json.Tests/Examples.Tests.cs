using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Json.Tests;

/// <summary>
/// The module's shipped examples, opened by the module's own code (json-editor Requirement 8.5).
/// Live walk: a file added to <c>examples/</c> is covered on arrival, and one nothing opens
/// cannot exist here.
/// </summary>
public class ExamplesTests
{
    private static string ExamplesRoot { get; } = RepositoryPaths.Under("src", "editors", "json", "examples");

    /// <summary>The combined project's replica of this module's examples (structure.md's replication rule).</summary>
    private static string ReplicaRoot { get; } = RepositoryPaths.Under("src", "examples", "editors", "json");

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
        // Arrange and act: 7 files at the time of writing; shrinking unexpectedly is the bug.
        Assert.True(EveryExampleFile().Count >= 7, "the examples walk lost files");
    }

    [Theory]
    [MemberData(nameof(EveryExampleFile))]
    public async Task EveryExample_OpensInThisEditor_AndRoundTripsByteIdentically(string relativePath)
    {
        // Arrange: a temp copy, so the unchanged-save proof never writes into the source tree.
        var source = IoPath.Combine(ExamplesRoot, relativePath);
        using var folder = TestFolder.Create("json-example");
        var temp = folder.Combine(IoPath.GetFileName(relativePath));
        var original = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(temp, original, TestContext.Current.CancellationToken);

        // Act: the module's own session opens it, and what it read is written back the way
        // every editor's save writes - the shared save command's TextFileBuffer.
        await using var session = new JsonEditorSession(temp);
        Assert.Equal("", session.Refusal);
        var opened = TextFileBuffer.Open(temp);
        Assert.NotNull(opened.Buffer);
        var error = await opened.Buffer.SaveAsync(session.Content, new FileWriter(), TestContext.Current.CancellationToken);

        // Assert (Requirement 8.1: everything plain guarantees, this module guarantees).
        Assert.Equal("", error);
        Assert.Equal(original, await File.ReadAllBytesAsync(temp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheLineEndingExamples_AreWhatTheirNamesSay()
    {
        // Arrange and act: the round trip above proves nothing about CRLF or a byte-order mark
        // unless the files carry them, and git would rewrite the endings but for .gitattributes.
        var crlf = await File.ReadAllBytesAsync(IoPath.Combine(ExamplesRoot, "crlf-settings.json"), TestContext.Current.CancellationToken);
        var bom = await File.ReadAllBytesAsync(IoPath.Combine(ExamplesRoot, "utf8-bom-settings.json"), TestContext.Current.CancellationToken);
        var lf = await File.ReadAllBytesAsync(IoPath.Combine(ExamplesRoot, "service.json"), TestContext.Current.CancellationToken);

        // Assert: every line feed of the CRLF example follows a carriage return; the LF example
        // holds none; the BOM example starts with one and has no final newline.
        var crlfFeeds = Enumerable.Range(0, crlf.Length).Where(index => crlf[index] == (byte)'\n').ToArray();
        Assert.NotEmpty(crlfFeeds);
        Assert.All(crlfFeeds, index => Assert.Equal((byte)'\r', crlf[index - 1]));
        Assert.DoesNotContain((byte)'\r', lf);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bom[..3]);
        Assert.NotEqual((byte)'\n', bom[^1]);
    }

    [Fact]
    public void TheBrokenExample_NamesTheSchemaItBreaks()
    {
        // Arrange and act: the pair the browser checks open; renaming one without the other
        // would leave the broken example with no schema and nothing to break.
        var content = File.ReadAllText(IoPath.Combine(ExamplesRoot, "service-broken.json"));

        // Assert.
        Assert.Contains("\"$schema\": \"./service.schema.json\"", content, StringComparison.Ordinal);
        Assert.True(File.Exists(IoPath.Combine(ExamplesRoot, "service.schema.json")));
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
        Assert.True(File.Exists(replica), $"{relativePath} has no replica under src/examples/editors/json");
        Assert.Equal(
            await File.ReadAllBytesAsync(moduleCopy, TestContext.Current.CancellationToken),
            await File.ReadAllBytesAsync(replica, TestContext.Current.CancellationToken));
    }
}
