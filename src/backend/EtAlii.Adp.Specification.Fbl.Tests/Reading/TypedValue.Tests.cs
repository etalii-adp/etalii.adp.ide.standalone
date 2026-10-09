using EtAlii.Adp.Specification.Fbl.Rules;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Reading;

/// <summary>
/// Which values convert to which of a specification's scalar types (FBL §7.4). A value arrives as
/// its family read it: typed from YAML and JSON, always text from XML.
/// </summary>
public class TypedValueTests
{
    [Theory]
    [InlineData("number", "931298")]
    [InlineData("number", "-1.5e3")]
    [InlineData("bool", "true")]
    [InlineData("date", "2026-10-09")]
    [InlineData("datetime", "2026-10-09T08:30:00+02:00")]
    [InlineData("time", "08:30")]
    [InlineData("time", "08:30:15")]
    [InlineData("text", "anything")]
    [InlineData("Property", "p1")]
    public void TextThatConverts_Reads(string type, string value) => Assert.True(TypedValue.Reads(type, value));

    [Theory]
    [InlineData("number", "about half a million")]
    [InlineData("number", "NaN")]
    [InlineData("number", "")]
    [InlineData("bool", "yes")]
    [InlineData("date", "9 October")]
    [InlineData("date", "2026-13-01")]
    [InlineData("datetime", "tomorrow")]
    [InlineData("time", "half past eight")]
    public void TextThatDoesNotConvert_DoesNotRead(string type, string value) => Assert.False(TypedValue.Reads(type, value));

    [Fact]
    public void TypedValues_ReadAsTheirOwnTypeOnly()
    {
        // Assert.
        Assert.True(TypedValue.Reads("number", 931298L));
        Assert.True(TypedValue.Reads("number", 0.5));
        Assert.True(TypedValue.Reads("bool", false));
        Assert.False(TypedValue.Reads("number", true));
        Assert.False(TypedValue.Reads("bool", 1L));
        Assert.False(TypedValue.Reads("date", 20261009L));
    }
}
