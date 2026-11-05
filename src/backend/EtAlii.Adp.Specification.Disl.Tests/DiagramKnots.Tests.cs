using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// <c>diagram.knots(relType)</c> (DISL §12.4): the strongly connected components over the relations of a type and its
/// subtypes that have more than one member or a self-loop, members in model order, components ordered by their first
/// member.
/// </summary>
public class DiagramKnotsTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": { "Station": {} },
          "relations": {
            "Track": { "source": "Station", "target": "Station" },
            "Branch": { "extends": "Track" },
            "Road": { "source": "Station", "target": "Station" } } }
        """));

    private static DislDiagram Diagram(string stations, params (string From, string To, string Type)[] relations)
    {
        var diagram = new DislDiagram(Specification);
        foreach (var name in stations.Split(' ')) diagram.AddNode("Station", name);
        var index = 0;
        foreach ((string from, string to, string type) in relations)
        {
            diagram.AddRelation(type, $"r{index++}", diagram.ElementById(from), diagram.ElementById(to));
        }
        return diagram;
    }

    private static IReadOnlyList<string> Knots(DislDiagram diagram, string type) =>
        [.. ((IReadOnlyList<object?>)Specification.Environment(DislContext.Constraint).Compile($"diagram.knots('{type}')")
            .Evaluate(new Dictionary<string, object?> { ["self"] = diagram, ["diagram"] = diagram, ["env"] = diagram.Env.ToCel() })!)
            .Select(knot => string.Join(" ", ((IReadOnlyList<object?>)knot!).Cast<DislElement>().Select(member => member.Id)))];

    [Fact]
    public void TwoCyclesSharingAMember_AreOneKnot_InModelOrder()
    {
        // Arrange: e -> b -> e and b -> d -> b share b; a stands apart.
        var diagram = Diagram("a b c d e", ("e", "b", "Track"), ("b", "e", "Track"), ("b", "d", "Track"), ("d", "b", "Track"), ("a", "c", "Track"));

        // Act and assert.
        Assert.Equal(["b d e"], Knots(diagram, "Track"));
    }

    [Fact]
    public void ASelfLoop_IsAKnotOfOne_AndALoneNodeIsNone()
    {
        // Arrange.
        var diagram = Diagram("a b c", ("c", "c", "Track"), ("a", "b", "Track"));

        // Act and assert.
        Assert.Equal(["c"], Knots(diagram, "Track"));
    }

    [Fact]
    public void Knots_AreOrderedByTheirFirstMember_OverTheTypeAndItsSubtypesOnly()
    {
        // Arrange.
        var diagram = Diagram("a b c d", ("d", "c", "Track"), ("c", "d", "Branch"), ("b", "a", "Track"), ("a", "b", "Track"), ("a", "d", "Road"), ("d", "a", "Road"));

        // Act and assert.
        Assert.Equal(["a b", "c d"], Knots(diagram, "Track"));
        Assert.Equal(["a d"], Knots(diagram, "Road"));
    }

    [Fact]
    public void AKnotFoundFirst_IsStillListedAfterOneWhoseFirstMemberComesEarlier()
    {
        // Arrange: the search from a finishes b and c before a itself.
        var diagram = Diagram("a b c", ("a", "a", "Track"), ("a", "b", "Track"), ("b", "c", "Track"), ("c", "b", "Track"));

        // Act and assert.
        Assert.Equal(["a", "b c"], Knots(diagram, "Track"));
    }
}
