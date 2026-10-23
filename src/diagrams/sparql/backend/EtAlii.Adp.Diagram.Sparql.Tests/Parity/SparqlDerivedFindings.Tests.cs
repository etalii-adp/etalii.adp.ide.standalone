using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.Sparql.Tests.Parity;

/// <summary>
/// The findings derived from the bundled definition (<see cref="SparqlValidator"/> over
/// <see cref="SparqlDefinition.Findings"/>) are the hand-written validator's (<see cref="HandWrittenSparql.Findings"/>),
/// finding for finding and in order: severity, code, line and sentence, for every document of the corpus and for the
/// texts below, which reach what the corpus does not (sparql-disl-fbl, Requirement 5.3).
/// </summary>
public class SparqlDerivedFindingsTests
{
    private static readonly ToolOrigin _origin = ServiceCollectionAddSparqlExtension.SparqlOrigin;

    /// <summary>Texts the corpus does not hold, each named for what it reaches.</summary>
    public static TheoryData<string, string> Reached() => new()
    {
        { "an empty file", "" },
        { "a prefix declared twice and used by neither", "PREFIX ex: <http://example.org/>\nPREFIX ex: <http://example.org/other/>\nSELECT * WHERE { ?s ?p ?o }\n" },
        { "a prefix used only in a filter", "PREFIX ex: <http://example.org/>\nPREFIX xsd: <http://www.w3.org/2001/XMLSchema#>\nSELECT ?s WHERE { ?s ex:p ?o FILTER(?o > \"1\"^^xsd:integer) }\n" },
        { "a prefix named only in a comment", "PREFIX ex: <http://example.org/>\nPREFIX un: <http://example.org/un/>\n# un:thing\nSELECT ?s WHERE { ?s ex:p ?o }\n" },
        { "two unbound projections and one bound by BIND", "PREFIX ex: <http://example.org/>\nSELECT ?a ?s ?b ?c WHERE { ?s ex:p ?o BIND(?o AS ?c) }\n" },
        { "a projection alias", "PREFIX ex: <http://example.org/>\nSELECT ?s (COUNT(?o) AS ?n) WHERE { ?s ex:p ?o } GROUP BY ?s\n" },
        { "a tokenizer error", "SELECT ?s WHERE { ?s <unterminated ?o }\n" },
        { "a parse error on the first line", "SELECT WHERE\n" },
        { "an update document", "PREFIX ex: <http://example.org/>\nDELETE WHERE { ?s ex:p ?o }\n" },
    };

    public static TheoryData<string> Documents() => [.. SparqlCorpus.Documents()];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryFinding_IsTheHandWrittenValidators(string document) =>
        await AssertSame(document, await File.ReadAllTextAsync(SparqlCorpus.Full(document), TestContext.Current.CancellationToken));

    [Theory]
    [MemberData(nameof(Reached))]
    public async Task EveryFinding_IsTheHandWrittenValidators_WhereTheCorpusDoesNotReach(string name, string text) => await AssertSame(name, text);

    /// <summary>The documents reach every code the hand-written validator has, so the comparisons above are not vacuous.</summary>
    [Fact]
    public async Task TheDocuments_ReachEveryCode()
    {
        // Arrange.
        var texts = new List<string>();
        foreach (var document in SparqlCorpus.Documents()) texts.Add(await File.ReadAllTextAsync(SparqlCorpus.Full(document), TestContext.Current.CancellationToken));
        texts.AddRange(Reached().Select(row => row.Data.Item2));

        // Act.
        var reached = texts.SelectMany(HandWrittenSparql.Findings).Select(problem => problem.RuleId).ToHashSet(StringComparer.Ordinal);

        // Assert.
        Assert.Equal(
            new[] { HandWrittenSparql.UnboundProjectionRuleId, HandWrittenSparql.UnparseableRuleId, HandWrittenSparql.UnusedPrefixRuleId },
            reached.Order(StringComparer.Ordinal));
    }

    private static async Task AssertSame(string name, string text)
    {
        var expected = HandWrittenSparql.Findings(text).Select(Line).ToList();
        var actual = (await new SparqlValidator(_origin).ValidateAsync(
            new DiagramValidationRequest(text, "query.rq", ProjectRoot.From("root"), "root/query.rq", null), TestContext.Current.CancellationToken)).Select(Line).ToList();
        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"{name}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
    }

    private static string Line(DiagramProblem problem) =>
        $"{problem.Severity} | {problem.RuleId} | {(problem.Location as DiagramProblemLineLocation)?.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? problem.Location?.ToString()} | {problem.Message}";
}
