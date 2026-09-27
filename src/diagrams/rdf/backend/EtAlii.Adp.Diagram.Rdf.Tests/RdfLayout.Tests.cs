using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The computed layout: pure, deterministic, type-banded, untyped last - no physics
/// (rdf-diagram Requirements 3.3 and 4.4).
/// </summary>
public class RdfLayoutTests
{
    private static RdfProjectionResult Project(string text) =>
        RdfProjection.Project(RdfParser.Parse(LineDocument.Parse(text)));

    private const string Banded =
        "@prefix ex: <http://example.org/> .\r\n"
        + "ex:zebra a ex:Animal .\r\n"
        + "ex:apple a ex:Fruit .\r\n"
        + "ex:bear a ex:Animal .\r\n"
        + "ex:loose ex:notes \"untyped\" .\r\n";

    [Fact]
    public void Bands_GroupByPrimaryType_UntypedLast()
    {
        // Arrange.
        var projection = Project(Banded);

        // Act.
        var positions = RdfLayout.Positions(projection);

        // Assert.
        // Same type, same band: same y-range; the two animals share a row.
        Assert.Equal(
            positions["res:http://example.org/zebra"].Y,
            positions["res:http://example.org/bear"].Y);

        // Bands are ordered by type name: Animal above Fruit, and the untyped node below both.
        Assert.True(positions["res:http://example.org/zebra"].Y < positions["res:http://example.org/apple"].Y);
        Assert.True(positions["res:http://example.org/apple"].Y < positions["res:http://example.org/loose"].Y);
    }

    [Fact]
    public void TheLayout_IsDeterministic()
    {
        // Arrange.
        var projection = Project(Banded);

        // Act & assert.
        Assert.Equal(RdfLayout.Positions(projection), RdfLayout.Positions(projection));
    }

    [Fact]
    public void EveryDrawnNode_GetsAPosition()
    {
        // Arrange.
        var projection = Project(Banded);

        // Act.
        var positions = RdfLayout.Positions(projection);

        // Assert, first, that there is anything to assert about. "Every drawn node has a
        // position" is perfectly true of a projection that drew no nodes, so without this the
        // test reports success loudest at the moment it has stopped testing anything.
        Assert.True(
            projection.Nodes.Count > 0,
            "The projection drew no nodes, so this guard checked nothing; the four subjects the fixture states should each have produced one.");

        Assert.All(projection.Nodes, node => Assert.True(positions.ContainsKey(node.Id)));
    }
}
