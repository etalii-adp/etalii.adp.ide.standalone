using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The Requirement 7 findings: facts about the text with file and line, no inference, no
/// network - and nothing at all for a clean file.
/// </summary>
public class RdfValidatorTests
{
    private static async Task<IReadOnlyList<DiagramProblem>> Validate(string text)
    {
        var validator = new RdfValidator(ServiceCollectionAddRdfExtension.RdfOrigin);
        return await validator.ValidateAsync(
            new DiagramValidationRequest(text, "graph", "", "", null),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ACleanFile_ReportsNothing()
    {
        // Arrange & act.
        var problems = await Validate(
            "@prefix ex: <http://example.org/> .\r\n"
            + "ex:alice ex:name \"Alice\"@en ;\r\n"
            + "    ex:age \"42\"^^<http://www.w3.org/2001/XMLSchema#integer> .\r\n");

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public async Task AnUnparseableFile_ReportsOnceWithItsLine()
    {
        // Arrange & act.
        var problems = await Validate("@prefix ex: <http://example.org/> .\r\nex:alice ex:knows\r\n");

        // Assert.
        var problem = Assert.Single(problems);
        Assert.Equal(RdfValidator.UnparseableRuleId, problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
    }

    [Fact]
    public async Task ADuplicatePrefix_IsWarnedAtTheRedeclaration()
    {
        // Arrange & act.
        var problems = await Validate(
            "@prefix ex: <http://example.org/> .\r\n"
            + "@prefix ex: <http://elsewhere.example/> .\r\n"
            + "ex:a ex:b ex:c .\r\n");

        // Assert.
        var problem = Assert.Single(problems, p => p.RuleId == RdfValidator.DuplicatePrefixRuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
    }

    [Fact]
    public async Task RelativeIrisWithoutABase_AreWarnedOnce()
    {
        // Arrange & act.
        var problems = await Validate(
            "@prefix ex: <http://example.org/> .\r\n"
            + "<one> ex:p <two> .\r\n"
            + "<three> ex:p <four> .\r\n");

        // Assert.
        // Once, not once per term: the fix - declare a base - is one edit (Requirement 7.3).
        var problem = Assert.Single(problems, p => p.RuleId == RdfValidator.RelativeIriRuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
    }

    [Fact]
    public async Task WithABase_RelativeIrisAreFine()
    {
        // Arrange & act.
        var problems = await Validate(
            "@base <http://example.org/> .\r\n"
            + "@prefix ex: <http://example.org/ns#> .\r\n"
            + "<one> ex:p <two> .\r\n");

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public async Task AMalformedLanguageTag_IsWarnedWithItsLine()
    {
        // Arrange & act.
        var problems = await Validate(
            "@prefix ex: <http://example.org/> .\r\n"
            + "ex:a ex:label \"x\"@en-- .\r\n");

        // Assert.
        var problem = Assert.Single(problems, p => p.RuleId == RdfValidator.LanguageTagRuleId);
        Assert.Contains("en--", problem.Message);
    }

    [Fact]
    public async Task AnUnknownXsdDatatype_IsWarned_ButForeignDatatypesAreNot()
    {
        // Arrange & act.
        var problems = await Validate(
            "@prefix ex: <http://example.org/> .\r\n"
            + "ex:a ex:v \"x\"^^<http://www.w3.org/2001/XMLSchema#nope> .\r\n"
            + "ex:a ex:w \"y\"^^ex:ownType .\r\n");

        // Assert.
        // A custom datatype outside XSD is somebody's vocabulary, not a mistake.
        var problem = Assert.Single(problems);
        Assert.Equal(RdfValidator.UnknownDatatypeRuleId, problem.RuleId);
        Assert.Contains("nope", problem.Message);
    }

    [Fact]
    public void AddRdf_RegistersTheValidatorsPerReading()
    {
        // Arrange & act.
        var provider = new ServiceCollection().AddRdf().BuildServiceProvider();
        var validators = provider.GetServices<IDiagramValidator>().ToList();

        // Assert: exactly one validator per origin - core's DiagramValidators enforces it - the
        // anchor's own for w3c/rdf and the ontology reading's for w3c/owl, which delegates the
        // file-level rules to the family validator rather than restating them (owl-diagram
        // Requirement 5.5). The other sibling readings register their own the same way.
        var rdf = Assert.Single(validators, validator => validator.Origin == ServiceCollectionAddRdfExtension.RdfOrigin);
        Assert.IsType<RdfValidator>(rdf);
        var owl = Assert.Single(validators, validator => validator.Origin == ServiceCollectionAddRdfExtension.OwlOrigin);
        Assert.IsType<OwlValidator>(owl);
    }
}
