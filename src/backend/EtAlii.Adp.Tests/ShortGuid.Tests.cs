using Xunit;

namespace EtAlii.Adp.Tests;

public class ShortGuidTests
{
    // Requirement 1: round-trip and fixed length.

    [Fact]
    public void ToString_WithEmptyGuid_ReturnsAllZeros()
    {
        var shortGuid = new ShortGuid(Guid.Empty);

        var result = shortGuid.ToString();

        Assert.Equal(new string('0', ShortGuid.Length), result);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    [InlineData("12345678-9abc-def0-1234-56789abcdef0")]
    public void RoundTrip_WithRepresentativeGuids_ReconstructsOriginalValue(string guidText)
    {
        var original = Guid.Parse(guidText);

        var shortGuid = new ShortGuid(original);
        var reconstructed = ShortGuid.Parse(shortGuid.ToString());

        Assert.Equal(original, reconstructed.Guid);
    }

    [Fact]
    public void RoundTrip_WithManyRandomGuids_AlwaysReconstructsOriginalValueAtFixedLength()
    {
        for (var i = 0; i < 1000; i++)
        {
            var original = Guid.NewGuid();

            var text = new ShortGuid(original).ToString();
            var reconstructed = ShortGuid.Parse(text);

            Assert.Equal(ShortGuid.Length, text.Length);
            Assert.Equal(original, reconstructed.Guid);
        }
    }

    // Requirement 2: Guid-like interface, equality, ordering.

    [Fact]
    public void ImplicitConversions_BetweenGuidAndShortGuid_RoundTrip()
    {
        Guid original = Guid.NewGuid();

        ShortGuid shortGuid = original;
        Guid back = shortGuid;

        Assert.Equal(original, back);
    }

    [Fact]
    public void NewShortGuid_CalledRepeatedly_ProducesDistinctNonEmptyValues()
    {
        var first = ShortGuid.NewShortGuid();
        var second = ShortGuid.NewShortGuid();

        Assert.NotEqual(ShortGuid.Empty, first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void FromName_CalledRepeatedlyWithTheSameName_ProducesTheSameValue()
    {
        var first = ShortGuid.FromName("developer");
        var second = ShortGuid.FromName("developer");

        Assert.Equal(first, second);
    }

    [Fact]
    public void FromName_WithDifferentNames_ProducesDifferentValues()
    {
        var first = ShortGuid.FromName("developer");
        var second = ShortGuid.FromName("someone-else");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_WithSameUnderlyingGuid_AreEqualWithMatchingHashCodes()
    {
        var value = Guid.NewGuid();
        var a = new ShortGuid(value);
        var b = new ShortGuid(value);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_WithDifferentUnderlyingGuid_AreNotEqual()
    {
        var a = new ShortGuid(Guid.NewGuid());
        var b = new ShortGuid(Guid.NewGuid());

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void CompareTo_ForAShuffledList_ProducesTheSameOrderAsOrdinalStringComparison()
    {
        var shortGuids = Enumerable.Range(0, 200).Select(_ => ShortGuid.NewShortGuid()).ToList();

        var byValue = shortGuids.OrderBy(g => g).Select(g => g.ToString()).ToList();
        var byString = shortGuids.Select(g => g.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList();

        Assert.Equal(byString, byValue);
    }

    // Requirement 3: strict parsing and validation.

    [Fact]
    public void Parse_WithMixedCaseInput_SucceedsAndNormalizesToLowercase()
    {
        var lower = new ShortGuid(Guid.NewGuid()).ToString();
        var mixedCase = string.Concat(lower.Select((c, i) => i % 2 == 0 ? char.ToUpperInvariant(c) : c));

        var result = ShortGuid.Parse(mixedCase);

        Assert.Equal(lower, result.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public void Parse_WithWrongLength_ThrowsFormatException(string text)
    {
        Assert.Throws<FormatException>(() => ShortGuid.Parse(text));
        Assert.False(ShortGuid.TryParse(text, provider: null, out var result));
        Assert.Equal(default, result);
    }

    [Fact]
    public void Parse_WithTooLongInput_ThrowsFormatException()
    {
        var text = new string('0', ShortGuid.Length + 1);

        Assert.Throws<FormatException>(() => ShortGuid.Parse(text));
        Assert.False(ShortGuid.TryParse(text, provider: null, out _));
    }

    [Fact]
    public void Parse_WithInvalidCharacter_ThrowsFormatException()
    {
        var text = new string('0', ShortGuid.Length - 1) + "!";

        Assert.Throws<FormatException>(() => ShortGuid.Parse(text));
        Assert.False(ShortGuid.TryParse(text, provider: null, out _));
    }

    [Fact]
    public void Parse_WithValueExceedingGuidRange_ThrowsFormatException()
    {
        // 25 'z' characters is 36^25 - 1, which exceeds UInt128.MaxValue (and therefore any Guid's range).
        var text = new string('z', ShortGuid.Length);

        Assert.Throws<FormatException>(() => ShortGuid.Parse(text));
        Assert.False(ShortGuid.TryParse(text, provider: null, out _));
    }

    [Fact]
    public void Parse_WithSpanOverload_MatchesStringOverload()
    {
        var text = new ShortGuid(Guid.NewGuid()).ToString();

        var fromSpan = ShortGuid.Parse(text.AsSpan());
        var fromString = ShortGuid.Parse(text);

        Assert.Equal(fromString, fromSpan);
    }

    // Requirement 4: allocation-conscious formatting and parsing.

    [Fact]
    public void TryFormat_WithExactlySizedBuffer_SucceedsAndWritesExpectedCharacters()
    {
        var shortGuid = new ShortGuid(Guid.NewGuid());
        Span<char> buffer = stackalloc char[ShortGuid.Length];

        var succeeded = shortGuid.TryFormat(buffer, out var charsWritten);

        Assert.True(succeeded);
        Assert.Equal(ShortGuid.Length, charsWritten);
        Assert.Equal(shortGuid.ToString(), new string(buffer));
    }

    [Fact]
    public void TryFormat_WithTooSmallBuffer_ReturnsFalseAndWritesNothing()
    {
        var shortGuid = new ShortGuid(Guid.NewGuid());
        Span<char> buffer = stackalloc char[10];

        var succeeded = shortGuid.TryFormat(buffer, out var charsWritten);

        Assert.False(succeeded);
        Assert.Equal(0, charsWritten);
    }

    [Fact]
    public void TryParse_WithSpanOverload_MatchesStringOverload()
    {
        var text = new ShortGuid(Guid.NewGuid()).ToString();

        var spanSucceeded = ShortGuid.TryParse(text.AsSpan(), provider: null, out var fromSpan);
        var stringSucceeded = ShortGuid.TryParse(text, provider: null, out var fromString);

        Assert.True(spanSucceeded);
        Assert.True(stringSucceeded);
        Assert.Equal(fromString, fromSpan);
    }
}
