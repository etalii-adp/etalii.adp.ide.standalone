using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A relation's owner (DISL §4.11.3, §12.2): a derived relation's <c>owner</c> when it states one, in the object form
/// and as a key of the expression form; else, and for every stored relation, the nearest common ancestor of its ends,
/// or the diagram when they have none. CEL reads it as <c>self.owner</c>.
/// </summary>
public class DerivedRelationOwnerTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": { "Box": { "attributes": { "name": { "type": "string" } }, "children": { "allowed": ["Box"] } } },
          "relations": {
            "Link": { "source": "Box", "target": "Box" },
            "Plain": { "source": "Box", "target": "Box",
              "derived": { "from": "diagram.relationsOfType('Link')", "source": "item.source", "target": "item.target" } },
            "Up": { "source": "Box", "target": "Box",
              "derived": { "from": "diagram.relationsOfType('Link')", "source": "item.source", "target": "item.target", "owner": "item.source.parent" } },
            "Listed": { "source": "Box", "target": "Box",
              "derived": "diagram.relationsOfType('Link').map(r, {'source': r.source, 'target': r.target, 'owner': r.target})" },
            "Wrong": { "source": "Box", "target": "Box",
              "derived": { "from": "diagram.relationsOfType('Link')", "source": "item.source", "target": "item.target", "owner": "item" } } } }
        """));

    private static FblElement Box(string id, string? parent, int line) =>
        new(id, true, "Box", "box", false, new Dictionary<string, object?> { ["name"] = id }, parent, parent is null ? null : "children", null, null, new Span(0, 0), line);

    private static FblElement Link(string id, string source, string target, int line) =>
        new(id, true, "Link", "link", true, new Dictionary<string, object?>(), null, null, source, target, new Span(0, 0), line);

    /// <summary>
    /// <c>p</c> holds <c>a</c> and <c>b</c>, <c>a</c> holds <c>q</c>, <c>c</c> is at the top: a link between siblings,
    /// one between a grandchild and its parent's sibling, one to the top and one from a node to its own child.
    /// </summary>
    private static DislModel Model() => DislModelBuilder.From(new FblModel(
    [
        Box("p", null, 1), Box("a", "p", 2), Box("b", "p", 3), Box("q", "a", 4), Box("c", null, 5),
        Link("l1", "a", "b", 6),
        Link("l2", "q", "b", 7),
        Link("l3", "a", "c", 8),
        Link("l4", "p", "a", 9),
    ], [], false), Specification);

    private static string Owner(DislElement relation) => relation.Owner?.Id ?? "diagram";

    [Fact]
    public void AStoredRelation_BelongsToTheNearestCommonAncestorOfItsEnds_OrTheDiagram()
    {
        // Act.
        var model = Model();

        // Assert.
        Assert.Equal(["p", "p", "diagram", "diagram"], model.Diagram.RelationsOfType("Link").Select(Owner));
    }

    [Fact]
    public void ADerivedRelationWithoutAnOwner_BelongsWhereItsEndsSay()
    {
        // Act.
        var model = Model();

        // Assert.
        Assert.Equal(["p", "p", "diagram", "diagram"], model.Diagram.RelationsOfType("Plain").Select(Owner));
    }

    [Fact]
    public void ADerivedRelationsOwner_IsTheOneItStates_NullBeingTheDiagram()
    {
        // Act.
        var model = Model();

        // Assert.
        Assert.Equal(["p", "a", "p", "diagram"], model.Diagram.RelationsOfType("Up").Select(Owner));
    }

    [Fact]
    public void TheExpressionForm_StatesAnOwnerByItsKey()
    {
        // Act.
        var model = Model();

        // Assert.
        Assert.Equal(["b", "b", "c", "a"], model.Diagram.RelationsOfType("Listed").Select(Owner));
    }

    [Fact]
    public void AnOwnerThatIsNotANode_DropsItsItems()
    {
        // Act.
        var model = Model();

        // Assert.
        Assert.Empty(model.Diagram.RelationsOfType("Wrong"));
        var finding = Assert.Single(model.Findings);
        Assert.Equal(DislModelBuilder.DerivedFailed, finding.Code);
        Assert.Equal("Items of Wrong were dropped: the owner of an item is not a node or the diagram", finding.Message);
    }

    [Theory]
    [InlineData("Link", 1, "self.owner.id == 'p'")]
    [InlineData("Link", 2, "self.owner == diagram")]
    [InlineData("Up", 1, "self.owner.id == 'a'")]
    [InlineData("Up", 3, "self.owner == diagram")]
    public void Cel_ReadsTheOwnerAsSelfOwner(string type, int index, string expression)
    {
        // Arrange.
        var model = Model();
        var self = model.Diagram.RelationsOfType(type)[index];

        // Act.
        var value = Specification.Environment(DislContext.Element).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = self, ["diagram"] = model.Diagram, ["env"] = new CelMap() });

        // Assert.
        Assert.Equal(true, value);
    }
}
