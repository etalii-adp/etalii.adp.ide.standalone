using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A derived relation type's <c>key</c> (DISL §4.11.3): items with equal keys yield one relation, with
/// <c>item</c> the group's first item, <c>group</c> all of them in <c>from</c> order and <c>index</c> the
/// group's position; its <c>sources</c> default to the group.
/// </summary>
public class DerivedRelationKeyTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": { "Box": { "attributes": { "name": { "type": "string" } } } },
          "relations": {
            "Link": { "source": "Box", "target": "Box", "attributes": { "label": { "type": "string" } } },
            "Merged": { "source": "Box", "target": "Box",
              "attributes": { "label": { "type": "string" }, "count": { "type": "int" }, "position": { "type": "int" } },
              "derived": { "from": "diagram.relationsOfType('Link')", "key": "[item.source.id, item.target.id]",
                "source": "item.source", "target": "item.target",
                "attributes": { "label": "item.label", "count": "group.size()", "position": "index" } } },
            "Broken": { "source": "Box", "target": "Box",
              "derived": { "from": "diagram.relationsOfType('Link')", "key": "{'pair': item.source.id}", "source": "item.source", "target": "item.target" } } } }
        """));

    private static FblElement Box(string id, int line) =>
        new(id, true, "Box", "box", false, new Dictionary<string, object?> { ["name"] = id.ToUpperInvariant() }, null, null, null, null, new Span(0, 0), line);

    private static FblElement Link(string id, string source, string target, string label, int line) =>
        new(id, true, "Link", "link", true, new Dictionary<string, object?> { ["label"] = label }, null, null, source, target, new Span(0, 0), line);

    private static DislModel Model() => DislModelBuilder.From(new FblModel(
    [
        Box("a", 1), Box("b", 2), Box("c", 3),
        Link("l1", "a", "b", "first", 4),
        Link("l2", "b", "c", "third", 5),
        Link("l3", "a", "b", "second", 6),
    ], [], false), Specification);

    [Fact]
    public void TwoItemsOnOnePair_YieldOneRelation_WithTheFirstItemsAttributes()
    {
        // Act.
        var model = Model();

        // Assert.
        var merged = model.Diagram.RelationsOfType("Merged");
        Assert.Equal(["Merged:a->b", "Merged:b->c"], merged.Select(relation => relation.Id));
        Assert.Equal(["first", "third"], merged.Select(relation => relation.ValueOf("label")));
        Assert.Equal([2L, 1L], merged.Select(relation => relation.ValueOf("count")));
        Assert.Equal([0L, 1L], merged.Select(relation => relation.ValueOf("position")));
    }

    [Fact]
    public void TheSourcesOfAGroupedRelation_AreItsGroup()
    {
        // Act.
        var model = Model();

        // Assert.
        var merged = model.Diagram.RelationsOfType("Merged");
        Assert.Equal(["l1", "l3"], merged[0].Sources.Select(source => source.Id));
        Assert.Equal(["l2"], merged[1].Sources.Select(source => source.Id));
    }

    [Fact]
    public void AKeyThatIsNotAScalarOrAList_DropsItsItems()
    {
        // Act.
        var model = Model();

        // Assert.
        Assert.Empty(model.Diagram.RelationsOfType("Broken"));
        var finding = Assert.Single(model.Findings);
        Assert.Equal(DislModelBuilder.DerivedFailed, finding.Code);
        Assert.Equal("Items of Broken were dropped: the key of an item is not a string, number, bool or list of those", finding.Message);
    }
}
