using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// What makes a vocabulary lie, named with lines (skos-diagram Requirement 7): each finding on
/// its own fixture, a clean vocabulary reporting nothing, the family's text facts composed
/// rather than restated, and the cycle finding fed by the layout's own detection.
/// </summary>
public class SkosValidatorTests : IDisposable
{
    private static readonly DiagramOrigin Origin = new("w3c", "skos");
    private readonly string _root;

    public SkosValidatorTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private const string Prelude = """
        @prefix skos: <http://www.w3.org/2004/02/skos/core#> .
        @prefix ex: <http://example.org/> .

        """;

    private async Task<IReadOnlyList<DiagramProblem>> Validate(string turtle, string? registrationPath = null)
    {
        var request = new DiagramValidationRequest(Prelude + turtle, "scheme", _root, IoPath.Combine(_root, "scheme.ttl"), registrationPath);
        return await new SkosValidator(Origin).ValidateAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ACleanVocabulary_ReportsNothing()
    {
        // Arrange & act.
        var problems = await Validate("""
            ex:scheme a skos:ConceptScheme ; skos:prefLabel "Scheme"@en ; skos:hasTopConcept ex:top .
            ex:top a skos:Concept ; skos:prefLabel "Top"@en ; skos:topConceptOf ex:scheme .
            ex:leaf a skos:Concept ; skos:prefLabel "Leaf"@en ; skos:inScheme ex:scheme ; skos:broader ex:top .
            """);

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public async Task AnUnparseableFile_IsThatOneFinding_AndNothingElse()
    {
        // Arrange & act: the family's rule, composed - not restated.
        var problems = await Validate("ex:a skos:broader");

        // Assert.
        var problem = Assert.Single(problems);
        Assert.Equal(RdfValidator.UnparseableRuleId, problem.RuleId);
    }

    [Fact]
    public async Task ACycle_IsOneErrorNamingItsConcepts_FromTheLayoutsOwnDetection()
    {
        // Arrange & act.
        var problems = await Validate("""
            ex:a a skos:Concept ; skos:prefLabel "A"@en ; skos:broader ex:b .
            ex:b a skos:Concept ; skos:prefLabel "B"@en ; skos:broader ex:a .
            """);

        // Assert: the same knot the layout breaks, and lines from the stating triples.
        var cycle = Assert.Single(problems, problem => problem.RuleId == SkosValidator.CycleRuleId);
        Assert.Equal(DiagramProblemSeverity.Error, cycle.Severity);
        Assert.Contains("A", cycle.Message, StringComparison.Ordinal);
        Assert.Contains("B", cycle.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicatePreferredLabels_InOneLanguage_AreAnError()
    {
        // Arrange & act.
        var problems = await Validate("""
            ex:a a skos:Concept ; skos:prefLabel "Tea"@en ; skos:prefLabel "Cuppa"@en .
            """);

        // Assert.
        var duplicate = Assert.Single(problems, problem => problem.RuleId == SkosValidator.DuplicatePrefLabelRuleId);
        Assert.Contains("'en'", duplicate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RelatedAcrossADirectHierarchyLink_Warns_AndSaysWhatIsNotChecked()
    {
        // Arrange & act: S27's direct case; the transitive case needs entailment and is
        // deliberately not checked - the message says so.
        var problems = await Validate("""
            ex:a a skos:Concept ; skos:prefLabel "A"@en .
            ex:b a skos:Concept ; skos:prefLabel "B"@en ; skos:broader ex:a ; skos:related ex:a .
            """);

        // Assert.
        var overlap = Assert.Single(problems, problem => problem.RuleId == SkosValidator.RelatedOverlapRuleId);
        Assert.Contains("direct case", overlap.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingPreferredLabel_Warns_AndAnUnfiledConceptInforms()
    {
        // Arrange & act: one concept with neither label nor scheme.
        var problems = await Validate("""
            ex:orphan a skos:Concept .
            """);

        // Assert.
        Assert.Single(problems, problem => problem.RuleId == SkosValidator.MissingPrefLabelRuleId);
        Assert.Single(problems, problem => problem.RuleId == SkosValidator.UnfiledConceptRuleId);
    }

    [Fact]
    public async Task AConceptReachableFromATop_IsNotReportedUnfiled()
    {
        // Arrange & act: no membership assertion, but hanging under the scheme's top - drawn in
        // the unfiled band per Requirement 1.1, yet not the mistake the info exists for.
        var problems = await Validate("""
            ex:scheme a skos:ConceptScheme ; skos:prefLabel "S"@en ; skos:hasTopConcept ex:top .
            ex:top a skos:Concept ; skos:prefLabel "Top"@en ; skos:topConceptOf ex:scheme .
            ex:child a skos:Concept ; skos:prefLabel "Child"@en ; skos:broader ex:top .
            """);

        // Assert.
        Assert.DoesNotContain(problems, problem => problem.RuleId == SkosValidator.UnfiledConceptRuleId);
    }

    [Fact]
    public async Task SkosXlLabels_AreOneInfoPerFile()
    {
        // Arrange & act.
        var problems = await Validate("""
            @prefix skosxl: <http://www.w3.org/2008/05/skos-xl#> .
            ex:a a skos:Concept ; skos:prefLabel "A"@en ; skosxl:prefLabel ex:aLabel ; skos:inScheme ex:s .
            ex:b a skos:Concept ; skos:prefLabel "B"@en ; skosxl:altLabel ex:bLabel ; skos:inScheme ex:s .
            ex:s a skos:ConceptScheme ; skos:prefLabel "S"@en .
            """);

        // Assert.
        Assert.Single(problems, problem => problem.RuleId == SkosValidator.XlLabelsRuleId);
    }

    [Fact]
    public async Task AHierarchyEndThatIsNotAConcept_Warns()
    {
        // Arrange & act: typing is never inferred; the file's own silence is reported.
        var problems = await Validate("""
            ex:a a skos:Concept ; skos:prefLabel "A"@en ; skos:inScheme ex:s ; skos:broader ex:untyped .
            ex:s a skos:ConceptScheme ; skos:prefLabel "S"@en .
            """);

        // Assert.
        var warning = problems.Where(problem => problem.RuleId == SkosValidator.NonConceptRuleId).ToList();
        Assert.NotEmpty(warning);
        Assert.Contains(warning, problem => problem.Message.Contains("untyped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMisplacedLanguageHeader_IsNamedWithItsLine()
    {
        // Arrange: the registration whose language: line sits above body: - ignored by the
        // session (2.1's half), and named here (this half).
        var registration = IoPath.Combine(_root, "scheme.adp");
        await File.WriteAllTextAsync(registration, "w3c/skos\nlanguage: nl\nbody: scheme.ttl\n", TestContext.Current.CancellationToken);

        // Act.
        var problems = await Validate("""
            ex:s a skos:ConceptScheme ; skos:prefLabel "S"@en .
            """, registration);

        // Assert.
        var misplaced = Assert.Single(problems, problem => problem.RuleId == SkosValidator.MisplacedHeaderRuleId);
        Assert.Contains("line 2", misplaced.Message, StringComparison.Ordinal);
    }
}
