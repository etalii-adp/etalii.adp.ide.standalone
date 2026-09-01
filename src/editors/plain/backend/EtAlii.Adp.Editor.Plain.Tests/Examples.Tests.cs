using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Plain.Tests;

/// <summary>
/// The module's shipped examples, opened by the module's own code (modular-text-editors
/// Requirement 10.4: "the examples cannot drift from what the code supports"). The walk is
/// live, so a file added to <c>examples/</c> is covered on arrival - and a file nothing
/// opens cannot exist here, which is exactly what R10.4 forbids.
/// </summary>
public class ExamplesTests
{
    /// <summary>
    /// The module's <c>examples/</c> folder, found by walking up from the test binary rather
    /// than by counting <c>..</c> segments - the count changes with the build layout, the
    /// folder name does not (the shape ExampleRegistrationTests already uses).
    /// </summary>
    private static string ExamplesRoot { get; } = Locate("src", "editors", "plain", "examples");

    /// <summary>The combined project's replica of this module's examples (structure.md's replication rule).</summary>
    private static string ReplicaRoot { get; } = Locate("src", "examples", "plain");

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
        // Arrange and act: 3 files at the time of writing. The count moving down unexpectedly
        // is what this guards - a vanished example silently shrinks the theories below.
        Assert.True(EveryExampleFile().Count() >= 3, "the examples walk lost files");
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

            // Act: the module's own session - open, then save what was opened.
            await using var session = new PlainEditorSession(temp);
            Assert.Equal("", session.Refusal);
            var error = await session.SaveAsync(session.Content, TestContext.Current.CancellationToken);

            // Assert: the family's headline guarantee, held by the shipped material itself.
            Assert.Equal("", error);
            Assert.Equal(original, await File.ReadAllBytesAsync(temp, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(IoPath.GetDirectoryName(temp)!, recursive: true);
        }
    }

    [Fact]
    public void TheCrlfExample_IsDetectedAsCrlf()
    {
        // Arrange and act: the edge case this example exists to exercise. `.gitattributes`
        // (* text=auto eol=crlf) guarantees the working-tree bytes are CRLF on any machine.
        var buffer = TextFileBuffer.Open(IoPath.Combine(ExamplesRoot, "crlf-notes.txt")).Buffer;

        // Assert.
        Assert.NotNull(buffer);
        Assert.Equal("CRLF", buffer.LineEndingStyle);
    }

    [Fact]
    public void TheBomExample_IsDetectedAsUtf8WithBom()
    {
        // Arrange and act: git never touches a BOM, so this byte survives checkout untouched.
        var buffer = TextFileBuffer.Open(IoPath.Combine(ExamplesRoot, "utf8-bom-notes.txt")).Buffer;

        // Assert.
        Assert.NotNull(buffer);
        Assert.Equal("UTF-8 with BOM", buffer.EncodingName);
    }

    [Theory]
    [MemberData(nameof(EveryExampleFile))]
    public async Task EveryExample_HasAByteIdenticalReplicaInTheCombinedProject(string relativePath)
    {
        // Arrange: structure.md's rule - a module's examples and the combined project's copy
        // must never drift. Byte-identical is achievable here because a text editor's example
        // has no project-relative `body:` header to rewrite (unlike a diagram's `.adp`).
        var moduleCopy = IoPath.Combine(ExamplesRoot, relativePath);
        var replica = IoPath.Combine(ReplicaRoot, relativePath);

        // Act and assert.
        Assert.True(File.Exists(replica), $"{relativePath} has no replica under src/examples/plain");
        Assert.Equal(
            await File.ReadAllBytesAsync(moduleCopy, TestContext.Current.CancellationToken),
            await File.ReadAllBytesAsync(replica, TestContext.Current.CancellationToken));
    }
}
