using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.Sparql.Tests.Parity;

/// <summary>
/// The property rows derived from the bundled definition (<see cref="SparqlDefinition.Rows"/> and, for the header band,
/// <see cref="SparqlChrome.Rows"/>) are the hand-written provider's (<see cref="HandWrittenSparql.Rows"/>) for every
/// drawn element of every document of the corpus, the header band, the banner and an unknown id: the same ids, labels,
/// values, editors, reasons and groups, in the same order (sparql-disl-fbl, Requirement 5.1).
/// </summary>
public class SparqlDerivedRowsTests
{
    public static TheoryData<string> Documents() => [.. SparqlCorpus.Documents()];

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryRow_IsTheHandWrittenProvidersRow(string document)
    {
        // Arrange.
        var text = File.ReadAllText(SparqlCorpus.Full(document));
        var outcome = SparqlParser.Outcome(text);
        if (!outcome.IsUsable)
        {
            // An unusable entry has no rows, derived or not: the provider answers before either is asked.
            return;
        }
        var projection = HandWrittenSparql.Project(outcome.Model);
        var derived = new SparqlDocument(SparqlBody.Parse(text));

        // Act and assert.
        foreach (var id in SparqlDerived.Targets(projection))
        {
            var expected = Lines(HandWrittenSparql.Rows(projection, id));
            var actual = Lines(SparqlChrome.IsChrome(id) ? SparqlChrome.Rows(derived, id) : SparqlDefinition.Rows(derived, id));
            Assert.True(expected.SequenceEqual(actual, StringComparer.Ordinal), $"{document} {id}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
        }
    }

    [Fact]
    public void AnElementTheBudgetCut_HasNoRows_AsTheHandWrittenProviderGaveNone()
    {
        // Arrange: over the bound, the model holds elements the drawing does not.
        var text = File.ReadAllText(SparqlCorpus.Full(SparqlCorpus.Documents().Single(document => document.EndsWith("/over-the-bound.rq", StringComparison.Ordinal))));
        var query = SparqlParser.Parse(text);
        var derived = new SparqlDocument(SparqlBody.Parse(text));
        var diagram = derived.Disl.Diagram;
        var cut = diagram.Elements.Where(element => !diagram.IsDrawn(element) && SparqlDerived.IsWireId(element.Id)).Select(element => element.Id).ToList();

        // Act and assert.
        Assert.NotEmpty(cut);
        Assert.All(cut, id =>
        {
            Assert.Empty(HandWrittenSparql.Rows(HandWrittenSparql.Project(query), id));
            Assert.Empty(SparqlDefinition.Rows(derived, id));
        });
    }

    private static List<string> Lines(IReadOnlyList<ContextPropertyDefinition> rows) =>
        [.. rows.Select(row => $"{row.Id} | {row.Label} | {row.Value} | {row.Editor} | {row.ReadOnlyReason} | {row.Group} | {string.Join(",", row.Candidates ?? [])}")];
}
