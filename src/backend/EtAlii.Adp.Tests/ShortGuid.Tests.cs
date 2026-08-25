using Xunit;

namespace EtAlii.Adp.Tests;

public class ShortGuidTests
{
    // Requirement 1: round-trip and fixed length.

    [Fact]
    public void ToString_WithEmptyGuid_ReturnsAllZeros()
    {
        // Arrange.
        var shortGuid = new ShortGuid(Guid.Empty);

        // Act.
        var result = shortGuid.ToString();

        // Assert.
        Assert.Equal(new string('0', ShortGuid.Length), result);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    [InlineData("12345678-9abc-def0-1234-56789abcdef0")]
    public void RoundTrip_WithRepresentativeGuids_ReconstructsOriginalValue(string guidText)
    {
        // Arrange.
        var original = Guid.Parse(guidText);

        // Act.
        var shortGuid = new ShortGuid(original);
        var reconstructed = ShortGuid.Parse(shortGuid.ToString());

        // Assert.
        Assert.Equal(original, reconstructed.Guid);
    }

    [Fact]
    public void RoundTrip_WithManyRandomGuids_AlwaysReconstructsOriginalValueAtFixedLength()
    {
        // Arrange.
        for (var i = 0; i < 1000; i++)
        {
            var original = Guid.NewGuid();

        // Act.
            var text = new ShortGuid(original).ToString();
            var reconstructed = ShortGuid.Parse(text);

        // Assert.
            Assert.Equal(ShortGuid.Length, text.Length);
            Assert.Equal(original, reconstructed.Guid);
        }
    }

    // Requirement 2: Guid-like interface, equality, ordering.

    [Fact]
    public void ImplicitConversions_BetweenGuidAndShortGuid_RoundTrip()
    {
        // Arrange.
        Guid original = Guid.NewGuid();

        // Act.
        ShortGuid shortGuid = original;
        Guid back = shortGuid;

        // Assert.
        Assert.Equal(original, back);
    }

    [Fact]
    public void NewShortGuid_CalledRepeatedly_ProducesDistinctNonEmptyValues()
    {
        // Arrange and act.
        var first = ShortGuid.NewShortGuid();
        var second = ShortGuid.NewShortGuid();

        // Assert.
        Assert.NotEqual(ShortGuid.Empty, first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void FromName_CalledRepeatedlyWithTheSameName_ProducesTheSameValue()
    {
        // Arrange and act.
        var first = ShortGuid.FromName("developer");
        var second = ShortGuid.FromName("developer");

        // Assert.
        Assert.Equal(first, second);
    }

    [Fact]
    public void FromName_WithDifferentNames_ProducesDifferentValues()
    {
        // Arrange and act.
        var first = ShortGuid.FromName("developer");
        var second = ShortGuid.FromName("someone-else");

        // Assert.
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_WithSameUnderlyingGuid_AreEqualWithMatchingHashCodes()
    {
        // Arrange and act.
        var value = Guid.NewGuid();
        var a = new ShortGuid(value);
        var b = new ShortGuid(value);

        // Assert.
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_WithDifferentUnderlyingGuid_AreNotEqual()
    {
        // Arrange and act.
        var a = new ShortGuid(Guid.NewGuid());
        var b = new ShortGuid(Guid.NewGuid());

        // Assert.
        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void CompareTo_ForAShuffledList_ProducesTheSameOrderAsOrdinalStringComparison()
    {
        // Arrange.
        var shortGuids = Enumerable.Range(0, 200).Select(_ => ShortGuid.NewShortGuid()).ToList();

        // Act.
        var byValue = shortGuids.OrderBy(g => g).Select(g => g.ToString()).ToList();
        var byString = shortGuids.Select(g => g.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList();

        // Assert.
        Assert.Equal(byString, byValue);
    }

    // Requirement 3: strict parsing and validation.

    [Fact]
    public void Parse_WithMixedCaseInput_SucceedsAndNormalizesToLowercase()
    {
        // Arrange.
        var lower = new ShortGuid(Guid.NewGuid()).ToString();
        var mixedCase = string.Concat(lower.Select((c, i) => i % 2 == 0 ? char.ToUpperInvariant(c) : c));

        // Act.
        var result = ShortGuid.Parse(mixedCase);

        // Assert.
        Assert.Equal(lower, result.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public void Parse_WithWrongLength_ThrowsFormatException(string text)
    {
        // Arrange, act and assert.
        Assert.Throws<FormatException>(() => ShortGuid.Parse(text));
        Assert.False(ShortGuid.TryParse(text, provider: null, out var result));
        Assert.Equal(default, result);
    }

    [Fact]
    public void Parse_WithTooLongInput_ThrowsFormatException()
    {
        // Act.
        var text = new string('0', ShortGuid.Length + 1);

        // Assert.
        Assert.Throws<FormatException>(() => ShortGuid.Parse(text));
        Assert.False(ShortGuid.TryParse(text, provider: null, out _));
    }

    [Fact]
    public void Parse_WithInvalidCharacter_ThrowsFormatException()
    {
        // Act.
        var text = new string('0', ShortGuid.Length - 1) + "!";

        // Assert.
        Assert.Throws<FormatException>(() => ShortGuid.Parse(text));
        Assert.False(ShortGuid.TryParse(text, provider: null, out _));
    }

    [Fact]
    public void Parse_WithValueExceedingGuidRange_ThrowsFormatException()
    {
        // Act.
        // 25 'z' characters is 36^25 - 1, which exceeds UInt128.MaxValue (and therefore any Guid's range).
        var text = new string('z', ShortGuid.Length);

        // Assert.
        Assert.Throws<FormatException>(() => ShortGuid.Parse(text));
        Assert.False(ShortGuid.TryParse(text, provider: null, out _));
    }

    [Fact]
    public void Parse_WithSpanOverload_MatchesStringOverload()
    {
        // Arrange.
        var text = new ShortGuid(Guid.NewGuid()).ToString();

        // Act.
        var fromSpan = ShortGuid.Parse(text.AsSpan());
        var fromString = ShortGuid.Parse(text);

        // Assert.
        Assert.Equal(fromString, fromSpan);
    }

    // Requirement 4: allocation-conscious formatting and parsing.

    [Fact]
    public void TryFormat_WithExactlySizedBuffer_SucceedsAndWritesExpectedCharacters()
    {
        // Arrange.
        var shortGuid = new ShortGuid(Guid.NewGuid());
        Span<char> buffer = stackalloc char[ShortGuid.Length];

        // Act.
        var succeeded = shortGuid.TryFormat(buffer, out var charsWritten);

        // Assert.
        Assert.True(succeeded);
        Assert.Equal(ShortGuid.Length, charsWritten);
        Assert.Equal(shortGuid.ToString(), new string(buffer));
    }

    [Fact]
    public void TryFormat_WithTooSmallBuffer_ReturnsFalseAndWritesNothing()
    {
        // Arrange.
        var shortGuid = new ShortGuid(Guid.NewGuid());
        Span<char> buffer = stackalloc char[10];

        // Act.
        var succeeded = shortGuid.TryFormat(buffer, out var charsWritten);

        // Assert.
        Assert.False(succeeded);
        Assert.Equal(0, charsWritten);
    }

    [Fact]
    public void TryParse_WithSpanOverload_MatchesStringOverload()
    {
        // Arrange.
        var text = new ShortGuid(Guid.NewGuid()).ToString();

        // Act.
        var spanSucceeded = ShortGuid.TryParse(text.AsSpan(), provider: null, out var fromSpan);
        var stringSucceeded = ShortGuid.TryParse(text, provider: null, out var fromString);

        // Assert.
        Assert.True(spanSucceeded);
        Assert.True(stringSucceeded);
        Assert.Equal(fromString, fromSpan);
    }
}
