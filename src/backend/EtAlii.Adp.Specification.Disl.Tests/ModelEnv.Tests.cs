using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The <c>env</c> a model's own expressions read (DISL §12.3): a derived type's (§4.11) and a derived
/// attribute's (§4.3), which see the viewpoint the host builds the model for.
/// </summary>
public class ModelEnvTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": { "Box": { "attributes": { "name": { "type": "string" }, "seenIn": { "type": "string", "derived": "env.viewpoint" } } } },
          "relations": {
            "Link": { "source": "Box", "target": "Box" },
            "Shown": { "source": "Box", "target": "Box",
              "derived": { "from": "env.viewpoint == 'wide' ? diagram.relationsOfType('Link') : []", "source": "item.source", "target": "item.target" } } } },
        "viewpoints": { "wide": {}, "narrow": {} }
        """));

    private static FblModel Reading() => new(
    [
        new FblElement("a", true, "Box", "box", false, new Dictionary<string, object?> { ["name"] = "A" }, null, null, null, null, new Span(0, 0), 1),
        new FblElement("b", true, "Box", "box", false, new Dictionary<string, object?> { ["name"] = "B" }, null, null, null, null, new Span(0, 0), 2),
        new FblElement("l", true, "Link", "link", true, new Dictionary<string, object?>(), null, null, "a", "b", new Span(0, 0), 3),
    ], [], false);

    [Fact]
    public void ADerivedType_IsComputedForTheViewpointTheModelIsBuiltFor()
    {
        // Act.
        var wide = DislModelBuilder.From(Reading(), Specification, new DislEnv(Viewpoint: "wide"));
        var narrow = DislModelBuilder.From(Reading(), Specification, new DislEnv(Viewpoint: "narrow"));

        // Assert.
        Assert.Empty(wide.Findings);
        Assert.Equal(["Shown:a->b"], wide.Diagram.RelationsOfType("Shown").Select(relation => relation.Id));
        Assert.Empty(narrow.Findings);
        Assert.Empty(narrow.Diagram.RelationsOfType("Shown"));
    }

    [Fact]
    public void ADerivedAttribute_ReadsTheViewpointTheModelIsBuiltFor()
    {
        // Act.
        var model = DislModelBuilder.From(Reading(), Specification, new DislEnv(Viewpoint: "wide"));

        // Assert.
        Assert.Equal("wide", model.Diagram.ElementById("a")!.ValueOf("seenIn"));
    }

    [Fact]
    public void AModelBuiltHeadless_HasNoViewpoint()
    {
        // Act.
        var model = DislModelBuilder.From(Reading(), Specification);

        // Assert.
        Assert.Empty(model.Findings);
        Assert.Empty(model.Diagram.RelationsOfType("Shown"));
        Assert.Equal("", model.Diagram.ElementById("a")!.ValueOf("seenIn"));
    }
}
