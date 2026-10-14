using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// <c>persistence.ids.prefix</c> under the <c>natural</c> strategy (DISL §11.5, §11.5.1): the id of an
/// element of a type that declares a prefix is that prefix followed by its natural id - for a node the
/// id it was read with, for a relation read without one its ends as read, joined by <c>|</c> - and a
/// type that declares none keeps the id it was read with.
/// </summary>
public class NaturalIdPrefixesTests
{
    private static DislSpecification Specification(string ids) => Specifications.Loaded(Specifications.With($$"""
        "metamodel": {
          "types": { "Pump": { "attributes": { "tag": { "type": "string", "key": true } } },
                     "Tank": { "attributes": { "tag": { "type": "string", "key": true } } } },
          "relations": { "Pipe": { "source": ["Pump", "Tank"], "target": ["Pump", "Tank"] } } },
        "persistence": {
          "ids": {{ids}},
          "typeMap": { "Line": { "as": "Pipe", "attributes": { "from": "source", "to": "target" } } } }
        """));

    private static readonly DislSpecification Prefixed = Specification("""{ "strategy": "natural", "prefix": { "Pump": "pump:", "Pipe": "pipe:" } }""");

    private static FblElement Node(string id, string type, int line) =>
        new(id, true, type, type.ToLowerInvariant(), false, new Dictionary<string, object?> { ["tag"] = id }, null, null, null, null, new Span(0, 0), line);

    /// <summary>A pipe read as an entry whose reference attributes name its ends, as a lines binding reads one.</summary>
    private static FblElement Line(string from, string to, int line) =>
        new($"line@{line}", false, "Line", "line", false, new Dictionary<string, object?> { ["from"] = from, ["to"] = to }, null, null, null, null, new Span(0, 0), line);

    /// <summary>A pipe read as a relation with its ends, as a binding with a relation rule reads one.</summary>
    private static FblElement Pipe(string source, string target, int line) =>
        new($"pipe@{line}", false, "Pipe", "pipe", true, new Dictionary<string, object?>(), null, null, source, target, new Span(0, 0), line);

    private static DislDiagram Read(DislSpecification specification, params FblElement[] elements) =>
        DislModelBuilder.From(new FblModel(elements, [], false), specification).Diagram;

    [Fact]
    public void AnElementOfATypeWithAPrefix_IsThePrefixFollowedByItsNaturalId()
    {
        // Act.
        var diagram = Read(Prefixed, Node("p1", "Pump", 1), Node("p2", "Pump", 2));

        // Assert.
        Assert.Equal(["pump:p1", "pump:p2"], diagram.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void ARelationReadWithoutAnId_IsThePrefixFollowedByItsEndsJoinedByABar()
    {
        // Act.
        var diagram = Read(Prefixed, Node("p1", "Pump", 1), Node("p2", "Pump", 2), Line("p1", "p2", 3), Pipe("p2", "p1", 4));

        // Assert.
        Assert.Equal(["pipe:p1|p2", "pipe:p2|p1"], diagram.Relations.Select(relation => relation.Id));
        Assert.Equal(["pump:p1", "pump:p2"], diagram.Relations.Select(relation => relation.Source!.Id));
        Assert.Equal(["pump:p2", "pump:p1"], diagram.Relations.Select(relation => relation.Target!.Id));
    }

    [Fact]
    public void ATypeWithoutAPrefix_KeepsItsId()
    {
        // Act.
        var diagram = Read(Prefixed, Node("p1", "Pump", 1), Node("t1", "Tank", 2), Line("p1", "t1", 3));

        // Assert.
        Assert.Equal(["pump:p1", "t1"], diagram.Nodes.Select(node => node.Id));
        Assert.Equal(("pipe:p1|t1", "t1"), (diagram.Relations[0].Id, diagram.Relations[0].Target!.Id));
    }

    [Fact]
    public void AnEndThatNamesNothing_IsStillPartOfTheId_AndStaysAsRead()
    {
        // Act.
        var diagram = Read(Prefixed, Node("p1", "Pump", 1), Line("p1", "ghost", 2));

        // Assert.
        var pipe = Assert.Single(diagram.Relations);
        Assert.Equal(("pipe:p1|ghost", "p1", "ghost"), (pipe.Id, pipe.SourceId, pipe.TargetId));
        Assert.Null(pipe.Target);
    }

    [Fact]
    public void TwoNodesReadWithOneId_BothGetThePrefix_AndARelationFindsTheFirst()
    {
        // Act: a duplicated name is kept, not refused (DISL 11.5.4); the first in reading order keeps it.
        var diagram = Read(Prefixed, Node("p1", "Pump", 1), Node("p1", "Pump", 2), Line("p1", "p1", 3));

        // Assert.
        Assert.Equal(["pump:p1", "pump:p1"], diagram.Nodes.Select(node => node.Id));
        var pipe = Assert.Single(diagram.Relations);
        Assert.Equal("pipe:p1|p1", pipe.Id);
        Assert.Same(diagram.Nodes[0], pipe.Source);
        Assert.Same(diagram.Nodes[0], pipe.Target);
    }

    [Fact]
    public void OnePrefixForEveryType_AppliesToEachOfThem()
    {
        // Arrange.
        var specification = Specification("""{ "strategy": "natural", "prefix": "id_" }""");

        // Act.
        var diagram = Read(specification, Node("p1", "Pump", 1), Node("t1", "Tank", 2), Line("p1", "t1", 3));

        // Assert.
        Assert.Equal(["id_p1", "id_t1", "id_p1|t1"], diagram.Elements.Select(element => element.Id));
    }

    [Fact]
    public void APrefixUnderAnotherStrategy_LeavesTheIdsRead()
    {
        // Arrange: a uuid strategy's prefix is for the ids the runtime mints, not for those it reads.
        var specification = Specification("""{ "strategy": "uuid-v4", "prefix": { "Pump": "pump:" } }""");

        // Act.
        var diagram = Read(specification, Node("p1", "Pump", 1));

        // Assert.
        Assert.Equal("p1", Assert.Single(diagram.Nodes).Id);
    }
}
