using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The Manchester-style expression renderer: every construct's documented form, the depth-2
/// elision against the uncapped form, the cycle guard, and the malformed short-circuits
/// (owl-diagram Requirement 3).
/// </summary>
public class ExpressionRendererTests
{
    private const string Prelude = """
        @prefix : <http://example.org/t#> .
        @prefix owl: <http://www.w3.org/2002/07/owl#> .
        @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .
        @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

        """;

    /// <summary>Renders the blank object of :A's subClassOf axiom in <paramref name="body"/>.</summary>
    private static OwlExpressionText Render(string body, int? maxDepth = null)
    {
        var model = RdfParser.Parse(LineDocument.Parse(Prelude + body));
        var root = model.Triples
            .Single(t => t is { Subject: IriTerm { Iri: "http://example.org/t#A" }, Predicate.Iri: OwlVocabulary.SubClassOf })
            .Object;
        return ExpressionRenderer.Render(root, model, maxDepth);
    }

    [Theory]
    [InlineData("[ a owl:Restriction ; owl:onProperty :p ; owl:someValuesFrom :B ]", "∃ :p.:B")]
    [InlineData("[ a owl:Restriction ; owl:onProperty :p ; owl:allValuesFrom :B ]", "∀ :p.:B")]
    [InlineData("[ a owl:Restriction ; owl:onProperty :p ; owl:hasValue :b ]", "∋ :p.:b")]
    [InlineData("[ a owl:Restriction ; owl:onProperty :p ; owl:hasValue 42 ]", "∋ :p.42")]
    [InlineData("[ a owl:Restriction ; owl:onProperty :p ; owl:cardinality 1 ]", "= 1 :p")]
    [InlineData("[ a owl:Restriction ; owl:onProperty :p ; owl:minCardinality 2 ]", "≥ 2 :p")]
    [InlineData("[ a owl:Restriction ; owl:onProperty :p ; owl:maxCardinality 3 ]", "≤ 3 :p")]
    [InlineData("[ a owl:Restriction ; owl:onProperty :p ; owl:qualifiedCardinality 1 ; owl:onClass :B ]", "= 1 :p.:B")]
    [InlineData("[ a owl:Restriction ; owl:onProperty :p ; owl:minQualifiedCardinality 2 ; owl:onDataRange xsd:integer ]", "≥ 2 :p.xsd:integer")]
    [InlineData("[ a owl:Class ; owl:unionOf ( :B :C ) ]", "(:B ∪ :C)")]
    [InlineData("[ a owl:Class ; owl:intersectionOf ( :B :C ) ]", "(:B ∩ :C)")]
    [InlineData("[ a owl:Class ; owl:complementOf :B ]", "¬:B")]
    [InlineData("[ a owl:Class ; owl:oneOf ( :b :c ) ]", "{:b, :c}")]
    public void EveryConstruct_RendersItsDocumentedForm(string expression, string expected)
    {
        // Arrange & act.
        var rendered = Render($":A rdfs:subClassOf {expression} .");

        // Assert.
        Assert.Equal(expected, rendered.Text);
        Assert.False(rendered.Elided);
        Assert.False(rendered.Cyclic);
    }

    [Fact]
    public void ANamedFiller_PrefersItsLabel()
    {
        // Arrange & act.
        var rendered = Render("""
            :A rdfs:subClassOf [ a owl:Restriction ; owl:onProperty :p ; owl:someValuesFrom :B ] .
            :B rdfs:label "Topping" .
            """);

        // Assert: rdfs:label wins over the prefixed name (Requirement 1.4).
        Assert.Equal("∃ :p.Topping", rendered.Text);
    }

    [Fact]
    public void DeepNesting_ElidesAtTheCanvasCap_AndTheUncappedFormDoesNot()
    {
        // Arrange: four expression levels - restriction > union > restriction > intersection.
        const string body = """
            :A rdfs:subClassOf [ a owl:Restriction ; owl:onProperty :p ; owl:someValuesFrom
                [ a owl:Class ; owl:unionOf ( :B
                    [ a owl:Restriction ; owl:onProperty :q ; owl:allValuesFrom
                        [ a owl:Class ; owl:intersectionOf ( :C :D ) ] ] ) ] ] .
            """;

        // Act.
        var capped = Render(body, ExpressionRenderer.CanvasDepth);
        var uncapped = Render(body);

        // Assert: the canvas label cuts at depth two with the visible marker; the grid form is whole.
        Assert.Equal("∃ :p.(:B ∪ …)", capped.Text);
        Assert.True(capped.Elided);
        Assert.Equal("∃ :p.(:B ∪ ∀ :q.(:C ∩ :D))", uncapped.Text);
        Assert.False(uncapped.Elided);
    }

    [Fact]
    public void ACyclicStructure_RendersTheGuardInsteadOfRecursingForever()
    {
        // Arrange: a labeled blank restriction whose filler is itself.
        var rendered = Render(":A rdfs:subClassOf _:r . _:r a owl:Restriction ; owl:onProperty :p ; owl:someValuesFrom _:r .");

        // Assert.
        Assert.Equal("∃ :p.…", rendered.Text);
        Assert.True(rendered.Cyclic);
    }

    [Theory]
    [InlineData("[ a owl:Restriction ; owl:someValuesFrom :B ]", "∃ ?.:B")]
    [InlineData("[ a owl:Restriction ; owl:onProperty :p ]", "? :p")]
    [InlineData("[ a owl:Class ]", "? ?")]
    public void MalformedStructures_ShortCircuitToTheProblemLabel(string expression, string expected)
    {
        // Arrange & act: what is there shows, ? stands for what is missing (Requirement 3.5).
        var rendered = Render($":A rdfs:subClassOf {expression} .");

        // Assert.
        Assert.Equal(expected, rendered.Text);
    }

    [Fact]
    public void TheProjection_CarriesTheRenderedLabelAndTheElisionFlag()
    {
        // Arrange & act: the same deep nesting, through OwlProjection.
        var model = RdfParser.Parse(LineDocument.Parse(Prelude + """
            :A a owl:Class ;
                rdfs:subClassOf [ a owl:Restriction ; owl:onProperty :p ; owl:someValuesFrom
                    [ a owl:Class ; owl:unionOf ( :B
                        [ a owl:Restriction ; owl:onProperty :q ; owl:allValuesFrom
                            [ a owl:Class ; owl:intersectionOf ( :C :D ) ] ] ) ] ] .
            """));
        var graph = OwlProjection.Project(model);

        // Assert: the root expression node wears the depth-capped label and the visible marker.
        var root = graph.Nodes.Single(node => node.Id == $"expr:http://example.org/t#A|{OwlVocabulary.SubClassOf}|0");
        Assert.Equal("∃ :p.(:B ∪ …)", root.Display);
        Assert.True(root.Elided);
        Assert.False(root.Malformed);
    }
}
