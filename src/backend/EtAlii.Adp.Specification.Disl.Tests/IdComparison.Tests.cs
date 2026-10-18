using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// How ids are compared (DISL §11.5.4, <c>persistence.ids.compare</c>): with <c>ignore-case</c>, without
/// regard to case for lookups, references and duplicate detection, each element keeping the spelling
/// it was read with; with <c>exact</c>, the default, as code-point sequences.
/// </summary>
public class IdComparisonTests
{
    private static DislSpecification Specification(string compare) => Specifications.Loaded(Specifications.With($$"""
        "metamodel": {
          "types": { "Box": { "attributes": { "name": { "type": "string" }, "keeper": { "type": "Box" } } } },
          "relations": { "Link": { "source": "Box", "target": "Box" } } },
        "persistence": { "ids": { "strategy": "natural", "compare": "{{compare}}" } }
        """));

    private static FblElement Box(string id, int line, string? keeper = null) =>
        new(id, true, "Box", "box", false, keeper is null ? new Dictionary<string, object?>() : new Dictionary<string, object?> { ["keeper"] = keeper }, null, null, null, null, new Span(0, 0), line);

    private static DislModel Model(string compare, params FblElement[] elements) => DislModelBuilder.From(new FblModel(elements, [], false), Specification(compare));

    [Fact]
    public void IgnoringCase_ALookupFindsTheElementAsItWasRead()
    {
        // Act.
        var model = Model("ignore-case", Box("Api", 1));

        // Assert.
        var found = model.Diagram.ElementById("API");
        Assert.NotNull(found);
        Assert.Equal("Api", found.Id);
    }

    [Fact]
    public void IgnoringCase_RelationEndsAndReferencesResolve()
    {
        // Act.
        var model = Model("ignore-case",
            Box("Api", 1), Box("Db", 2, keeper: "API"),
            new FblElement("l", true, "Link", "link", true, new Dictionary<string, object?>(), null, null, "api", "DB", new Span(0, 0), 3));

        // Assert.
        var link = model.Diagram.ElementById("l")!;
        Assert.Equal("Api", link.Source?.Id);
        Assert.Equal("Db", link.Target?.Id);
        Assert.Same(model.Diagram.ElementById("Api"), model.Diagram.ElementById("Db")!.ValueOf("keeper"));
    }

    [Fact]
    public void IgnoringCase_TwoSpellingsOfOneId_AreADuplicate_AndTheFirstKeepsIt()
    {
        // Act.
        var model = Model("ignore-case", Box("Api", 1), Box("API", 2));
        var findings = ConstraintEvaluator.Evaluate(model.Diagram.Specification, model.Diagram);

        // Assert.
        var duplicate = Assert.Single(findings);
        Assert.Equal("std.duplicateId", duplicate.Code);
        Assert.Equal(["API"], duplicate.ElementIds);
        Assert.Equal(2, duplicate.Line);
        Assert.Same(model.Diagram.Nodes[0], model.Diagram.ElementById("api"));
    }

    [Fact]
    public void Exactly_ADifferentCaseIsADifferentId()
    {
        // Act.
        var model = Model("exact", Box("Api", 1), Box("API", 2));
        var findings = ConstraintEvaluator.Evaluate(model.Diagram.Specification, model.Diagram);

        // Assert.
        Assert.Empty(findings);
        Assert.Null(model.Diagram.ElementById("api"));
        Assert.Same(model.Diagram.Nodes[1], model.Diagram.ElementById("API"));
    }
}
