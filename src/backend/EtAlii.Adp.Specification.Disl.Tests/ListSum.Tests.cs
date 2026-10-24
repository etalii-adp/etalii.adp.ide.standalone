using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary><c>l.sum()</c> (DISL §12.4): the total of a list of numbers, an int while every item is one.</summary>
public class ListSumTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With());

    private static object? Evaluate(string expression) =>
        Specification.Environment(DislContext.Element).Compile(expression).Evaluate(new Dictionary<string, object?>());

    [Theory]
    [InlineData("[1, 2, 3, 4].sum()", 10L)]
    [InlineData("[-3, 3].sum()", 0L)]
    [InlineData("[].sum()", 0L)]
    [InlineData("[10, 20].map(n, n * 2).sum()", 60L)]
    public void TheTotalOfInts_IsAnInt(string expression, long expected) =>
        Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("[1, 2.5].sum()", 3.5)]
    [InlineData("[0.5, 0.25].sum()", 0.75)]
    [InlineData("[2.0].sum()", 2.0)]
    public void TheTotalWithADouble_IsADouble(string expression, double expected) =>
        Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("['a'].sum()", "sum() takes a list of numbers.")]
    [InlineData("[9223372036854775807, 1].sum()", "sum() of these ints overflows.")]
    public void TheTotalOfTextOrPastTheLargestInt_IsAnError(string expression, string message) =>
        Assert.Equal(new CelError(message), Evaluate(expression));
}
