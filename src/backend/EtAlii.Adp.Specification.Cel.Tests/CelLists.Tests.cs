using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>The lists library, after cel-spec's lists_ext tests, and DISL §12.1's string order and stable sorts.</summary>
public class CelListsTests
{
    [Theory]
    [InlineData("lists.range(4)", "[0,1,2,3]")]
    [InlineData("lists.range(0)", "[]")]
    [InlineData("[1, [2, [3]]].flatten()", "[1,2,[3]]")]
    [InlineData("[1, [2, [3]]].flatten(2)", "[1,2,3]")]
    [InlineData("[[], [[]], 1].flatten()", "[[],1]")]
    [InlineData("[].flatten()", "[]")]
    [InlineData("[1].flatten(-1)", "error: flatten() needs a depth of 0 or more.")]
    [InlineData("[1, 2, 2, 3].distinct()", "[1,2,3]")]
    [InlineData("['c', 'a', 'a', 'b', 'a', 'b', 'c', 'c'].distinct()", "[c,a,b]")]
    [InlineData("[1, 1.0, 2].distinct()", "[1,2]")]
    [InlineData("[[1], [1], [2]].distinct()", "[[1],[2]]")]
    [InlineData("[1, 2, 3, 4].slice(1, 3)", "[2,3]")]
    [InlineData("[1, 2, 3, 4].slice(0, 0)", "[]")]
    [InlineData("[1, 2, 3, 4].slice(3, 1)", "error: slice(3, 1) is out of range for a list of 4.")]
    [InlineData("[1, 2, 3, 4].slice(0, 5)", "error: slice(0, 5) is out of range for a list of 4.")]
    [InlineData("[4, 3, 2, 1].sort()", "[1,2,3,4]")]
    [InlineData("['d', 'a', 'b', 'c'].sort()", "[a,b,c,d]")]
    [InlineData("[2.5, 1, 2].sort()", "[1,2,2.5]")]
    [InlineData("[true, false].sort()", "[false,true]")]
    [InlineData("['a', 1].sort()", "error: sort() needs values of one type.")]
    [InlineData("[5, 1, 2, 3].reverse()", "[3,2,1,5]")]
    [InlineData("[{'name': 'foo', 'score': 0}, {'name': 'bar', 'score': -10}, {'name': 'baz', 'score': 1000}].sortBy(e, e.score).map(e, e.name)", "[bar,foo,baz]")]
    [InlineData("['tea', 'ab', 'coffee', 'cd'].sortBy(s, size(s))", "[ab,cd,tea,coffee]")]
    public void AListFunctionEvaluates(string expression, string expected)
    {
        // Act.
        var value = Evaluate.Expression(expression);

        // Assert.
        Assert.Equal(expected, Evaluate.Text(value));
    }

    [Fact]
    public void SortingIsStable()
    {
        // Arrange: many equal keys, whose original order must survive.
        var items = Enumerable.Range(0, 200).Select(i => (object?)new CelMap { ["key"] = (long)(i % 3), ["at"] = (long)i }).ToList();

        // Act.
        var sorted = (IReadOnlyList<object?>)Evaluate.Expression("items.sortBy(i, i.key)", new Dictionary<string, object?> { ["items"] = items })!;

        // Assert.
        var order = sorted.Cast<CelMap>().Select(m => ((long)m["key"]!, (long)m["at"]!)).ToList();
        Assert.Equal(order.OrderBy(o => o.Item1).ThenBy(o => o.Item2).ToList(), order);
    }

    [Theory]
    // U+FFFF is before U+1F600 by code point; in UTF-16 code units the emoji's high surrogate (U+D83D) is before U+FFFF.
    [InlineData("'￿' < '\U0001F600'", "true")]
    [InlineData("'\U0001F600' > ''", "true")]
    [InlineData("'a' < 'ab' && 'ab' < 'b'", "true")]
    [InlineData("['\U0001F600', '￿', 'a'].sort() == ['a', '￿', '\U0001F600']", "true")]
    [InlineData("['\U0001F600', '￿'].sortBy(s, s)[0] == '￿'", "true")]
    public void StringsOrderByCodePoint(string expression, string expected)
    {
        // Act.
        var value = Evaluate.Expression(expression);

        // Assert.
        Assert.Equal(expected, Evaluate.Text(value));
    }
}
