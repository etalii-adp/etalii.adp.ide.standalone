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

    [Fact]
    public async Task TheDocumentsArray_AndTheFieldNamingEachEntry_AreNamedByTheAdapter()
    {
        // Act.
        var root = await GenerateAsync(new Named());

        // Assert.
        Assert.False(root.TryGetProperty("documents", out _));
        var solutions = root.GetProperty("solutions").EnumerateArray().ToList();
        Assert.Equal(["first", "second", "copy"], solutions.Select(solution => solution.EnumerateObject().First().Value.GetString()));
        Assert.All(solutions, solution => Assert.Equal("solution", solution.EnumerateObject().First().Name));
        Assert.Equal("first", solutions[2].GetProperty("sameAs").GetString());
    }

    [Fact]
    public async Task WhatIsRecordedOnce_IsWrittenBeforeTheDocuments_WithTheToolboxWhereTheAdapterPutsIt()
    {
        // Act.
        var plain = await GenerateAsync(new Adapter());
        var around = await GenerateAsync(new Around());

        // Assert.
        Assert.Equal(["module", "generator", "regenerate", "toolbox", "documents"], plain.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["module", "generator", "regenerate", "registrations", "toolbox", "emptyDocument", "documents"], around.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["add-box | Box | square | drops add-box-here | A box."], around.GetProperty("toolbox").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task ATranscriptWithoutAToolbox_HasNoToolboxField()
    {
        // Act.
        var root = await GenerateAsync(new Toolless());

        // Assert.
        Assert.Equal(["module", "generator", "regenerate", "documents"], root.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task WhatIdentifiesADocument_IsWrittenByTheAdapter_AndDecidesWhichIsSameAs()
    {
        // Arrange.
        var adapter = new Identified();

        // Act.
        var documents = (await GenerateAsync(adapter)).GetProperty("documents").EnumerateArray().ToList();

        // Assert: equal bytes in another folder are another reading, and a document with no identity is never the same as one.
        Assert.Equal(["first", "elsewhere", "made", "made again"], adapter.Written);
        Assert.Equal(["document", "sha256", "folder", "sameAs"], documents[2].EnumerateObject().Select(property => property.Name));
        Assert.Equal("first", documents[2].GetProperty("sameAs").GetString());
        Assert.Equal(["document", "tree", "recorded"], documents[4].EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task WhatFollowsTheDocuments_IsWrittenAfterThem_AndLast()
    {
        // Act.
        var root = await GenerateAsync(new Followed());

        // Assert.
        Assert.Equal(["module", "generator", "regenerate", "toolbox", "documents", "graphs"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["empty"], root.GetProperty("graphs").EnumerateArray().Select(graph => graph.GetString()));
    }

    private static async Task<JsonElement> GenerateAsync(TranscriptAdapter adapter)
    {
        var bytes = await TranscriptHarness.GenerateAsync(adapter, TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }

    /// <summary>A module of three inline documents, the third a copy of the first, in a real module's folder.</summary>
    private class Adapter() : TranscriptAdapter("mindmap", "EtAlii.Adp.Diagram.Fake.Tests", "fake.transcript.json")
    {
        public List<string> Written { get; } = [];

        public List<string> Folders { get; } = [];

        public List<bool> EmptyOnEntry { get; } = [];

        public override string Origin => "etalii/fake";

        protected override IReadOnlyList<ToolboxItemDefinition>? Toolbox { get; } = [new("add-box", "Box", "square", "A box.", "add-box-here")];

        public override IReadOnlyList<TranscriptDocument> Corpus() => [Inline("first", "one"), Inline("second", "two"), Inline("copy", "one")];

        public override async Task WriteDocumentAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, string folder, CancellationToken cancellationToken)
        {
            Written.Add(document.Name);
            Folders.Add(folder);
            EmptyOnEntry.Add(Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any());
            await File.WriteAllBytesAsync(Path.Combine(folder, "body"), bytes, cancellationToken);
            json.WriteString("recorded", $"recorded {document.Name}");
        }

        protected static TranscriptDocument Inline(string name, string text) => new(name, _ => Task.FromResult(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>A module whose transcript names its documents solutions.</summary>
    private sealed class Named : Adapter
    {
        public override string DocumentsName => "solutions";

        public override string DocumentName => "solution";
    }

    /// <summary>A module that records its registrations before the toolbox and its empty document after it.</summary>
    private sealed class Around : Adapter
    {
        public override async Task WriteBeforeDocumentsAsync(Utf8JsonWriter json, CancellationToken cancellationToken)
        {
            json.WritePropertyName("registrations");
            json.WriteLines(["IThing => Thing (Singleton)"]);
            await base.WriteBeforeDocumentsAsync(json, cancellationToken);
            json.WriteString("emptyDocument", "empty");
        }
    }

    /// <summary>A module with no toolbox to record.</summary>
    private sealed class Toolless : Adapter
    {
        protected override IReadOnlyList<ToolboxItemDefinition>? Toolbox => null;
    }

    /// <summary>A module that records a graph of its own after the documents.</summary>
    private sealed class Followed : Adapter
    {
        public override Task WriteAfterDocumentsAsync(Utf8JsonWriter json, CancellationToken cancellationToken)
        {
            json.WritePropertyName("graphs");
            json.WriteLines(["empty"]);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A module whose documents are read in a folder, so equal bytes in another folder are another
    /// reading, and two of which are made from nothing and have no identity at all.
    /// </summary>
    private sealed class Identified : Adapter
    {
        public override IReadOnlyList<TranscriptDocument> Corpus() =>
        [
            new InFolder("first", "a", Inline("first", "one").Read),
            new InFolder("elsewhere", "b", Inline("elsewhere", "one").Read),
            new InFolder("copy", "a", Inline("copy", "one").Read),
            new InFolder("made", null, Inline("made", "").Read),
            new InFolder("made again", null, Inline("made again", "").Read),
        ];

        public override async Task<string?> WriteIdentityAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, CancellationToken cancellationToken)
        {
            if (((InFolder)document).Folder is not { } folder)
            {
                json.WriteString("tree", "none; made from nothing");
                return null;
            }

            var sha = await base.WriteIdentityAsync(json, document, bytes, cancellationToken);
            json.WriteString("folder", folder);
            return $"{sha} {folder}";
        }

        private sealed record InFolder(string Name, string? Folder, Func<CancellationToken, Task<byte[]>> Read) : TranscriptDocument(Name, Read);
    }
}
