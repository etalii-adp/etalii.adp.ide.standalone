using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>The regular expression subset (FBL §2.5) that CEL's <c>matches</c> and FBL's patterns share.</summary>
public class RegexSubsetTests
{
    [Theory]
    [InlineData(@"(a)\1", "backreference")]
    [InlineData(@"(?<n>a)\k<n>", "named backreference")]
    [InlineData(@"\p{L}", "Unicode property")]
    [InlineData("(?<=a)b", "Lookbehind")]
    [InlineData("a(?=b)", "Lookahead")]
    [InlineData("(?>a)", "Atomic")]
    [InlineData("(?i)a", "Inline flags")]
    [InlineData("a++", "Possessive")]
    [InlineData("[a", "not closed")]
    public void AConstructOutsideTheSubsetIsRejectedByName(string expression, string named)
    {
        // Act.
        var problem = RegexSubset.Check(expression);

        // Assert.
        Assert.NotNull(problem);
        Assert.Contains(named, problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"^(?<name>[A-Za-z_]\w*)\s*->\s*""(?<label>[^""]*)""$")]
    [InlineData(@"^\d{4}-\d{2}-\d{2}$")]
    [InlineData("^(?:a|b)*?c$")]
    public void AnExpressionInTheSubsetIsAccepted(string expression)
    {
        // Act and assert.
        Assert.Null(RegexSubset.Check(expression));
    }

    [Fact]
    public void DigitsAndWordCharactersAreAsciiOnly()
    {
        // Arrange.
        var regex = new BoundedRegex(@"^\d\w$", false, TimeSpan.FromSeconds(1));

        // Act and assert: an Arabic-Indic digit and a letter with a diacritic are outside \d and \w, as in RE2.
        Assert.True(regex.IsMatch("1a"));
        Assert.False(regex.IsMatch("١a"));
        Assert.False(regex.IsMatch("1é"));
    }
}
