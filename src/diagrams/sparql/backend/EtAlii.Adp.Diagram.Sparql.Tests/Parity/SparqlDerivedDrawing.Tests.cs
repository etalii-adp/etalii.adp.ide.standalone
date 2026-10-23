using System.Globalization;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.Sparql.Tests.Parity;

/// <summary>
/// The drawn query derived from the bundled definition (<see cref="SparqlDefinition.Drawing(SparqlDocument)"/>) is the
/// hand-written projection's (<see cref="HandWrittenSparql"/>) for every document of the corpus, element for element:
/// the same ids, in the same order, with the same fields, cut at 500 into the same elements with the same two numbers
/// (sparql-disl-fbl, Requirements 4.2 to 4.4).
/// </summary>
public class SparqlDerivedDrawingTests
{
    /// <summary>The findings of a derived type that went wrong, none of which may occur over the corpus.</summary>
    private static readonly string[] _derivedTypeFindings = [DislModelBuilder.DerivedFailed, DislModelBuilder.DerivedId, DislModelBuilder.Containment];

    public static TheoryData<string> Documents() => [.. SparqlCorpus.Documents()];

    [Theory]
    [MemberData(nameof(Documents))]
    public void TheDerivedDrawing_IsTheHandWrittenOne(string document)
    {
        // Arrange.
        var text = File.ReadAllText(SparqlCorpus.Full(document));
        var query = SparqlParser.Outcome(text).Model;

        // Act.
        var derived = new SparqlDocument(query, text).Drawing;

        // Assert.
        Assert.Equal(SparqlDrawingLines.Of(HandWrittenSparql.Project(query)), SparqlDrawingLines.Of(derived));
    }

    [Fact]
    public void NoDerivedType_GoesWrong_OverTheCorpus()
    {
        // Arrange and act.
        var findings = SparqlCorpus.Documents()
            .Select(document => (Document: document, Text: File.ReadAllText(SparqlCorpus.Full(document))))
            .SelectMany(document => SparqlDefinition.Model(SparqlParser.Outcome(document.Text).Model, document.Text).Findings
                .Select(finding => $"{document.Document}: {finding}"))
            .ToList();

        // Assert.
        Assert.DoesNotContain(findings, finding => _derivedTypeFindings.Any(code => finding.Contains($": {code}", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheCutAt500_KeepsTheHandWrittenElements_AndReportsItsTwoNumbers()
    {
        // Arrange.
        var text = File.ReadAllText(SparqlCorpus.Full(SparqlCorpus.Documents().Single(document => document.EndsWith("/over-the-bound.rq", StringComparison.Ordinal))));

        // Act.
        var derived = new SparqlDocument(SparqlParser.Parse(text), text).Drawing;

        // Assert.
        Assert.True(derived.Truncated);
        Assert.Equal((500, 505), (derived.Shown, derived.Total));
        Assert.Equal(SparqlDrawingLines.Of(HandWrittenSparql.Project(SparqlParser.Parse(text))), SparqlDrawingLines.Of(derived));
    }

    [Theory]
    [InlineData("haw-US")]
    [InlineData("th-TH")]
    public void TheVariableOrder_IsTheInvariantOne_UnderEveryCulture(string culture)
    {
        // Arrange: the hand-written projection orders scope paths by the current culture (finding A11); the
        // definition orders them by code point, which is the invariant culture's order for every path it produces.
        var text = File.ReadAllText(SparqlCorpus.Full(SparqlCorpus.Documents().Single(document => document.EndsWith("/scope-paths-by-culture.rq", StringComparison.Ordinal))));
        var current = CultureInfo.CurrentCulture;
        IReadOnlyList<string> expected;
        IReadOnlyList<string> derived;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            expected = SparqlDrawingLines.Of(HandWrittenSparql.Project(SparqlParser.Parse(text)));

            // Act.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            derived = SparqlDrawingLines.Of(new SparqlDocument(SparqlParser.Parse(text), text).Drawing);
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
        }

        // Assert.
        Assert.Equal(expected, derived);
    }
}
