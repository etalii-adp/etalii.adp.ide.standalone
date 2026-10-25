using System.Globalization;
using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// <c>formatNumber(n, pattern)</c> (DISL §12.4): a number written by a pattern of digit placeholders,
/// grouping and decimals, in the invariant culture.
/// </summary>
public class FormatNumberTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With());

    private static object? Evaluate(string expression) =>
        Specification.Environment(DislContext.Element).Compile(expression).Evaluate(new Dictionary<string, object?>());

    [Theory]
    [InlineData("formatNumber(1302, '#,##0.##')", "1,302")]
    [InlineData("formatNumber(1234567.891, '#,##0.##')", "1,234,567.89")]
    [InlineData("formatNumber(0.5, '#,##0.##')", "0.5")]
    [InlineData("formatNumber(3.3, '0.####')", "3.3")]
    [InlineData("formatNumber(2.0, '0.####')", "2")]
    [InlineData("formatNumber(-0.00001, '0.####')", "-0")]
    [InlineData("formatNumber(42, '0')", "42")]
    public void ANumber_IsWrittenByItsPattern(string expression, string expected) =>
        Assert.Equal(expected, Evaluate(expression));

    /// <summary>
    /// The same digits a host writes with .NET's custom numeric formats, at a half in the last kept
    /// decimal and at a value past a double's exact integers alike.
    /// </summary>
    [Theory]
    [InlineData(0.0001, "0.####")]
    [InlineData(0.00015, "0.####")]
    [InlineData(1.00005, "0.####")]
    [InlineData(2.5, "0.####")]
    [InlineData(12345678901234567890d, "0.####")]
    [InlineData(1.005, "#,##0.##")]
    [InlineData(12345678901234567890d, "#,##0.##")]
    public void TheDigits_AreThoseOfTheInvariantCulture(double value, string pattern) =>
        Assert.Equal(value.ToString(pattern, CultureInfo.InvariantCulture),
            Evaluate($"formatNumber({value.ToString("R", CultureInfo.InvariantCulture)}{(value % 1 == 0 && Math.Abs(value) < 1e15 ? ".0" : "")}, '{pattern}')"));

    /// <summary>
    /// Amounts as a property row shows them with <c>'0.####'</c>: at most four decimals, none when whole, no grouping.
    /// The values are those a host's rows hold over its corpus, small fractions and thousands alike.
    /// </summary>
    [Theory]
    [InlineData("formatNumber(0.01, '0.####')", "0.01")]
    [InlineData("formatNumber(0.16, '0.####')", "0.16")]
    [InlineData("formatNumber(1.0, '0.####')", "1")]
    [InlineData("formatNumber(1.1, '0.####')", "1.1")]
    [InlineData("formatNumber(12.5, '0.####')", "12.5")]
    [InlineData("formatNumber(170.0, '0.####')", "170")]
    [InlineData("formatNumber(5500.0, '0.####')", "5500")]
    public void AnAmount_IsWrittenWithoutGroupingOrTrailingZeros(string expression, string expected) =>
        Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("formatNumber('a', '0')", "formatNumber() takes a number and a pattern.")]
    [InlineData("formatNumber(1, '0.00 €')", "'0.00 €' is not a number pattern this runtime writes: digits '0' and '#', grouping ',' and one decimal point '.'.")]
    [InlineData("formatNumber(1, '')", "'' is not a number pattern this runtime writes: digits '0' and '#', grouping ',' and one decimal point '.'.")]
    public void ATextOrAnUnknownPattern_IsAnError(string expression, string message) =>
        Assert.Equal(new CelError(message), Evaluate(expression));
}
