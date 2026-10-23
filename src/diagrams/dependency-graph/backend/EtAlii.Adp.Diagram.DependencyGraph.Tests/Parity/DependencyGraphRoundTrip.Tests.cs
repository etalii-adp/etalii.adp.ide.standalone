using EtAlii.Adp.Files;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests.Parity;

/// <summary>
/// Every document of the parity corpus, read and stored without a change, gives no splice and the same bytes
/// (dependency-graph-disl-fbl, task 8, Requirement 3.7): the pair of line-ending fixtures and the one without a final
/// newline included. A document that cannot be read is not stored at all, so it has no round trip to hold.
/// </summary>
public class DependencyGraphRoundTripTests
{
    private static readonly DependencyGraphTranscript _transcript = new();

    public static TheoryData<string> Documents()
    {
        var data = new TheoryData<string>();
        foreach ((string name, _) in _transcript.Corpus()) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task ADocumentReadAndStoredWithoutAChange_IsTheSameBytes(string name)
    {
        // Arrange.
        var bytes = await _transcript.Corpus().Single(document => document.Name == name).Read(TestContext.Current.CancellationToken);
        using var folder = TestFolder.Create("dependency-graph-round-trip");
        var path = folder.Combine("body.dgr");
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        var store = new DependencyGraphDocumentStore(new FileWriter());
        var entry = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        if (!entry.IsUsable) return;

        // Act.
        var saved = await store.SaveAsync(path, entry.Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", saved.Error);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheCorpus_HoldsTheLineEndingPairAndTheFileWithoutAFinalNewline()
    {
        // Act.
        var names = _transcript.Corpus().Select(document => document.Name).ToList();

        // Assert: the documents whose bytes are the round trip's hardest cases are among those it runs over.
        Assert.Contains(names, name => name.EndsWith("/crlf-line-endings.dgr", StringComparison.Ordinal));
        Assert.Contains(names, name => name.EndsWith("/lf-line-endings.dgr", StringComparison.Ordinal));
        Assert.Contains(names, name => name.EndsWith("/no-trailing-newline.dgr", StringComparison.Ordinal));
    }
}
