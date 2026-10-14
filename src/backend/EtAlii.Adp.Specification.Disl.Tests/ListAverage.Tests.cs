using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary><c>l.avg()</c> (DISL §12.4): the mean of a list of numbers, as a double.</summary>
public class ListAverageTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With());

    private static object? Evaluate(string expression) =>
        Specification.Environment(DislContexts.Element).Compile(expression).Evaluate(new Dictionary<string, object?>());

    [Theory]
    [InlineData("[1, 2, 3, 4].avg()", 2.5)]
    [InlineData("[2.5].avg()", 2.5)]
    [InlineData("[1, 2.0].avg()", 1.5)]
    [InlineData("[-3, 3].avg()", 0.0)]
    [InlineData("[10, 20].map(n, n * 2).avg()", 30.0)]
    public void TheMean_IsADouble(string expression, double expected) =>
        Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("[].avg()", "avg() of an empty list has no value.")]
    [InlineData("['a'].avg()", "avg() takes a list of numbers.")]
    public void TheMeanOfNothingOrOfText_IsAnError(string expression, string message) =>
        Assert.Equal(new CelError(message), Evaluate(expression));
}
