using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// <c>diagram.cycles(relType, max)</c> and <c>diagram.cyclesTruncated(relType, max)</c> (DISL §12.4):
/// the elementary cycles over the relations of a type and its subtypes, each starting at its member
/// that comes first in model order, ordered by that member and then lexicographically by member order,
/// a self-loop a cycle of one, and never more than <c>max</c> of them.
/// </summary>
public class DiagramCyclesTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": { "Station": {} },
          "relations": {
            "Track": { "source": "Station", "target": "Station" },
            "Branch": { "extends": "Track" },
            "Road": { "source": "Station", "target": "Station" } } }
        """));

    /// <summary>A diagram with one Station per name, in that order, and a relation of <paramref name="type"/> per pair.</summary>
    private static DislDiagram Diagram(string stations, params (string From, string To)[] tracks) =>
        Diagram(stations, [.. tracks.Select(track => (track.From, track.To, "Track"))]);

    private static DislDiagram Diagram(string stations, IEnumerable<(string From, string To, string Type)> relations)
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

    private static object? Evaluate(DislDiagram diagram, string expression) =>
        Specification.Environment(DislContext.Constraint).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = diagram, ["diagram"] = diagram, ["env"] = new CelMap() });

    /// <summary>The cycles as text: each cycle its member ids joined by a space, the cycles by a comma.</summary>
    private static string Cycles(DislDiagram diagram, long max = 1000)
    {
        var value = Evaluate(diagram, $"diagram.cycles('Track', {max}).map(c, c.map(e, e.id).join(' ')).join(', ')");
        return value as string ?? throw new InvalidOperationException($"The cycles were not computed: {value}");
    }

    private static bool Truncated(DislDiagram diagram, long max) =>
        Evaluate(diagram, $"diagram.cyclesTruncated('Track', {max})") as bool? ?? throw new InvalidOperationException("cyclesTruncated did not give a bool.");

    [Fact]
    public void ASelfLoop_IsACycleOfOne()
    {
        // Arrange.
        var diagram = Diagram("a b", ("a", "a"), ("a", "b"));

        // Act.
        var cycles = Cycles(diagram);

        // Assert.
        Assert.Equal("a", cycles);
    }

    [Fact]
    public void TwoCyclesSharingAMember_AreEachFoundOnce()
    {
        // Arrange.
        var diagram = Diagram("a b c", ("a", "b"), ("b", "a"), ("b", "c"), ("c", "b"));

        // Act.
        var cycles = Cycles(diagram);

        // Assert.
        Assert.Equal("a b, b c", cycles);
    }

    [Fact]
    public void ACycle_StartsAtItsMemberThatComesFirstInModelOrder()
    {
        // Arrange: the relations are declared from b, so the cycle is entered at b.
        var diagram = Diagram("a b c", ("b", "c"), ("c", "a"), ("a", "b"));

        // Act.
        var cycles = Cycles(diagram);

        // Assert.
        Assert.Equal("a b c", cycles);
    }

    [Fact]
    public void TheCycles_AreOrderedByFirstMemberThenLexicographicallyByMemberOrder_NotByRelationOrder()
    {
        // Arrange: the relations through c are declared before those through b, and d before both.
        var diagram = Diagram("a b c d",
            ("d", "b"), ("b", "d"),
            ("a", "c"), ("c", "a"),
            ("c", "b"), ("b", "c"),
            ("a", "b"), ("b", "a"));

        // Act.
        var cycles = Cycles(diagram);

        // Assert: a b before a b c (a prefix first), a b c before a c, a c before a c b; then b's.
        Assert.Equal("a b, a b c, a c, a c b, b c, b d", cycles);
    }

    [Fact]
    public void TheBound_KeepsTheFirstCyclesInOrder_AndSaysThatItCut()
    {
        // Arrange.
        var diagram = Diagram("a b c", ("c", "a"), ("a", "c"), ("b", "a"), ("a", "b"), ("c", "c"));

        // Act.
        var all = Cycles(diagram, 3);
        var cut = Cycles(diagram, 2);
        var none = Cycles(diagram, 0);

        // Assert.
        Assert.Equal("a b, a c, c", all);
        Assert.False(Truncated(diagram, 3));
        Assert.Equal("a b, a c", cut);
        Assert.True(Truncated(diagram, 2));
        Assert.Equal("", none);
        Assert.True(Truncated(diagram, 0));
    }

    [Fact]
    public void ADiagramWithoutCycles_IsNotTruncated()
    {
        // Arrange.
        var diagram = Diagram("a b c", ("a", "b"), ("b", "c"));

        // Act.
        var cycles = Cycles(diagram, 0);

        // Assert.
        Assert.Equal("", cycles);
        Assert.False(Truncated(diagram, 0));
    }

    [Fact]
    public void TheCycles_FollowTheTypeAndItsSubtypes_AndNoOtherRelation()
    {
        // Arrange: a Branch is a Track, a Road is not, and a relation whose end names nothing is not walked.
        var diagram = Diagram("a b c", [("a", "b", "Track"), ("b", "a", "Branch"), ("b", "c", "Road"), ("c", "b", "Track")]);
        diagram.AddRelation("Track", "dangling", diagram.ElementById("c"), null);

        // Act.
        var cycles = Cycles(diagram);

        // Assert.
        Assert.Equal("a b", cycles);
    }

    [Fact]
    public void ACycleIsFoundAgain_AfterTheDiagramChanges()
    {
        // Arrange.
        var diagram = Diagram("a b", ("a", "b"));
        var before = Cycles(diagram);

        // Act.
        diagram.AddRelation("Track", "back", diagram.ElementById("b"), diagram.ElementById("a"));
        var after = Cycles(diagram);

        // Assert.
        Assert.Equal(("", "a b"), (before, after));
    }

    [Fact]
    public void TwoNodesWithOneId_AreTwoMembers_AndNothingThrows()
    {
        // Arrange: a reading can hold a duplicated id (DISL 11.5.4); the search goes by element, not by id.
        var diagram = new DislDiagram(Specification);
        var first = diagram.AddNode("Station", "a");
        var second = diagram.AddNode("Station", "a");
        diagram.AddRelation("Track", "there", first, second);
        diagram.AddRelation("Track", "back", second, first);

        // Act.
        var cycles = Cycles(diagram);

        // Assert.
        Assert.Equal("a a", cycles);
    }

    [Fact]
    public void ANegativeBound_IsAnError()
    {
        // Arrange.
        var diagram = Diagram("a", ("a", "a"));

        // Act.
        var value = Evaluate(diagram, "diagram.cycles('Track', -1)");

        // Assert.
        Assert.IsType<CelError>(value);
    }

    [Fact]
    public void TheCost_IsTheGraphTimesTheBound()
    {
        // Arrange: a budget below (nodes + relations) × (max + 1) refuses the call however few cycles there are.
        var specification = Specifications.Loaded(Specifications.With("""
            "language": { "id": "org.example.test", "version": "1.0.0", "limits": { "celCost": 2000 } },
            "metamodel": { "types": { "Station": {} }, "relations": { "Track": { "source": "Station", "target": "Station" } } }
            """));
        var diagram = new DislDiagram(specification);
        var a = diagram.AddNode("Station", "a");
        diagram.AddRelation("Track", "loop", a, a);
        var program = specification.Environment(DislContext.Constraint).Compile("diagram.cycles('Track', 1000).size()");

        // Act.
        var value = program.Evaluate(new Dictionary<string, object?> { ["self"] = diagram, ["diagram"] = diagram, ["env"] = new CelMap() });

        // Assert.
        Assert.IsType<CelError>(value);
    }
}
