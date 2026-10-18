using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The file a model is built for (DISL §8.6, §12.2): known while it is built, so that a derived id or a
/// derived element computed then reads an element's <c>location()</c> with its line.
/// </summary>
public class ModelFileTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": { "Box": { "attributes": { "name": { "type": "string" } } } },
          "relations": {
            "Link": { "source": "Box", "target": "Box" },
            "Echo": { "source": "Box", "target": "Box",
              "derived": { "from": "diagram.relationsOfType('Link')", "id": "'echo@' + string(item.location().orValue({'line': 0}).line)", "source": "item.source", "target": "item.target" } } } },
        "persistence": { "ids": { "strategy": "natural", "types": {
          "Link": { "strategy": "derived", "expression": "self.source.id + '->' + self.target.id + '@' + string(self.location().orValue({'line': 0}).line)" } } } }
        """));

    private static FblModel Reading() => new(
    [
        new FblElement("a", true, "Box", "box", false, new Dictionary<string, object?>(), null, null, null, null, new Span(0, 0), 1),
        new FblElement("b", true, "Box", "box", false, new Dictionary<string, object?>(), null, null, null, null, new Span(0, 0), 2),
        new FblElement("link@3", false, "Link", "link", true, new Dictionary<string, object?>(), null, null, "a", "b", new Span(0, 0), 3),
    ], [], false);

    [Fact]
    public void ADerivedIdAndADerivedElement_ReadTheLineOfTheFileTheModelIsBuiltFor()
    {
        // Act.
        var model = DislModelBuilder.From(Reading(), Specification, new DislEnv(), "body.txt");

        // Assert.
        Assert.Equal("body.txt", model.Diagram.File);
        Assert.Equal(["a->b@3", "echo@3"], model.Diagram.Relations.Select(relation => relation.Id).Order());
    }

    [Fact]
    public void AModelBuiltForNoFile_HasNoLocations()
    {
        // Act.
        var model = DislModelBuilder.From(Reading(), Specification);

        // Assert.
        Assert.Null(model.Diagram.File);
        Assert.Equal(["a->b@0", "echo@0"], model.Diagram.Relations.Select(relation => relation.Id).Order());
    }
}
