using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>What the mapper sends: a card's share bar is measured against the whole document, not the view.</summary>
public sealed class SupplyChainElementMapperTests
{
    [Fact]
    public void ACardsShare_IsOfTheWholeDocument_NotOfWhatTheViewHolds()
    {
        // Arrange: a small mine in view, and one four times its size far outside it.
        var model = SupplyChainParser.Parse(LineDocument.Parse("""
            supply-chain: 1
            nodes:
              - id: small
                type: raw-material
                quantity: 25
                unit: t
                x: 0
                y: 0
              - id: large
                type: raw-material
                quantity: 100
                unit: t
                x: 5000
                y: 5000
            """));

        // Act.
        var delivered = new SupplyChainElementMapper().Visible(model, new DiagramViewport(-10, -10, 300, 200), SupplyChainTrace.None);

        // Assert.
        var small = Assert.Single(delivered);
        Assert.Equal(0.25, SupplyChainNodePayload.Parser.ParseFrom(small.Payload.ToArray()).Share, 3);
    }
}
