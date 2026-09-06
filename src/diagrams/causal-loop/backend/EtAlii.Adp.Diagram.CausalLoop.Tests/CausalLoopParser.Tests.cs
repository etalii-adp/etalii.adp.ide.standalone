using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// The body format and its reading (causal-loop-diagram Requirements 1.2, 1.3, 2.2, 3.6): one
/// statement per line, polarity read in both traditions and never inferred, and a document that
/// comes back byte for byte.
/// </summary>
public class CausalLoopParserTests
{
    private const string Corpus =
        "causal-loop 1\r\n"
        + "\r\n"
        + "# the classic pair\r\n"
        + "variable population \"Population\"\r\n"
        + "variable births \"Births\"\r\n"
        + "variable deaths \"Deaths\"\r\n"
        + "\r\n"
        + "link population -> births +\r\n"
        + "link births -> population +\r\n"
        + "link population -> deaths +\r\n"
        + "link deaths -> population - delayed weight=1.5 \"lagged\"\r\n"
        + "\r\n"
        + "loop R1 \"Births beget births\" population births\r\n"
        + "loop B1 \"Deaths check growth\" population deaths\r\n";

    private static CausalLoopParseResult Parse(string text) =>
        CausalLoopParser.Parse(CausalLoopDocument.Parse(text));

    [Fact]
    public void TheCorpus_ReadsItsVariablesLinksAndLoops()
    {
        // Act.
        var result = Parse(Corpus);

        // Assert.
        Assert.Empty(result.Problems);
        Assert.Equal(3, result.Model.Variables.Count);
        Assert.Equal(4, result.Model.Links.Count);
        Assert.Equal(2, result.Model.Loops.Count);
    }

    [Fact]
    public void AVariable_CarriesItsLabel_AndFallsBackToItsIdWithoutOne()
    {
        // Act.
        var labelled = Assert.Single(Parse("variable population \"Population\"\r\n").Model.Variables);
        var bare = Assert.Single(Parse("variable population\r\n").Model.Variables);

        // Assert.
        Assert.Equal("Population", labelled.Display);
        Assert.Equal("population", bare.Display);
    }

    [Fact]
    public void ALink_CarriesItsDelayItsWeightAndItsLabel()
    {
        // Act.
        var link = Parse(Corpus).Model.Links.Single(candidate => candidate.From == "deaths");

        // Assert.
        Assert.Equal(CausalLoopPolarity.Negative, link.Polarity);
        Assert.True(link.Delayed);
        Assert.Equal(1.5, link.Weight);
        Assert.Equal("lagged", link.Label);
    }

    [Fact]
    public void ALink_IsUnflippedUnlessItSaysSo_AndReadsTheWordAsAModifier()
    {
        // Which side an arc bows to is the module's own decision until the document overrides
        // it, so silence has to mean "the module decides" rather than "flipped false by luck" -
        // and the word has to be read as a modifier rather than swallowed as the link's label,
        // which is what an unrecognised token becomes.
        // Act.
        var silent = Parse(Corpus).Model.Links.Single(candidate => candidate.From == "deaths");
        var stated = Assert.Single(
            Parse(Corpus.Replace(
                "link population -> births +",
                "link population -> births + flipped",
                StringComparison.Ordinal)).Model.Links,
            candidate => candidate.From == "population" && candidate.To == "births");

        // Assert.
        Assert.False(silent.Flipped);
        Assert.True(stated.Flipped);
        Assert.Equal("", stated.Label);
    }

    /// <summary>
    /// Both notations are in live use and mean the same two things, so a document written in
    /// either reads the same (Requirement 2.2).
    /// </summary>
    [Theory]
    [InlineData("+", CausalLoopPolarity.Positive)]
    [InlineData("s", CausalLoopPolarity.Positive)]
    [InlineData("S", CausalLoopPolarity.Positive)]
    [InlineData("-", CausalLoopPolarity.Negative)]
    [InlineData("o", CausalLoopPolarity.Negative)]
    [InlineData("O", CausalLoopPolarity.Negative)]
    public void PolarityReadsBothTraditions(string token, CausalLoopPolarity expected)
    {
        // Act & assert.
        Assert.Equal(expected, CausalLoopParser.PolarityOf(token));
        Assert.Equal(expected, Assert.Single(Parse($"link a -> b {token}\r\n").Model.Links).Polarity);
    }

