using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>The DISL function library (DISL §12.4) the two bundled definitions call, value by value.</summary>
public class DislCelLibraryTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "enums": { "Phase": { "values": { "peak": { "label": "Peak" }, "trough": {} } } },
          "types": { "Thing": { "attributes": { "name": { "type": "string" } } } } }
        """));

    private static object? Evaluate(string expression) =>
        Specification.Environment(DislContexts.Element).Compile(expression).Evaluate(new Dictionary<string, object?>());

    [Theory]
    [InlineData("yearMonth(2026, 9)", 2026L * 12 + 8)]
    [InlineData("yearMonth(0, 1)", 0L)]
    [InlineData("yearMonth(-1, 12)", -1L)]
    [InlineData("yearMonth(-3200, 1)", -3200L * 12)]
    [InlineData("yearMonth(1900, 1).year()", 1900L)]
    [InlineData("(-1).year()", -1L)]
    [InlineData("(-1).month()", 12L)]
    [InlineData("(-13).year()", -2L)]
    [InlineData("(-12).month()", 1L)]
    [InlineData("0.month()", 1L)]
    [InlineData("yearMonth(2026, 9).month()", 9L)]
    public void AMonthIndex_IsBuiltAndTakenApartInAstronomicalYears(string expression, long expected) =>
        Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("yearMonth(2026, 0)", 0)]
    [InlineData("yearMonth(2026, 13)", 13)]
    public void AMonthOutsideOneToTwelve_IsAnError(string expression, int month) =>
        Assert.Equal(new CelError($"yearMonth() takes a month from 1 to 12, not {month}."), Evaluate(expression));

    [Theory]
    [InlineData("formatYearMonth(yearMonth(2026, 9), 'uuuu-MM')", "2026-09")]
    [InlineData("formatYearMonth(-1, 'uuuu-MM')", "-0001-12")]
    [InlineData("formatYearMonth(yearMonth(-3200, 1), 'uuuu-MM')", "-3200-01")]
    [InlineData("formatYearMonth(yearMonth(123456, 1), 'uuuu-MM')", "123456-01")]
    [InlineData("formatYearMonth(yearMonth(7, 3), 'u-M')", "7-3")]
    [InlineData("formatYearMonth(yearMonth(2026, 9), 'MMM uuuu')", "Sep 2026")]
    [InlineData("formatYearMonth(yearMonth(2026, 9), 'MMMM')", "September")]
    [InlineData("formatYearMonth(yearMonth(2026, 9), 'MMMM \\'of\\' uuuu')", "September of 2026")]
    [InlineData("formatYearMonth(yearMonth(2026, 9), '\\'\\'MM')", "'09")]
    public void AMonthIndex_IsFormattedWithAnLdmlPattern(string expression, string expected) =>
        Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("formatYearMonth(0, 'yyyy')", 'y')]
    [InlineData("formatYearMonth(0, 'dd-MM')", 'd')]
    public void APatternLetterAMonthHasNot_IsAnError(string expression, char letter) =>
        Assert.Equal(
            new CelError($"formatYearMonth() formats a month index with the pattern letters u and M only, not '{letter}' (DISL §5.5)."),
            Evaluate(expression));

    [Theory]
    [InlineData("2026-09", 2026L * 12 + 8)]
    [InlineData("0000-01", 0L)]
    [InlineData("-0001-12", -1L)]
    [InlineData("-3200-01", -3200L * 12)]
    [InlineData("123456-01", 123456L * 12)]
    [InlineData("-123456-12", -123456L * 12 + 11)]
    public void TheJsonForm_IsParsed(string text, long expected) =>
        Assert.Equal(CelOptional.Of(expected), Evaluate($"parseYearMonth('{text}')"));

    [Theory]
    [InlineData("2026-00")]
    [InlineData("2026-13")]
    [InlineData("2026-9")]
    [InlineData("226-09")]
    [InlineData("1234567-01")]
    [InlineData("+2026-09")]
    [InlineData(" 2026-09")]
    [InlineData("2026-09 ")]
    [InlineData("2026/09")]
    [InlineData("")]
    public void TextNotInTheJsonForm_ParsesToNone(string text) =>
        Assert.Equal(CelOptional.None, Evaluate($"parseYearMonth('{text}')"));

    [Theory]
    [InlineData("clamp(5, 1, 3)", 3L)]
    [InlineData("clamp(0, 1, 3)", 1L)]
    [InlineData("clamp(2, 1, 3)", 2L)]
    [InlineData("min(3, 1, 2)", 1L)]
    [InlineData("max(3, 1, 2, 7)", 7L)]
    [InlineData("max(1, 0)", 1L)]
    public void ClampMinAndMax_KeepIntegers(string expression, long expected) =>
        Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("clamp(2.5, 1, 3)", 2.5)]
    [InlineData("clamp(-0.5, 0.0, 1.0)", 0.0)]
    [InlineData("min(max(1.25, 0.5), 1.0)", 1.0)]
    [InlineData("max(1, 2.5)", 2.5)]
    [InlineData("min(0.25, 1)", 0.25)]
    public void ClampMinAndMax_CompareNumbersOfEitherKind(string expression, double expected) =>
        Assert.Equal(expected, Evaluate(expression));

    [Fact]
    public void MinAndMax_GiveTheArgumentItself() => Assert.Equal(1L, Evaluate("max(1, 0.5)"));

    [Theory]
    [InlineData("enumLabel('Phase', 'peak')", "Peak")]
    [InlineData("enumLabel('Phase', 'trough')", "trough")]
    [InlineData("enumLabel('Phase', 'other')", "other")]
    [InlineData("lower('ÀBc')", "àbc")]
    public void Text_IsDerivedFromTheSpecification(string expression, string expected) =>
        Assert.Equal(expected, Evaluate(expression));

    [Fact]
    public void AnEnumerationTheSpecificationLacks_IsAnError() =>
        Assert.Equal(new CelError("'Colour' is not an enumeration of this specification."), Evaluate("enumLabel('Colour', 'red')"));
}
