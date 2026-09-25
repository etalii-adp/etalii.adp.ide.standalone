using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The shapes validator (shacl-diagram Requirement 7): facts about the shapes file, composed on
/// top of the family's text facts - and never a word about whether any data conforms.
/// </summary>
public class ShaclValidatorTests
{
    private const string Prelude = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.org/> .
        @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

        """;

    private static readonly DiagramOrigin _origin = new("w3c", "shacl");

    private static async Task<IReadOnlyList<DiagramProblem>> Validate(string body)
    {
        var validator = new ShaclValidator(_origin);
        var request = new DiagramValidationRequest(Prelude + body, "shapes", "/root", "/root/shapes.ttl", null);
        return await validator.ValidateAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AWellFormedShapesFile_ReportsNothing()
    {
        var problems = await Validate("""
            ex:PersonShape a sh:NodeShape ;
              sh:targetClass ex:Person ;
              sh:property [ sh:path ex:name ; sh:datatype xsd:string ; sh:minCount 1 ; sh:maxCount 1 ] .
            """ + "\n");

        Assert.Empty(problems);
    }

    [Fact]
    public async Task APropertyShapeWithoutAPath_IsAnError()
    {
        var problems = await Validate("ex:S a sh:NodeShape ; sh:property [ sh:minCount 1 ] .\n");

        var problem = Assert.Single(problems, candidate => candidate.RuleId == ShaclValidator.PathCardinalityRuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Contains("no sh:path", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APropertyShapeWithTwoPaths_IsAnError()
    {
        var problems = await Validate("ex:S a sh:NodeShape ; sh:property [ sh:path ex:a ; sh:path ex:b ] .\n");

        var problem = Assert.Single(problems, candidate => candidate.RuleId == ShaclValidator.PathCardinalityRuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Contains("2 sh:path", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANodeShapeWithoutAPath_IsNotAFinding()
    {
        // A node shape has no path by definition; silence here is correctness, not leniency.
        var problems = await Validate("ex:S a sh:NodeShape ; sh:targetClass ex:Person ; sh:closed true .\n");

        Assert.DoesNotContain(problems, problem => problem.RuleId == ShaclValidator.PathCardinalityRuleId);
    }

    [Fact]
    public async Task MinCountAboveMaxCount_IsAWarning()
    {
        var problems = await Validate("ex:S a sh:NodeShape ; sh:property [ sh:path ex:p ; sh:minCount 3 ; sh:maxCount 1 ] .\n");

        var problem = Assert.Single(problems, candidate => candidate.RuleId == ShaclValidator.ImpossibleCountsRuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Contains("nothing can ever conform", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatatypeAndClassTogether_IsAWarning()
    {
        var problems = await Validate("ex:S a sh:NodeShape ; sh:property [ sh:path ex:p ; sh:datatype xsd:string ; sh:class ex:Person ] .\n");

        var problem = Assert.Single(problems, candidate => candidate.RuleId == ShaclValidator.DatatypeAndClassRuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
    }

    [Fact]
    public async Task AMisspelledShaclTerm_IsAWarning_NamingIt()
    {
        // sh:minCoumt is read by no processor, so the constraint the author meant is absent.
        var problems = await Validate("ex:S a sh:NodeShape ; sh:property [ sh:path ex:p ; sh:minCoumt 1 ] .\n");

        var problem = Assert.Single(problems, candidate => candidate.RuleId == ShaclValidator.UnknownTermRuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Contains("sh:minCoumt", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryRealShaclTerm_IsAccepted()
    {
        var problems = await Validate("""
            ex:S a sh:NodeShape ;
              sh:severity sh:Warning ;
              sh:message "careful" ;
              sh:deactivated false ;
              sh:closed true ;
              sh:ignoredProperties ( ex:x ) ;
              sh:property [ sh:path [ sh:inversePath ex:parent ] ; sh:nodeKind sh:IRI ; sh:pattern "^a" ; sh:flags "i" ] ;
              sh:or ( [ sh:datatype xsd:string ] [ sh:datatype xsd:integer ] ) .
            """ + "\n");

        Assert.DoesNotContain(problems, problem => problem.RuleId == ShaclValidator.UnknownTermRuleId);
    }

    [Fact]
    public async Task ATargetNamingAnAbsentTerm_IsNotAFinding()
    {
        // The load-bearing silence: a shapes graph aims at data that lives elsewhere, and saying
        // so would be reporting the medium as an error (Requirement 4.3).
        var problems = await Validate("""
            ex:S a sh:NodeShape ;
              sh:targetClass ex:NowhereDefined ;
              sh:targetNode ex:AlsoAbsent ;
              sh:targetSubjectsOf ex:unseen .
            """ + "\n");

        Assert.Empty(problems);
    }

    [Fact]
    public async Task NoFindingEverSpeaksOfDataConformance()
    {
        var problems = await Validate("""
            ex:S a sh:NodeShape ;
              sh:targetClass ex:Person ;
              sh:property [ sh:path ex:name ; sh:minCount 3 ; sh:maxCount 1 ] .
            ex:alice a ex:Person .
            """ + "\n");

        // ex:alice violates the shape on any real SHACL engine. This tool draws shapes and never
        // runs them, so the only finding is about the shapes file's own contradiction.
        Assert.Single(problems);
        Assert.Equal(ShaclValidator.ImpossibleCountsRuleId, problems[0].RuleId);
        Assert.DoesNotContain(problems, problem =>
            problem.Message.Contains("alice", StringComparison.OrdinalIgnoreCase)
            || problem.Message.Contains("conforms", StringComparison.OrdinalIgnoreCase)
            || problem.Message.Contains("violat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AShapeReferenceLeavingTheFile_IsAnInfo_NotAWarning()
    {
        var problems = await Validate("ex:S a sh:NodeShape ; sh:node ex:DefinedNextDoor .\n");

        var problem = Assert.Single(problems, candidate => candidate.RuleId == ShaclValidator.ReferenceLeavesFileRuleId);

        // Info because this tool reads one file: it cannot tell "missing" from "described
        // elsewhere", and a warning would be an accusation the evidence does not support.
        Assert.Equal(DiagramProblemSeverity.Info, problem.Severity);
        Assert.Contains("elsewhere, or missing", problem.Message, StringComparison.Ordinal);

        // And it is below the bar the examples are measured against.
        Assert.DoesNotContain(problems, candidate => candidate.Severity > DiagramProblemSeverity.Info);
    }

    [Fact]
    public async Task AShapeReferenceThisFileDescribes_IsNotAFinding()
    {
        var problems = await Validate("""
            ex:S a sh:NodeShape ; sh:node ex:Other .
            ex:Other a sh:NodeShape .
            """ + "\n");

        Assert.DoesNotContain(problems, problem => problem.RuleId == ShaclValidator.ReferenceLeavesFileRuleId);
    }

    [Fact]
    public async Task ATargetLeavingTheFile_StaysSilentWhileAReferenceDoesNot()
    {
        // The distinction Requirement 4.3 draws, in one file: the target is the medium working,
        // the shape reference is a loose end worth mentioning quietly.
        var problems = await Validate("ex:S a sh:NodeShape ; sh:targetClass ex:UnseenClass ; sh:node ex:UnseenShape .\n");

        var problem = Assert.Single(problems);
        Assert.Equal(ShaclValidator.ReferenceLeavesFileRuleId, problem.RuleId);
        Assert.Contains("UnseenShape", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("UnseenClass", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnparseableFile_IsThatOneFindingAndNothingElse()
    {
        var problems = await Validate("ex:S a sh:NodeShape ; sh:property [ sh:path ex:p \n");

        Assert.Single(problems);
        Assert.Equal(RdfValidator.UnparseableRuleId, problems[0].RuleId);
    }

    [Fact]
    public async Task TheFamilysOwnFindings_ComeThroughComposed_NotRestated()
    {
        // A duplicate prefix declaration is the family's rule; it must still be reported for a
        // shapes-registered file, exactly once, from the composed validator.
        var validator = new ShaclValidator(_origin);
        var text = """
            @prefix sh: <http://www.w3.org/ns/shacl#> .
            @prefix ex: <http://example.org/> .
            @prefix ex: <http://elsewhere.example/> .

            ex:S a sh:NodeShape .
            """ + "\n";

        var problems = await validator.ValidateAsync(
            new DiagramValidationRequest(text, "shapes", "/root", "/root/shapes.ttl", null), TestContext.Current.CancellationToken);

        Assert.Single(problems, problem => problem.RuleId == RdfValidator.DuplicatePrefixRuleId);
    }
}
