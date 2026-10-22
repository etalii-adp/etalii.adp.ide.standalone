using System.Text;
using System.Text.Json;
using EtAlii.Adp.Diagram;
using Xunit;

namespace EtAlii.Adp.Tests;

/// <summary>
/// The frame every module's parity transcript is written in: the header, the toolbox, a document
/// recorded once and then as <c>sameAs</c>, a scratch folder of its own for each, and the closing
/// line ending. A module's transcript test sees a change to the frame only where its corpus happens
/// to exercise it; these see it for every module at once.
/// </summary>
public class TranscriptHarnessTests
{
    [Fact]
    public async Task TheHeader_NamesTheModule_TheAdapterAsGenerator_AndHowToRegenerate()
    {
        // Act.
        var root = await GenerateAsync(new Adapter());

        // Assert.
        Assert.Equal("etalii/fake", root.GetProperty("module").GetString());
        Assert.Equal("src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Fake.Tests/Parity/Adapter.cs", root.GetProperty("generator").GetString());
        Assert.Equal("ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)", root.GetProperty("regenerate").GetString());
        Assert.Equal(["add-box | Box | square | drops add-box-here | A box."], root.GetProperty("toolbox").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task ADocumentAlreadyRecorded_IsWrittenAsSameAsTheFirst_AndNotRecordedAgain()
    {
        // Arrange.
        var adapter = new Adapter();

        // Act.
        var documents = (await GenerateAsync(adapter)).GetProperty("documents").EnumerateArray().ToList();

        // Assert.
        Assert.Equal(["first", "second", "copy"], documents.Select(document => document.GetProperty("document").GetString()));
        Assert.Equal(["recorded first", "recorded second"], documents.Take(2).Select(document => document.GetProperty("recorded").GetString()));
        Assert.Equal("first", documents[2].GetProperty("sameAs").GetString());
        Assert.False(documents[2].TryGetProperty("recorded", out _));
        Assert.Equal(TranscriptText.Sha256("one"u8), documents[2].GetProperty("sha256").GetString());
        Assert.Equal(["first", "second"], adapter.Written);
    }

    [Fact]
    public async Task EachDocument_GetsAnEmptyScratchFolderOfItsOwn_DeletedAfterwards()
    {
        // Arrange.
        var adapter = new Adapter();

        // Act.
        await TranscriptHarness.GenerateAsync(adapter, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(2, adapter.Folders.Distinct(StringComparer.Ordinal).Count());
        Assert.All(adapter.Folders, folder => Assert.False(Directory.Exists(folder), $"{folder} was left behind"));
        Assert.All(adapter.EmptyOnEntry, Assert.True);
    }

    [Fact]
    public async Task TheTranscript_EndsInOneLineEnding_AsGitChecksAJsonFileOut()
    {
        // Act.
        var bytes = await TranscriptHarness.GenerateAsync(new Adapter(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.EndsWith("}\r\n", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n\r\n", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    private static async Task<JsonElement> GenerateAsync(TranscriptAdapter adapter)
    {
        var bytes = await TranscriptHarness.GenerateAsync(adapter, TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }

    /// <summary>A module of three inline documents, the third a copy of the first, in a real module's folder.</summary>
    private sealed class Adapter() : TranscriptAdapter("mindmap", "EtAlii.Adp.Diagram.Fake.Tests", "fake.transcript.json")
    {
        public List<string> Written { get; } = [];

        public List<string> Folders { get; } = [];

        public List<bool> EmptyOnEntry { get; } = [];

        public override string Origin => "etalii/fake";

        public override IReadOnlyList<ToolboxItemDefinition> Toolbox { get; } = [new("add-box", "Box", "square", "A box.", "add-box-here")];

        public override IReadOnlyList<TranscriptDocument> Corpus() => [Inline("first", "one"), Inline("second", "two"), Inline("copy", "one")];

        public override async Task WriteDocumentAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, string folder, CancellationToken cancellationToken)
        {
            Written.Add(document.Name);
            Folders.Add(folder);
            EmptyOnEntry.Add(Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any());
            await File.WriteAllBytesAsync(Path.Combine(folder, "body"), bytes, cancellationToken);
            json.WriteString("recorded", $"recorded {document.Name}");
        }

        private static TranscriptDocument Inline(string name, string text) => new(name, _ => Task.FromResult(Encoding.UTF8.GetBytes(text)));
    }
}
