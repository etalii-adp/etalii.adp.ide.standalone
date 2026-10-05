using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>The CEL subset (FBL §2.4, DISL §12): what an expression evaluates to.</summary>
public class CelEvaluationTests
{
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
        var program = CelCompiler.Compile(expression, ["entry"]);

        // Act.
        var value = program.Evaluate(new Dictionary<string, object?> { ["entry"] = entry });

        // Assert.
        Assert.Equal(expected, value is List<object?> list ? string.Join(",", list) : value);
    }
}
