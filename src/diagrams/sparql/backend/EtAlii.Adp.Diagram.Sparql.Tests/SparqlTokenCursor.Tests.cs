using Xunit;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The token skim the parser's second tier rests on, asked directly: how brackets nest, which
/// keyword sits at depth zero, and what a range leaves open. The corpus never puts a keyword or a
/// variable where only the nesting tells them apart, so these are asked of the cursor itself.
/// </summary>
public class SparqlTokenCursorTests
{
    /// <summary>The index of the first token written <paramref name="value"/>.</summary>
    private static int IndexOf(SparqlTokenCursor cursor, string value)
    {
        for (var i = 0; cursor[i].Kind != SparqlTokenKind.EndOfFile; i++)
        {
            if (cursor[i].Value == value)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"'{value}' is not in the text.");
    }

    [Fact]
    public void FindTopLevelKeyword_PassesOverTheKeywordInsideABracket()
    {
        // Arrange.
        var cursor = new SparqlTokenCursor("BIND(IF(EXISTS { { SELECT (1 AS ?q) {} } }, 1, 0) AS ?r)");
        var open = IndexOf(cursor, "(");
        var close = cursor.FindBalancedEnd(open);

        // Act.
        var asIndex = cursor.FindTopLevelKeyword(open + 1, close, "AS");

        // Assert.
        Assert.Equal("?r", cursor[asIndex + 1].Value);
    }

    [Fact]
    public void FindBalancedEnd_CountsRoundCurlyAndSquareBracketsAsOneNesting()
    {
        // Arrange.
        var cursor = new SparqlTokenCursor("( a ( b [ c ] { d } ) e ) f");

        // Act.
        var close = cursor.FindBalancedEnd(0);

        // Assert.
        Assert.Equal(")", cursor[close].Value);
        Assert.Equal("f", cursor[close + 1].Value);
    }

    [Fact]
    public void FindBalancedEnd_OfABracketNeverClosed_NamesTheLineItOpensOn()
    {
        // Arrange.
        var cursor = new SparqlTokenCursor("a\n( b ( c )\n");

        // Act.
        var exception = Assert.Throws<SparqlParseException>(() => cursor.FindBalancedEnd(IndexOf(cursor, "(")));

        // Assert.
        Assert.Equal(2, exception.Line);
        Assert.Contains("never closed", exception.Message);
    }

    [Fact]
    public void DepthBetween_IsWhatTheRangeLeavesOpen()
    {
        // Arrange.
        var cursor = new SparqlTokenCursor("SELECT (COUNT(?y) AS ?n) ?x WHERE");
        var start = IndexOf(cursor, "(");

        // Act.
        var atY = cursor.DepthBetween(start, IndexOf(cursor, "?y"));
        var atX = cursor.DepthBetween(start, IndexOf(cursor, "?x"));

        // Assert.
        Assert.Equal(2, atY);
        Assert.Equal(0, atX);
    }

    [Fact]
    public void ASubqueryProjection_AfterAnAliasedExpression_ProjectsThePlainNameAndNotTheInnerOne()
    {
        // Arrange & act.
        var model = SparqlParser.Parse("SELECT * WHERE { { SELECT (COUNT(?y) AS ?n) ?x WHERE { ?x ?p ?y } } }");

        // Assert.
        var subSelect = Assert.Single(model.Where.SubSelects);
        Assert.Equal(["n", "x"], subSelect.ProjectedNames);
    }
}
