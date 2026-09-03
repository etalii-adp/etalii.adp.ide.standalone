using EtAlii.Adp.Diagram.Rdf.Shacl;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The computed shapes layout: every card positioned, deterministically, in discovery order -
/// no physics, no randomness (shacl-diagram Requirement 3.2).
/// </summary>
public class ShaclLayoutTests
{
    private static ShaclProjectionResult Project(string body) =>
        ShaclProjection.Project(RdfParser.Parse(RdfDocument.Parse(
            "@prefix sh: <http://www.w3.org/ns/shacl#> .\n@prefix ex: <http://example.org/> .\n" + body)));

    [Fact]
    public void EveryCard_GetsAPosition_AndTheSameFileAlwaysOpensTheSameWay()
    {
        var projection = Project("""
            ex:A a sh:NodeShape ; sh:property [ sh:path ex:p ] .
            ex:B a sh:NodeShape .
            ex:C a sh:NodeShape ; sh:node [ sh:closed true ] .
            ex:D a sh:NodeShape .
            """ + "\n");

        var positions = ShaclLayout.Positions(projection);

        Assert.Equal(projection.Cards.Count, positions.Count);
        Assert.All(projection.Cards, card => Assert.True(positions.ContainsKey(card.Id)));

        // Deterministic: a second computation lands every card on the same spot.
        var again = ShaclLayout.Positions(projection);
        Assert.All(positions, entry => Assert.Equal(entry.Value, again[entry.Key]));

        // Discovery order reads left to right: the first two cards share a grid row.
        Assert.True(positions[projection.Cards[0].Id].X < positions[projection.Cards[1].Id].X);
        Assert.Equal(positions[projection.Cards[0].Id].Y, positions[projection.Cards[1].Id].Y);
    }

    [Fact]
    public void AnonymousCards_ArePositionedToo_ComputedOnly()
    {
        var projection = Project("ex:C a sh:NodeShape ; sh:node [ sh:closed true ] .\n");
        var positions = ShaclLayout.Positions(projection);
        var blank = projection.Cards.Single(card => card.Blank);
        Assert.True(positions.ContainsKey(blank.Id));
    }
}
