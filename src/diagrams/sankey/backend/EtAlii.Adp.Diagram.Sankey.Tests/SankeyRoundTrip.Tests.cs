using EtAlii.Adp.Diagram.Sankey.Tests.Parity;
using EtAlii.Adp.Files;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// The bytes of a <c>.skv</c> survive being read through the binding (sankey-disl-fbl task 10): every document of the
/// corpus opened and saved without a change is written back as it was read, and reading it changes nothing of it.
/// </summary>
public class SankeyRoundTripTests
{
    public static TheoryData<string> Corpus() => SankeyCorpus.Names();

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task ADocumentSavedUnchanged_IsWrittenAsItWasRead(string name)
    {
        // Arrange.
        var source = new SankeyTranscript().Corpus().Single(candidate => candidate.Name == name);
        var original = await source.Read(TestContext.Current.CancellationToken);
        using var folder = TestFolder.Create("EtAlii.Adp.SankeyRoundTrip");
        var body = Path.Combine(folder.Path, "body.skv");
        await File.WriteAllBytesAsync(body, original, TestContext.Current.CancellationToken);
        var store = new SankeyDocumentStore(new FileWriter());

        // Act.
        var entry = await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken);
        var read = SankeyBody.Parse(entry.Document.Text);
        var saved = await store.SaveAsync(body, entry.Document, TestContext.Current.CancellationToken);

        // Assert: the reading holds the text as it was, and a body that cannot be read is not written at all.
        Assert.Equal(TranscriptText.Decode(original), read.Text);
        Assert.True(saved.Error.Length == 0 || !entry.IsUsable, saved.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEntryTheToolDoesNotDraw_HasNoIdWritten_AndCountsTowardsNothing()
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "undrawn-entries.skv"), TestContext.Current.CancellationToken);

        // Act.
        var model = SankeyReader.Parse(text);
        var layout = SankeyLayout.Of(model);

        // Assert: a node without an id and a later holder of one are read and kept, not drawn, and given no id (Q5).
        Assert.Contains(model.Nodes, node => node.Id.Length == 0);
        Assert.True(model.Nodes.Count > layout.Nodes.Count);
        Assert.Equal(text, SankeyReader.BodyOf(model)!.Text);
    }
}
