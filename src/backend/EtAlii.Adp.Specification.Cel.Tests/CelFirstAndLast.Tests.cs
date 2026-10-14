using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>
/// <c>l.first()</c> and <c>l.last()</c>: a list's first and last element as an optional, none for an
/// empty list (cel-spec's optional list functions; DISL §12.4).
/// </summary>
public class CelFirstAndLastTests
{
    [Theory]
    [InlineData("[1, 2, 3].first()", "optional.of(1)")]
    [InlineData("[1, 2, 3].last()", "optional.of(3)")]
    [InlineData("['only'].first() == ['only'].last()", "true")]
    [InlineData("[].first()", "optional.none()")]
    [InlineData("[].last()", "optional.none()")]
    [InlineData("[].last().orValue(0) + 1", "1")]
    [InlineData("[[1], [2, 3]].last().orValue([]).size()", "2")]
    [InlineData("[3, 1, 2].sort().last().value()", "3")]
    [InlineData("'text'.first()", "error: A list was expected.")]
    public void AListsEnd_IsAnOptional(string expression, string expected)
    {
        // Act.
        var value = Evaluate.Expression(expression);

        // Assert.
        Assert.Equal(expected, Evaluate.Text(value));
    }
}
