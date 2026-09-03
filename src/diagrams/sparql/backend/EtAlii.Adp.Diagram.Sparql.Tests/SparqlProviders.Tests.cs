using EtAlii.Adp.Backend.Context;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The providers: every property row read-only with its reason, and an action provider that
/// offers nothing - the seam half of the read-only position (Requirements 6.1, 6.2).
/// </summary>
public class SparqlProvidersTests : IDisposable
{
    private readonly string _root;
    private readonly SparqlDocumentStore _documents = new();

    public SparqlProvidersTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string CopyFixture(string name)
    {
        var destination = IoPath.Combine(_root, name);
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name), destination);
        return destination;
    }

    private ContextTarget TargetFor(string bodyPath, string elementId) =>
        new(ContextScope.DiagramElement, bodyPath, IsContainer: false, SourceId: default, _root, ShortGuid.NewShortGuid(), elementId);

    private async Task<IReadOnlyList<ContextPropertyDefinition>> RowsFor(string elementId)
    {
        var provider = new SparqlContextPropertyProvider(_documents);
        return await provider.DescribeAsync(TargetFor(CopyFixture("groups.rq"), elementId), CancellationToken.None);
    }

    [Fact]
    public async Task EveryRow_IsReadOnly_AndSaysWhy()
    {
        // Arrange & act: one selection of each kind the provider answers for.
        var body = CopyFixture("constructs.rq");
        var provider = new SparqlContextPropertyProvider(_documents);
        var everyRow = new List<ContextPropertyDefinition>();
        foreach (var elementId in (string[])
                 ["var:person", "iri:http://example.org/carol", SparqlElementMapper.HeaderId])
        {
            everyRow.AddRange(await provider.DescribeAsync(TargetFor(body, elementId), CancellationToken.None));
        }

        // Assert.
        Assert.NotEmpty(everyRow);
        Assert.All(everyRow, row =>
        {
            ArgumentNullException.ThrowIfNull(row);

            Assert.Equal(SparqlContextPropertyProvider.ReadOnlyReason, row.ReadOnlyReason);
            Assert.Contains("text editor", row.ReadOnlyReason);
        });
    }

    [Fact]
    public async Task AVariablesRows_StateItsNameProjectionAndJoinCount()
    {
        // Arrange & act.
        var rows = await RowsFor("var:x");

        // Assert.
        Assert.Equal("?x", Single(rows, SparqlContextPropertyProvider.VariableNameProperty).Value);
        Assert.Equal("Yes", Single(rows, SparqlContextPropertyProvider.VariableProjectedProperty).Value);
        Assert.Equal("5", Single(rows, SparqlContextPropertyProvider.VariableJoinCountProperty).Value);
    }

    [Fact]
    public async Task ABoundVariablesRow_CarriesItsDefiningExpressionAsWritten()
    {
        // Arrange & act.
        var rows = await RowsFor("var:doubled");

        // Assert.
        Assert.Equal("BIND(?x * 2 AS ?doubled)", Single(rows, SparqlContextPropertyProvider.VariableDefinitionProperty).Value);
    }

    [Fact]
    public async Task ARegionsRows_NameItsKindAndConstraint()
    {
        // Arrange & act.
        var rows = await RowsFor("region:where/service.0");

        // Assert.
        Assert.Equal("SERVICE", Single(rows, SparqlContextPropertyProvider.RegionKindProperty).Value);
        Assert.Contains("http://example.org/sparql", Single(rows, SparqlContextPropertyProvider.RegionLabelProperty).Value);
    }

    [Fact]
    public async Task TheHeaderSelection_StatesTheFormAndModifiers()
    {
        // Arrange & act.
        var body = CopyFixture("constructs.rq");
        var provider = new SparqlContextPropertyProvider(_documents);
        var rows = await provider.DescribeAsync(TargetFor(body, SparqlElementMapper.HeaderId), CancellationToken.None);

        // Assert.
        Assert.Equal("SELECT DISTINCT", Single(rows, SparqlContextPropertyProvider.QueryFormProperty).Value);
        Assert.Contains("LIMIT 10", Single(rows, SparqlContextPropertyProvider.QueryModifiersProperty).Value);
    }

    [Fact]
    public async Task ASetArrivingAnyway_IsRefusedWithTheReason()
    {
        // Arrange.
        var body = CopyFixture("groups.rq");
        var provider = new SparqlContextPropertyProvider(_documents);
        var original = File.ReadAllBytes(body);

        // Act.
        var result = await provider.SetAsync(
            TargetFor(body, "var:x"), SparqlContextPropertyProvider.VariableNameProperty, "y", CancellationToken.None);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(SparqlContextPropertyProvider.ReadOnlyReason, result.Error);
        Assert.Equal(original, File.ReadAllBytes(body));
    }

    [Fact]
    public async Task TheActionProvider_OffersNothingAndRefusesEveryPath()
    {
        // Arrange.
        var body = CopyFixture("groups.rq");
        var provider = new SparqlContextActionProvider();
        var target = TargetFor(body, "var:x");

        // Act & assert.
        Assert.Empty(await provider.DiscoverAsync(target, CancellationToken.None));

        var execution = await provider.ExecuteAsync(target, "anything", CancellationToken.None);
        Assert.Equal(SparqlContextActionProvider.NoActionsReason, Assert.IsType<ContextExecutionFailed>(execution).Message);

        var validation = await provider.ValidateAsync(target, "anything", "value", CancellationToken.None);
        Assert.False(validation.Valid);

        var commit = await provider.CommitAsync(target, "anything", "value", "", CancellationToken.None);
        Assert.False(commit.Completed);
    }

    [Fact]
    public async Task AnotherTypesFile_IsAnsweredWithNothing()
    {
        // Arrange: the provider is consulted for every element in its scope, so it answers with
        // nothing rather than parsing another notation's file.
        var path = IoPath.Combine(_root, "graph.ttl");
        File.WriteAllText(path, "<a> <b> <c> .");
        var provider = new SparqlContextPropertyProvider(_documents);

        // Act & assert.
        Assert.Empty(await provider.DescribeAsync(TargetFor(path, "var:x"), CancellationToken.None));
    }

    private static ContextPropertyDefinition Single(IReadOnlyList<ContextPropertyDefinition> rows, string id) =>
        Assert.Single(rows, row => row.Id == id);
}

