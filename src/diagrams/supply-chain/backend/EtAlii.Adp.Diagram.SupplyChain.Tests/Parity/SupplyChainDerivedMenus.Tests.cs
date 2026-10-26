using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>
/// The palette, the context menus and what removing a node asks first, derived from the DISL definition, equal the
/// hand-written ones (supply-chain-disl-fbl, task 5), record for record: for every document of the parity corpus and
/// every target the transcript records, every group, node and flow id, the empty id, a placement, a connect gesture
/// and an unknown id.
/// </summary>
public class SupplyChainDerivedMenusTests
{
    public static TheoryData<string> Documents() => SupplyChainCorpus.Names();

    [Fact]
    public void ThePalette_IsTheHandWrittenOne() => Assert.Equal(HandWrittenSupplyChain.Toolbox, SupplyChainDefinition.Toolbox);

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryMenu_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var model = await SupplyChainCorpus.ReadAsync(name, TestContext.Current.CancellationToken);

        // Act and assert.
        foreach (var target in SupplyChainCorpus.Targets(model))
        {
            var expected = HandWrittenSupplyChain.Menus(model, target);
            var actual = SupplyChainDefinition.Menus(model, target);
            Assert.True(expected.MenuLines().SequenceEqual(actual.MenuLines()), $"{name} {TranscriptText.Quoted(target)}:\nexpected {string.Join("\n         ", expected.MenuLines())}\nactual   {string.Join("\n         ", actual.MenuLines())}");
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryRemovalConfirmation_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var model = await SupplyChainCorpus.ReadAsync(name, TestContext.Current.CancellationToken);

        // Act and assert.
        foreach (var target in SupplyChainCorpus.Targets(model))
        {
            var expected = HandWrittenSupplyChain.RemovalConfirmation(model, target);
            var actual = SupplyChainDefinition.RemovalConfirmation(model, target);
            Assert.True(Equals(expected, actual), $"{name} {TranscriptText.Quoted(target)}:\nexpected {expected?.ToString() ?? "none"}\nactual   {actual?.ToString() ?? "none"}");
        }
    }
}
