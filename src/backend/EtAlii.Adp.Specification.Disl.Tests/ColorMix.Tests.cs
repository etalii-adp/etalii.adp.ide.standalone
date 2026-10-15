using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// <c>color(c).mix(c2, f)</c> (DISL §12.4): CSS Color 5 <c>color-mix()</c> in sRGB with premultiplied
/// alpha, written as <c>#rrggbbaa</c>, which a definition uses to mix a paint from two theme tokens.
/// </summary>
public class ColorMixTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With());

    private static object? Evaluate(string expression) =>
        Specification.Environment(DislContexts.Element).Compile(expression).Evaluate(new Dictionary<string, object?>());

    [Theory]
    [InlineData("color('#ff0000').mix('#0000ff', 0.25)", "#bf0040ff")]
    [InlineData("color('#ff0000').mix('#0000ff', 0)", "#ff0000ff")]
    [InlineData("color('#ff0000').mix('#0000ff', 1)", "#0000ffff")]
    [InlineData("color('#fff').mix('#000000', 0.5)", "#808080ff")]
    public void AMix_IsInterpolatedOnSrgb(string expression, string expected)
    {
        // Act.
        var value = Evaluate(expression);

        // Assert.
        Assert.Equal(expected, value);
    }

    [Fact]
    public void AMix_PremultipliesAlpha()
    {
        // Act: an opaque red mixed half with transparent blue keeps red's channels and halves the alpha.
        var value = Evaluate("color('#ff0000ff').mix('#0000ff00', 0.5)");

        // Assert.
        Assert.Equal("#ff000080", value);
    }

    [Theory]
    [InlineData("color('red').mix('#000', 0.5)", "color() takes a hex colour, not 'red'.")]
    [InlineData("color('#ff0000').mix('#000', 1.5)", "mix() takes a fraction from 0 to 1, not 1.5.")]
    public void WhatIsNotAColourOrAFraction_IsAnError(string expression, string message)
    {
        // Act.
        var value = Evaluate(expression);

        // Assert.
        Assert.Equal(new CelError(message), value);
    }

    [Fact]
    public void AMixOfThemeTokens_CompilesInTheContextsThatDrawIt()
    {
        // Act.
        var program = Specification.Environment(DislContexts.Shape).Compile("color(token('color.play')).mix(token('color.surface.raised'), 0.82)");

        // Assert.
        Assert.NotNull(program);
    }
}
