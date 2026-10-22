using EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
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
        List<string> failures = [];
        var applied = 0;

        // Act and assert.
        foreach (var type in FdgElementTypes.All)
        {
            await Check(new AddFdgElementCommand(body, type, 240, 160));
        }

        foreach (var element in model.Elements.Where(element => element.Id.Length > 0).Where((element, index) => index < Sample || element.IsComment).DistinctBy(element => element.Id, StringComparer.Ordinal))
        {
            await Check(new SetFdgPlacementCommand(body, element.Id, element.X + 10.5, element.Y - 4E-05));
            await Check(new SetFdgSizeCommand(body, element.Id, 10, element.IsComment ? 10 : null));
            await Check(new SetFdgSizeCommand(body, element.Id, 300.25, element.IsComment ? 200 : null));
            await Check(new RenameFdgElementCommand(body, element.Id, "Round trip"));
            await Check(new RenameFdgElementCommand(body, element.Id, " leading space\nand a second line"));
            await Check(new SetFdgDescriptionCommand(body, element.Id, "Described: here # and there"));
            await Check(new SetFdgDescriptionCommand(body, element.Id, ""));
            await Check(new RemoveFdgElementCommand(body, element.Id));
        }

        foreach (var connection in model.Connections.Where(connection => connection.Id.Length > 0).DistinctBy(connection => connection.Id, StringComparer.Ordinal).Take(Sample))
        {
            await Check(new RenameFdgConnectionCommand(body, connection.Id, "Round trip"));
            await Check(new SetFdgDescriptionCommand(body, connection.Id, "A link's description"));
            await Check(new SetFdgDescriptionCommand(body, connection.Id, ""));
            await Check(new DisconnectFdgConnectionCommand(body, connection.Id));
        }

        var ends = model.Elements.Where(element => element.Id.Length > 0).Take(Sample + 1).Select(element => element.Id).ToList();
        foreach (var type in FdgConnectionTypes.All)
        {
            foreach (var from in ends)
            {
                foreach (var to in ends.Where(to => to != from))
                {
                    await Check(new ConnectFdgElementsCommand(body, type, from, to));
                }
            }
        }

        Assert.True(failures.Count == 0, $"{relative}: {failures.Count} of {applied} edits\n{string.Join("\n", failures)}");
        // An empty file has no section to add to, so it takes no edit; every other readable one takes some.
        if (original.Length > 0 && !FdgBody.Parse(await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken)).IsUnreadable) Assert.True(applied > 0, $"{relative}: no edit applied, so nothing was undone.");
        return;

        async Task Check(ICommand command)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await File.WriteAllBytesAsync(body, original, cancellationToken);
            var dispatcher = new FdgTestDispatcher(new FdgDocumentStore(new FileWriter()));
            var done = await dispatcher.DispatchAsync(command, cancellationToken);
            if (done is not { IsSuccess: true, Inverse: not null }) return;

            applied++;
            var edited = await File.ReadAllBytesAsync(body, cancellationToken);
            var undone = await dispatcher.DispatchAsync(done.Inverse, cancellationToken);
            var afterUndo = await File.ReadAllBytesAsync(body, cancellationToken);
            if (!undone.IsSuccess)
            {
                failures.Add($"{command}: its undo was refused: {undone.Error}");
            }
            else if (!afterUndo.AsSpan().SequenceEqual(original))
            {
                failures.Add($"{command}: undone, the file is not what it was ({(edited.AsSpan().SequenceEqual(original) ? "the edit wrote nothing" : "the edit wrote")})");
            }
        }
    }

    private static Dictionary<string, object?> Values(string attribute, DislElement element) =>
        new(StringComparer.Ordinal) { [attribute] = element.Attributes.GetValueOrDefault(attribute) };

    /// <summary>A temporary folder holding copies of corpus documents, removed afterwards.</summary>
    private sealed class Folder : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.FdgRoundTripTests", Guid.NewGuid().ToString("N"));

        public Folder() => Directory.CreateDirectory(_path);

        /// <summary>Copies the corpus document at <paramref name="relative"/> into the folder, byte for byte, and gives its path.</summary>
        public string Copy(string relative)
        {
            var copy = Path.Combine(_path, Path.GetFileName(relative));
            File.Copy(Path.Combine(FdgTranscript.SourceFolder, relative), copy);
            return copy;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A temp folder left behind is not a test failure.
            }
        }
    }
}
