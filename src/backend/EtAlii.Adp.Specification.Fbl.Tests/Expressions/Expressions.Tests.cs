using EtAlii.Adp.Specification.Fbl.Expressions;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Expressions;

/// <summary>The regular expression subset (FBL §2.5) and the CEL subset (FBL §2.4).</summary>
public class ExpressionsTests
{
    [Theory]
    [InlineData(@"(a)\1", "backreference")]
    [InlineData(@"(?<n>a)\k<n>", "named backreference")]
    [InlineData(@"\p{L}", "Unicode property")]
    [InlineData(@"(?<=a)b", "Lookbehind")]
    [InlineData(@"a(?=b)", "Lookahead")]
    [InlineData(@"(?>a)", "Atomic")]
    [InlineData(@"(?i)a", "Inline flags")]
    [InlineData(@"a++", "Possessive")]
    [InlineData(@"[a", "not closed")]
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
    [InlineData(@"^(?:a|b)*?c$")]
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

    [Theory]
    [InlineData("has(entry.end)", true)]
    [InlineData("!has(entry.missing)", true)]
    [InlineData("entry.kind == 'task' && size(entry.tags) == 2", true)]
    [InlineData("entry.tags.exists(t, t.startsWith('b'))", true)]
    [InlineData("entry.tags.all(t, t.matches('^[a-z]+$'))", true)]
    [InlineData("entry.count > 2 ? 'many' : 'few'", "many")]
    [InlineData("entry.tags.map(t, t.upperAscii())", "A,B")]
    [InlineData("'b' in entry.tags", true)]
    [InlineData("int(entry.count) + 1", 4L)]
    public void ACelExpressionEvaluatesOnAnEntry(string expression, object expected)
    {
        // Arrange.
        var entry = new CelMap { ["end"] = "2026-10-01", ["kind"] = "task", ["count"] = 3L, ["tags"] = new List<object?> { "a", "b" } };
        var program = CelCompiler.Compile(expression, CelContext.Tree);

        // Act.
        var value = program.Evaluate(new Dictionary<string, object?> { ["entry"] = entry });

        // Assert.
        Assert.Equal(expected, value is List<object?> list ? string.Join(",", list) : value);
    }

    [Theory]
    [InlineData("groups.name == 'x'", CelContext.Tree, "'groups' is not a variable here")]
    [InlineData("entry.x.y.z()", CelContext.Tree, "")]
    [InlineData("timestamp('2026-01-01')", CelContext.Tree, "")]
    public void AnExpressionOutsideTheSubsetFailsToCompile(string expression, CelContext context, string message)
    {
        // Act.
        var refused = Record.Exception(() => CelCompiler.Compile(expression, context));

        // Assert.
        Assert.IsType<CelException>(refused);
        Assert.Contains(message, refused.Message, StringComparison.Ordinal);
    }
}
