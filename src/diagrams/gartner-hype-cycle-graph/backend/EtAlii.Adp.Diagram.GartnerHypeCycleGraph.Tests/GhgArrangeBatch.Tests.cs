using EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// Arrange writes its rows as one batch (<see cref="GhgBody.Batch"/>), planned against the body as it
/// was, where it used to apply them one by one and read the body again after each. The two must write
/// the same bytes, for every document of the parity corpus - technology-trends included, whose
/// arrangement the parity transcript skips for its size.
/// </summary>
public class GhgArrangeBatchTests
{
    /// <summary>The corpus, each text once: the shipped copies of an example are the example.</summary>
    public static TheoryData<string> Corpus => [.. GhgTranscript.Corpus().DistinctBy(document => TranscriptText.Sha256(File.ReadAllBytes(Located(document))))];

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task ArrangeAsOneBatch_WritesTheBytes_ArrangingOneRowAtATimeWrites(string document)
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var text = TranscriptText.Decode(await File.ReadAllBytesAsync(Located(document), cancellationToken));
        var batched = GhgBody.Parse(text);
        var oneByOne = GhgBody.Parse(text);
        var rows = GhgArrangement.RowsOf(GhgParser.Parse(batched));

        // Act.
        var batchedWrites = ArrangeGhgCommandHandler.RowWrites(batched, GhgParser.Parse(batched), rows);
        batched.Batch(() =>
        {
            foreach (var write in batchedWrites)
            {
                write();
            }
        });
        foreach (var write in ArrangeGhgCommandHandler.RowWrites(oneByOne, GhgParser.Parse(oneByOne), rows))
        {
            write();
        }

        // Assert.
        Assert.Equal(oneByOne.Text, batched.Text);
    }

    private static string Located(string document) => Path.Combine(GhgTranscript.ModuleFolder, "..", "..", document);
}
