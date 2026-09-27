using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The ontology validator: the Requirement 5 findings and the imports info (owl-diagram
/// Requirements 4.2, 5.1-5.5) - each firing on its fixture, all quiet on a clean file, and
/// nothing of the anchor's file-level rules restated.
/// </summary>
public class OwlValidatorTests
{
    private const string Prelude = """
        @prefix : <http://example.org/t#> .
        @prefix owl: <http://www.w3.org/2002/07/owl#> .
        @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .

        """;

    private static IReadOnlyList<DiagramProblem> Judge(string body) =>
        OwlValidator.Judge(RdfParser.Parse(LineDocument.Parse(Prelude + body)));

    [Fact]
    public void ACleanOntology_ReportsNothing()
    {
        // Arrange & act: declarations, a hierarchy, a well-formed restriction, an individual.
        var problems = Judge("""
            :Pizza a owl:Class .
            :Topping a owl:Class .
            :Vegetarian a owl:Class ;
                rdfs:subClassOf :Pizza ;
                rdfs:subClassOf [ a owl:Restriction ; owl:onProperty :hasTopping ; owl:allValuesFrom :Topping ] .
            :hasTopping a owl:ObjectProperty ; rdfs:domain :Pizza ; rdfs:range :Topping .
            :m a :Pizza .
            """);

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public void ASubclassCycle_WarnsNamingItsMembers()
    {
        // Arrange & act.
        var problems = Judge("""
            :A a owl:Class ; rdfs:subClassOf :B .
            :B a owl:Class ; rdfs:subClassOf :A .
            """);

        // Assert: a warning - the cycle is legal OWL and almost always a mistake (Requirement 5.1).
        var problem = Assert.Single(problems);
        Assert.Equal(OwlValidator.SubclassCycleRuleId, problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Contains(":A", problem.Message);
        Assert.Contains(":B", problem.Message);
    }

    [Fact]
    public void AMalformedExpression_IsAnErrorWithItsLine()
    {
        // Arrange & act: a restriction with no owl:onProperty (Requirement 5.2).
        var problems = Judge("""
            :A a owl:Class ;
                rdfs:subClassOf [ a owl:Restriction ; owl:someValuesFrom :B ] .
            :B a owl:Class .
            """);

        // Assert.
        var problem = Assert.Single(problems);
        Assert.Equal(OwlValidator.MalformedExpressionRuleId, problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.True(((DiagramProblemLineLocation)problem.Location!).Number > 0);
    }

    [Fact]
    public void AnUndeclaredProperty_IsNamedOnce()
    {
        // Arrange & act: :p carries axioms but no declaration anywhere in the file (Requirement 5.3).
        var problems = Judge("""
            :A a owl:Class .
            :p rdfs:domain :A .
            :p rdfs:range :A .
            """);

        // Assert: named once, not once per axiom.
        var problem = Assert.Single(problems);
        Assert.Equal(OwlValidator.UndeclaredPropertyRuleId, problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Info, problem.Severity);
        Assert.Contains(":p", problem.Message);
        Assert.Contains("not fetched", problem.Message);
    }

    [Fact]
    public void ADeprecatedEntity_StillReferenced_IsReported()
    {
        // Arrange & act (Requirement 5.4).
        var problems = Judge("""
            :Old a owl:Class ; owl:deprecated true .
            :New a owl:Class ; rdfs:subClassOf :Old .
            """);

        // Assert.
        var problem = Assert.Single(problems);
        Assert.Equal(OwlValidator.DeprecatedReferenceRuleId, problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Info, problem.Severity);
        Assert.Contains(":Old", problem.Message);
    }

    [Fact]
    public void Imports_AreNamedAsNotFetched()
    {
        // Arrange & act: the no-network rule made visible (Requirement 4.2).
        var problems = Judge("""
            <http://example.org/t> a owl:Ontology ;
                owl:imports <http://example.org/base> .
            """);

        // Assert.
        var problem = Assert.Single(problems);
        Assert.Equal(OwlValidator.ImportsNotFetchedRuleId, problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Info, problem.Severity);
        Assert.Contains("http://example.org/base", problem.Message);
        Assert.Contains("not fetched", problem.Message);
    }

    [Fact]
    public async Task AnUnparseableFile_CarriesTheFamilyFindingExactlyOnce()
    {
        // Arrange: nothing the parser can read.
        var validator = new OwlValidator(new DiagramOrigin("w3c", "owl"));

        // Act.
        var problems = await validator.ValidateAsync(
            new DiagramValidationRequest("not turtle at all {{{", "broken.ttl", "", "", null),
            TestContext.Current.CancellationToken);

        // Assert: the one finding is the family's unparseable rule, delegated rather than
        // restated (Requirement 5.5) - and this reading adds nothing on top of a broken file.
        var problem = Assert.Single(problems);
        Assert.Equal(RdfValidator.UnparseableRuleId, problem.RuleId);
    }
}