/// <summary>The validator's three findings, each on the fixture that provokes it (Requirement 7).</summary>
public class SparqlValidatorTests
{
    private static readonly DiagramOrigin _origin = ServiceCollectionAddSparqlExtension.SparqlOrigin;

    private static async Task<IReadOnlyList<DiagramProblem>> Validate(string text) =>
        await new SparqlValidator(_origin).ValidateAsync(
            new DiagramValidationRequest(text, "query.rq", "root", "root/query.rq", null), CancellationToken.None);

    [Fact]
    public async Task AQueryThatDoesNotParse_IsOneErrorNamingItsLine()
    {
        // Arrange & act.
        var problems = await Validate("PREFIX ex: <http://example.org/>\nSELECT ?s\nWHERE {\n  ?s undeclared:p ?o\n}\n");

        // Assert.
        var problem = Assert.Single(problems);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Equal(SparqlValidator.UnparseableRuleId, problem.RuleId);
        Assert.Equal(4u, Assert.IsType<DiagramProblemLineLocation>(problem.Location).Number);
    }

    [Fact]
    public async Task AnUpdateDocument_IsReportedThroughTheSameDoorByName()
    {
        // Arrange & act.
        var problems = await Validate("PREFIX ex: <http://example.org/>\nINSERT DATA { ex:s ex:p ex:o }\n");

        // Assert.
        var problem = Assert.Single(problems);
        Assert.Contains("SPARQL Update", problem.Message);
    }

    [Fact]
    public async Task AProjectedVariableTheWhereClauseNeverBinds_IsWarned()
    {
        // Arrange & act: legal SPARQL, and a silently empty column.
        var problems = await Validate("PREFIX ex: <http://example.org/>\nSELECT ?s ?missing WHERE { ?s ex:p ?o }\n");

        // Assert.
        var problem = Assert.Single(problems, candidate => candidate.RuleId == SparqlValidator.UnboundProjectionRuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Contains("?missing", problem.Message);
    }

    [Fact]
    public async Task AnUnusedPrefixDeclaration_IsInfoAtItsLine()
    {
        // Arrange & act.
        var problems = await Validate("PREFIX ex: <http://example.org/>\nPREFIX unused: <http://example.org/unused/>\nSELECT * WHERE { ?s ex:p ?o }\n");

        // Assert.
        var problem = Assert.Single(problems, candidate => candidate.RuleId == SparqlValidator.UnusedPrefixRuleId);
        Assert.Equal(DiagramProblemSeverity.Info, problem.Severity);
        Assert.Contains("unused:", problem.Message);
        Assert.Equal(2u, Assert.IsType<DiagramProblemLineLocation>(problem.Location).Number);
    }

    [Fact]
    public async Task ACleanQuery_ReportsNothing()
    {
        // Arrange & act.
        var problems = await Validate("PREFIX ex: <http://example.org/>\nSELECT ?s WHERE { ?s ex:p ?o }\nLIMIT 10\n");

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public async Task AServiceClause_IsNeverContacted()
    {
        // Arrange & act: drawn, never resolved - and the module references no HTTP client at
        // all, which NoWriterSurfaceTests asserts.
        var problems = await Validate(
            "PREFIX ex: <http://example.org/>\nSELECT ?s WHERE { SERVICE <http://example.invalid/sparql> { ?s ex:p ?o } }\n");

        // Assert.
        Assert.Empty(problems);
    }
}
