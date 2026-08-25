using Xunit;

namespace EtAlii.Adp.C4.Tests;

/// <summary>
/// Editing one argument of one line and leaving every other character exactly as it was, which
/// is what makes a rename a one-line diff (c4-diagrams Requirement 3.2).
/// </summary>
public class C4TokensTests
{
    [Fact]
    public void SplitWithSpans_KeepsAQuotedArgumentWhole_HoweverMuchWhitespaceItHas()
    {
        var tokens = C4Tokens.SplitWithSpans("        s = softwareSystem \"Internet Banking System\" \"Lets customers view accounts.\"");

        Assert.Equal(["s", "=", "softwareSystem", "Internet Banking System", "Lets customers view accounts."], tokens.Select(t => t.Value));
        Assert.True(tokens[3].Quoted);
        Assert.False(tokens[2].Quoted);
    }

    [Fact]
    public void SplitWithSpans_ReportsPositionsThatIndexBackIntoTheLine()
    {
        const string line = "    web = container \"Web App\" \"Serves.\" \"React\"";

        var tokens = C4Tokens.SplitWithSpans(line);

        var name = tokens[3];
        Assert.Equal("\"Web App\"", line.Substring(name.Start, name.Length));
    }

    [Fact]
    public void SplitWithSpans_StopsAtAComment()
    {
        var tokens = C4Tokens.SplitWithSpans("    u = person \"User\" // the person who uses it");

        Assert.Equal(["u", "=", "person", "User"], tokens.Select(t => t.Value));
    }

    [Theory]
    // keyword at index 2 (`identifier = keyword`), argument 0 is the name.
    [InlineData("    web = container \"Web App\" \"Serves.\" \"React\"", 2, 0, "Renamed", "    web = container \"Renamed\" \"Serves.\" \"React\"")]
    [InlineData("    web = container \"Web App\" \"Serves.\" \"React\"", 2, 1, "New text.", "    web = container \"Web App\" \"New text.\" \"React\"")]
    [InlineData("    web = container \"Web App\" \"Serves.\" \"React\"", 2, 2, "Kotlin", "    web = container \"Web App\" \"Serves.\" \"Kotlin\"")]
    public void ReplaceArgument_ChangesOnlyThatArgument(string line, int keyword, int argument, string value, string expected)
    {
        Assert.Equal(expected, C4Tokens.ReplaceArgument(line, keyword, argument, value));
    }

    [Fact]
    public void ReplaceArgument_KeepsTheTrailingBrace()
    {
        const string line = "    s = softwareSystem \"Old\" \"desc\" {";

        var result = C4Tokens.ReplaceArgument(line, 2, 0, "New");

        Assert.Equal("    s = softwareSystem \"New\" \"desc\" {", result);
    }

    [Fact]
    public void ReplaceArgument_KeepsATrailingComment()
    {
        const string line = "    u = person \"User\" \"desc\" // who uses it";

        var result = C4Tokens.ReplaceArgument(line, 2, 0, "Customer");

        Assert.Equal("    u = person \"Customer\" \"desc\" // who uses it", result);
    }

    [Fact]
    public void ReplaceArgument_AppendsAnArgumentTheLineDoesNotHaveYet()
    {
        const string line = "    web = container \"Web App\" \"Serves.\"";

        var result = C4Tokens.ReplaceArgument(line, 2, 2, "React");

        Assert.Equal("    web = container \"Web App\" \"Serves.\" \"React\"", result);
    }

    [Fact]
    public void ReplaceArgument_PadsTheGap_SoAppendedArgumentsKeepTheirMeaning()
    {
        // Setting a technology on a container that has only a name must not slide the
        // technology into the description's position.
        const string line = "    web = container \"Web App\"";

        var result = C4Tokens.ReplaceArgument(line, 2, 2, "React");

        Assert.Equal("    web = container \"Web App\" \"\" \"React\"", result);
    }

    [Fact]
    public void ReplaceArgument_AppendingBeforeABrace_KeepsTheBraceLast()
    {
        const string line = "    s = softwareSystem \"System\" {";

        var result = C4Tokens.ReplaceArgument(line, 2, 1, "A description.");

        Assert.Equal("    s = softwareSystem \"System\" \"A description.\" {", result);
    }

    [Fact]
    public void ReplaceArgument_EscapesAQuoteInTheValue()
    {
        const string line = "    s = softwareSystem \"System\"";

        var result = C4Tokens.ReplaceArgument(line, 2, 0, "the \"good\" one");

        Assert.Equal("    s = softwareSystem \"the \\\"good\\\" one\"", result);
    }

    [Fact]
    public void ReplaceArgument_OnALineWithNoIdentifier_CountsFromTheKeyword()
    {
        // `person "User"` with no `id =` prefix: the keyword is token 0.
        const string line = "    person \"User\" \"desc\"";

        var result = C4Tokens.ReplaceArgument(line, 0, 0, "Customer");

        Assert.Equal("    person \"Customer\" \"desc\"", result);
    }
}