    /// <summary>
    /// The distinction the whole module turns on. A link nobody marked is not a positive link:
    /// the loop check counts negatives, and a fabricated count would produce a confident label
    /// for a diagram that states none (Requirement 3.6).
    /// </summary>
    [Fact]
    public void AnUnmarkedLink_IsUnstated_AndNeverPositive()
    {
        // Act.
        var link = Assert.Single(Parse("link a -> b\r\n").Model.Links);

        // Assert.
        Assert.Equal(CausalLoopPolarity.Unstated, link.Polarity);
        Assert.NotEqual(CausalLoopPolarity.Positive, link.Polarity);
    }

    [Fact]
    public void ALoop_CarriesItsIdentifierItsNameAndItsCycle()
    {
        // Act.
        var loop = Parse(Corpus).Model.Loops.Single(candidate => candidate.Identifier == "B1");

        // Assert.
        Assert.Equal("Deaths check growth", loop.Name);
        Assert.Equal(["population", "deaths"], loop.Variables);
        Assert.False(loop.ClaimsReinforcing);
    }

    [Fact]
    public void TheIdentifier_IsReadAsAClaim_AndAnUnknownOneClaimsNothing()
    {
        // Act & assert.
        // R and B are the notation's two; anything else is not silently taken for either.
        Assert.True(Assert.Single(Parse("loop R2 \"x\" a b\r\n").Model.Loops).ClaimsReinforcing);
        Assert.False(Assert.Single(Parse("loop B7 \"x\" a b\r\n").Model.Loops).ClaimsReinforcing);
        Assert.Null(Assert.Single(Parse("loop Q1 \"x\" a b\r\n").Model.Loops).ClaimsReinforcing);
    }

    [Fact]
    public void BlanksAndCommentsStateNothing_AndAnUnreadableLineIsReportedRatherThanDropped()
    {
        // Act.
        var result = Parse("causal-loop 1\r\n\r\n# a note\r\nnonsense here\r\nlink a -> b +\r\n");

        // Assert.
        // The readable statement still draws; the unreadable one is named rather than ignored.
        Assert.Single(result.Model.Links);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(3, problem.Lines.Start);
        Assert.Contains("nonsense", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedLink_NamesWhatALinkShouldLookLike()
    {
        // Act.
        var result = Parse("link a b\r\n");

        // Assert.
        Assert.Empty(result.Model.Links);
        Assert.Contains("link <from> -> <to>", Assert.Single(result.Problems).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The property that makes the format reviewable: a document read and written back is the
    /// same bytes, so a one-link edit will produce a one-line diff rather than a rewritten file.
    /// </summary>
    [Theory]
    [InlineData(Corpus)]
    [InlineData("causal-loop 1\nvariable a\nlink a -> a +\n")]
    [InlineData("link a -> b +")]
    [InlineData("")]
    public void ADocument_ComesBackByteForByte(string text)
    {
        // Act & assert.
        Assert.Equal(text, CausalLoopDocument.Parse(text).Text);
    }

    [Fact]
    public void EachStatement_RecordsTheLineItCameFrom_SoAWriterCanSplice()
    {
        // Act.
        var model = Parse(Corpus).Model;

        // Assert.
        // The corpus puts the first variable on line 3, zero-based: header, blank, comment.
        Assert.Equal(3, model.Variables[0].Lines.Start);
        Assert.Equal(1, model.Variables[0].Lines.Length);
        Assert.All(model.Links, link => Assert.Equal(1, link.Lines.Length));
    }

    [Fact]
    public void ALabelWithSpaces_SurvivesItsQuotes()
    {
        // Act.
        var variable = Assert.Single(Parse("variable gdp \"Gross domestic product\"\r\n").Model.Variables);

        // Assert.
        Assert.Equal("Gross domestic product", variable.Label);
    }

    [Fact]
    public void TheStarterDocumentTheFactoryWrites_ParsesCleanly()
    {
        // Arrange.
        // The forward reference task 1.1 left: the factory emits text before a parser exists to
        // read it, so this is where the two are pinned together.
        var text = new CausalLoopDocumentFactory(ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin)
            .CreateEmptyDocument("feedback");

        // Act.
        var result = Parse(text);

        // Assert.
        Assert.Empty(result.Problems);
        Assert.Equal(2, result.Model.Variables.Count);
        Assert.Equal(2, result.Model.Links.Count);
        Assert.All(result.Model.Links, link => Assert.Equal(CausalLoopPolarity.Positive, link.Polarity));
        Assert.Equal("R1", Assert.Single(result.Model.Loops).Identifier);
    }
}
