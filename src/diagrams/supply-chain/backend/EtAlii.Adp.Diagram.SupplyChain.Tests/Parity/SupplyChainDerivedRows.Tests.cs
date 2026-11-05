using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>
/// The property rows derived from the DISL definition equal the hand-written ones (supply-chain-disl-fbl, task 5), row
/// for row and candidate for candidate: for every document of the parity corpus and every target the transcript records.
/// </summary>
public class SupplyChainDerivedRowsTests
{
    public static TheoryData<string> Documents() => SupplyChainCorpus.Names();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryRow_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var model = await SupplyChainCorpus.ReadAsync(name, TestContext.Current.CancellationToken);

        // Act and assert.
        foreach (var target in SupplyChainCorpus.Targets(model))
        {
            var expected = HandWrittenSupplyChain.Rows(model, target).Select(row => row.RowLine()).ToList();
            var actual = SupplyChainDefinition.Rows(model, target).Select(row => row.RowLine()).ToList();
            Assert.True(expected.SequenceEqual(actual), $"{name} {TranscriptText.Quoted(target)}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
        }
    }
}
