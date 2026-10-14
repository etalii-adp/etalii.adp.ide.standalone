using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// Writing what <c>persistence.ids.prefix</c> under the <c>natural</c> strategy read (DISL §11.5.1):
/// <see cref="DislWrite"/> writes every id as the binding read it, the prefix taken off again - the
/// element changed, a relation's ends and an attribute that holds elements - and a relation drawn as an
/// add of its binding type with its ends in the attributes the type map gives them.
/// </summary>
public class NaturalIdWritingTests
{
    private static DislSpecification Specification(string ids) => Specifications.Loaded(Specifications.With($$"""
        "metamodel": {
          "types": { "Pump": { "attributes": { "tag": { "type": "string", "key": true } } },
                     "Tank": { "attributes": { "tag": { "type": "string", "key": true },
                                               "feeds": { "type": "Pump", "many": true },
                                               "spare": { "type": "Pump" } } } },
          "relations": { "Pipe": { "source": ["Pump", "Tank"], "target": ["Pump", "Tank"],
                                   "attributes": { "bore": { "type": "int" } } },
                         "Hose": { "source": ["Pump"], "target": ["Pump"] } } },
        "persistence": {
          "ids": {{ids}},
          "typeMap": { "Line": { "as": "Pipe", "attributes": { "from": "source", "to": "target" } } } }
        """));

    private static readonly DislSpecification Prefixed = Specification("""{ "strategy": "natural", "prefix": { "Pump": "pump:", "Pipe": "pipe:", "Hose": "hose:" } }""");

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    [Fact]
    public void ASetAndARemove_AreWrittenUnderTheIdTheElementWasReadWith()
    {
        // Act.
        var set = DislWrite.ToFbl(Prefixed, new DislChange.Set("pump:p1", "Pump", Values(("tag", "p9"))));
        var removed = DislWrite.ToFbl(Prefixed, new DislChange.Remove("pipe:p1|p2"));

        // Assert.
        Assert.Equal("p1", Assert.IsType<ModelChange.Set>(set).Id);
        Assert.Equal(new ModelChange.Remove("p1|p2"), removed);
    }

    [Fact]
    public void AnAttributeThatHoldsElements_IsWrittenByTheIdsTheyWereReadWith()
    {
        // Arrange: an action's create stores an element as its id; a value set directly may be the element.
        var diagram = new DislDiagram(Prefixed);
        var pump = diagram.AddNode("Pump", "pump:p2", Values(("tag", "p2")));

        // Act.
        var created = Assert.IsType<ModelChange.Add>(DislWrite.ToFbl(Prefixed, new DislChange.Create("Tank", "t1", Values(("tag", "t1"), ("feeds", new List<object?> { "pump:p1", pump })), null, null)));
        var set = Assert.IsType<ModelChange.Set>(DislWrite.ToFbl(Prefixed, new DislChange.Set("t1", "Tank", Values(("spare", pump)))));

        // Assert.
        Assert.Equal(["p1", "p2"], (IEnumerable<object?>)created.Attributes["feeds"]!);
        Assert.Equal("p2", set.Attributes["spare"]);
    }

    [Fact]
    public void ARelationDrawn_IsAnAddOfItsBindingType_WithItsEndsInTheAttributesTheTypeMapGivesThem()
    {
        // Act.
        var mapped = Assert.IsType<ModelChange.Add>(DislWrite.ToFbl(Prefixed, new DislChange.Connect("Pipe", "pipe:p1|p2", "pump:p1", "pump:p2", Values(("bore", 4L)))));
        var plain = Assert.IsType<ModelChange.Add>(DislWrite.ToFbl(Prefixed, new DislChange.Connect("Hose", "hose:p1|p2", "pump:p1", "pump:p2", Values())));

        // Assert.
        Assert.Equal(("Line", "p1|p2"), (mapped.Type, mapped.Id));
        Assert.Equal([("bore", (object?)4L), ("from", "p1"), ("to", "p2")], mapped.Attributes.OrderBy(attribute => attribute.Key, StringComparer.Ordinal).Select(attribute => (attribute.Key, attribute.Value)));
        Assert.Equal(("Hose", "p1"), (plain.Type, plain.Attributes["source"]));
        Assert.Equal("p2", plain.Attributes["target"]);
    }

    [Fact]
    public void ATypeWithoutAPrefix_AndAnotherStrategy_KeepTheirIds()
    {
        // Arrange.
        var minted = Specification("""{ "strategy": "uuid-v4", "prefix": { "Pump": "pump:" } }""");

        // Act.
        var tank = DislWrite.ToFbl(Prefixed, new DislChange.Remove("t1"));
        var pump = DislWrite.ToFbl(minted, new DislChange.Remove("pump:p1"));

        // Assert.
        Assert.Equal(new ModelChange.Remove("t1"), tank);
        Assert.Equal(new ModelChange.Remove("pump:p1"), pump);
    }
}
