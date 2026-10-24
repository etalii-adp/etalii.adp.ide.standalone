using EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Disl;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// The bytes of a graph survive the tool (functional-decomposition-graph-disl-fbl task 10): every document of the
/// parity corpus saved without a change is written back as it was read, and every edit undone gives the file back
/// byte for byte, through the real store on a real file.
/// </summary>
/// <remarks>
/// The undo is the shared restore command, which writes back the text the edit replaced, so what is checked is that
/// each command's edit is one the restore takes back whole, on documents written in every style the corpus holds:
/// CRLF and LF, no final newline, comments above and after entries, quoted and block scalars.
/// </remarks>
public sealed class FdgRoundTripTests
{
    private const int Sample = 2;

    public static TheoryData<string> Documents() => [.. FdgTranscript.Corpus()];

    [Fact]
    public void TheCorpus_HoldsTheDocumentsWhoseEndingsAreTheTestSubject()
    {
        // Assert: the line-ending pair and the document without a final newline, so the theories below read them.
        Assert.Contains(FdgTranscript.Corpus(), path => path.EndsWith("/Fixtures/crlf-line-endings.fdg", StringComparison.Ordinal));
        Assert.Contains(FdgTranscript.Corpus(), path => path.EndsWith("/Fixtures/lf-line-endings.fdg", StringComparison.Ordinal));
        Assert.Contains(FdgTranscript.Corpus(), path => path.EndsWith("/Fixtures/no-trailing-newline.fdg", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task ADocumentSavedUnchanged_IsWrittenAsItWasRead(string relative)
    {
        // Arrange.
        using var folder = new Folder();
        var body = folder.Copy(relative);
        var original = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var store = new FdgDocumentStore(new FileWriter());
        var entry = await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken);

        // Act: every element's name or text set to what it already is.
        var same = entry.Document.IsUnreadable
            ? []
            : entry.Model.Diagram.Elements
                .Select(element => FdgDefinition.Set(entry.Document, element, element.Type.Name == "Comment" ? Values("text", element) : Values("name", element)))
                .ToList();
        var saved = await store.SaveAsync(body, entry.Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.All(same, outcome => Assert.True(outcome.WasApplied, outcome.Refusal));
        Assert.Equal(original, entry.Document.Bytes);
        Assert.True(saved.Error.Length == 0 || entry.Document.IsUnreadable, saved.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryEditUndone_GivesTheBytesBack(string relative)
    {
        // Arrange.
        using var folder = new Folder();
        var body = folder.Copy(relative);
        var original = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var model = FdgParser.Parse(LineDocument.Parse(await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken)));
        var roundTrip = new UndoRoundTrip(body, original, () => new FdgTestDispatcher(new FdgDocumentStore(new FileWriter())));

        // Act and assert.
        foreach (var type in FdgElementTypes.All)
        {
            await roundTrip.CheckAsync(new AddFdgElementCommand(body, type, 240, 160), TestContext.Current.CancellationToken);
        }

        foreach (var element in model.Elements.Where(element => element.Id.Length > 0).Where((element, index) => index < Sample || element.IsComment).DistinctBy(element => element.Id, StringComparer.Ordinal))
        {
            await roundTrip.CheckAsync(new SetFdgPlacementCommand(body, element.Id, element.X + 10.5, element.Y - 4E-05), TestContext.Current.CancellationToken);
            await roundTrip.CheckAsync(new SetFdgSizeCommand(body, element.Id, 10, element.IsComment ? 10 : null), TestContext.Current.CancellationToken);
            await roundTrip.CheckAsync(new SetFdgSizeCommand(body, element.Id, 300.25, element.IsComment ? 200 : null), TestContext.Current.CancellationToken);
            await roundTrip.CheckAsync(new RenameFdgElementCommand(body, element.Id, "Round trip"), TestContext.Current.CancellationToken);
            await roundTrip.CheckAsync(new RenameFdgElementCommand(body, element.Id, " leading space\nand a second line"), TestContext.Current.CancellationToken);
            await roundTrip.CheckAsync(new SetFdgDescriptionCommand(body, element.Id, "Described: here # and there"), TestContext.Current.CancellationToken);
            await roundTrip.CheckAsync(new SetFdgDescriptionCommand(body, element.Id, ""), TestContext.Current.CancellationToken);
            await roundTrip.CheckAsync(new RemoveFdgElementCommand(body, element.Id), TestContext.Current.CancellationToken);
        }

        foreach (var connection in model.Connections.Where(connection => connection.Id.Length > 0).DistinctBy(connection => connection.Id, StringComparer.Ordinal).Take(Sample))
        {
            await roundTrip.CheckAsync(new RenameFdgConnectionCommand(body, connection.Id, "Round trip"), TestContext.Current.CancellationToken);
            await roundTrip.CheckAsync(new SetFdgDescriptionCommand(body, connection.Id, "A link's description"), TestContext.Current.CancellationToken);
            await roundTrip.CheckAsync(new SetFdgDescriptionCommand(body, connection.Id, ""), TestContext.Current.CancellationToken);
            await roundTrip.CheckAsync(new DisconnectFdgConnectionCommand(body, connection.Id), TestContext.Current.CancellationToken);
        }

        var ends = model.Elements.Where(element => element.Id.Length > 0).Take(Sample + 1).Select(element => element.Id).ToList();
        foreach (var type in FdgConnectionTypes.All)
        {
            foreach (var from in ends)
            {
                foreach (var to in ends.Where(to => to != from))
                {
                    await roundTrip.CheckAsync(new ConnectFdgElementsCommand(body, type, from, to), TestContext.Current.CancellationToken);
                }
            }
        }

        Assert.True(roundTrip.Failures.Count == 0, $"{relative}: {roundTrip.Failures.Count} of {roundTrip.Applied} edits\n{roundTrip}");
        // An empty file has no section to add to, so it takes no edit; every other readable one takes some.
        if (original.Length > 0 && !FdgBody.Parse(await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken)).IsUnreadable) Assert.True(roundTrip.Applied > 0, $"{relative}: no edit applied, so nothing was undone.");
    }

    private static Dictionary<string, object?> Values(string attribute, DislElement element) =>
        new(StringComparer.Ordinal) { [attribute] = element.Attributes.GetValueOrDefault(attribute) };

    /// <summary>A temporary folder holding copies of corpus documents, removed afterwards.</summary>
    private sealed class Folder : IDisposable
    {
        private readonly TestFolder _testFolder = TestFolder.Create("EtAlii.Adp.FdgRoundTripTests");
        private readonly string _path;

        public Folder() => _path = _testFolder.Path;

        /// <summary>Copies the corpus document at <paramref name="relative"/> into the folder, byte for byte, and gives its path.</summary>
        public string Copy(string relative)
        {
            var copy = Path.Combine(_path, Path.GetFileName(relative));
            File.Copy(Path.Combine(FdgTranscript.SourceFolder, relative), copy);
            return copy;
        }

        public void Dispose()
        {
            _testFolder.Dispose();
        }
    }
}
