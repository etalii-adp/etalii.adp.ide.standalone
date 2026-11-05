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

    /// <summary>
    /// The example of §4.11.3: each stored relationship lifted to the nearest ancestor of each end that the view draws,
    /// several landing on one pair drawn as one line carrying the first one's description. <c>inView</c> is a function
    /// of the example's own here, and <c>liftTo</c> takes the first of the list by its size, as this runtime has no
    /// optional types.
    /// </summary>
    private static readonly DislSpecification Lifting = Specifications.Loaded(Specifications.With("""
        "functions": {
          "inView": { "params": [{ "name": "e", "type": "Element" }], "returns": "bool", "cel": "e.shown" },
          "liftTo": {
            "params": [{ "name": "e", "type": "Element" }], "returns": "Element", "uses": ["diagram"],
            "cel": "cel.bind(l, ([e] + e.ancestors()).filter(a, inView(a)), l.size() > 0 ? l[0] : null)"
          }
        },
        "metamodel": {
          "types": { "Element": { "attributes": { "shown": { "type": "bool" } }, "children": { "allowed": ["Element"] } } },
          "relations": {
            "Relationship": { "source": "Element", "target": "Element", "attributes": { "description": { "type": "string" }, "technology": { "type": "string" } } },
            "DrawnRelationship": {
              "source": "Element", "target": "Element",
              "attributes": { "description": { "type": "string" }, "technology": { "type": "string" } },
              "derived": {
                "from": "diagram.relationsOfType('Relationship').filter(r, liftTo(r.source) != null && liftTo(r.target) != null && liftTo(r.source) != liftTo(r.target))",
                "key": "[liftTo(item.source).id, liftTo(item.target).id]",
                "id": "'rel:' + liftTo(item.source).id + '->' + liftTo(item.target).id",
                "source": "liftTo(item.source)",
                "target": "liftTo(item.target)",
                "attributes": { "description": "item.description", "technology": "item.technology" },
                "edits": { "attribute:description": "describeRelationship", "delete": "deleteRelationships" }
              }
            }
          }
        }
        """));

    private static FblElement Element(string id, bool shown, string? parent, int line) =>
        new(id, true, "Element", "element", false, new Dictionary<string, object?> { ["shown"] = shown }, parent, parent is null ? null : "children", null, null, new Span(0, 0), line);

    private static FblElement Relationship(string id, string source, string target, string description, int line) =>
        new(id, true, "Relationship", "relationship", true, new Dictionary<string, object?> { ["description"] = description, ["technology"] = "HTTPS" }, null, null, source, target, new Span(0, 0), line);

    [Fact]
    public void TheExampleOf4113_LiftsRelationshipsToWhatTheViewDraws_OneLinePerPair()
    {
        // Act.
        var model = DislModelBuilder.From(new FblModel(
        [
            Element("web", true, null, 1), Element("api", false, "web", 2), Element("db", true, null, 3), Element("store", false, "db", 4),
            Relationship("r1", "api", "store", "Reads orders", 5),
            Relationship("r2", "web", "db", "Writes orders", 6),
            Relationship("r3", "api", "web", "Inside one container", 7),
        ], [], false), Lifting);

        // Assert.
        var drawn = Assert.Single(model.Diagram.RelationsOfType("DrawnRelationship"));
        Assert.Equal(("rel:web->db", "web", "db"), (drawn.Id, drawn.Source!.Id, drawn.Target!.Id));
        Assert.Equal("Reads orders", drawn.ValueOf("description"));
        Assert.Equal(["r1", "r2"], drawn.Sources.Select(source => source.Id));
    }
}
