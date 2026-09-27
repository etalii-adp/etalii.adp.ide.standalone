using System.Globalization;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The time scale and the rows, stated once on the backend: month to x, row to y, and the month
/// dates the document writes (design, <i>The time scale</i>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The horizontal axis is uniform in months, not in seconds.</b> Every month is
/// <see cref="UnitsPerMonth"/> wide, from a fixed origin of <see cref="OriginDate"/>, so x is a pure
/// function of a date and the client's month snap is a fixed step. Dates before the origin are simply
/// negative. The client states the same scale in its ruler declaration; both are asserted against the
/// one checked-in <c>scale-fixture.json</c> beside this module, so neither can drift alone.
/// </para>
/// <para>
/// <b>A month is carried as one integer</b>, <c>year * 12 + (month - 1)</c>, which makes a span a
/// subtraction and a move an addition. It is written back as <c>YYYY-MM</c>.
/// </para>
/// </remarks>
public static partial class GhgScale
{
    /// <summary>Canvas units per month.</summary>
    public const int UnitsPerMonth = 4;

    /// <summary>The date at x = 0.</summary>
    public const string OriginDate = "1900-01";

    /// <summary>Every trend's height, in canvas units.</summary>
    public const double TrendHeight = 32;

    /// <summary>The distance between two rows: a trend's height plus a 24-unit gutter for influences.</summary>
    public const double RowStep = 56;

    /// <summary>The month index of <see cref="OriginDate"/>.</summary>
    public static int OriginMonth { get; } = 1900 * 12;

    /// <summary>The month index of <paramref name="year"/> and <paramref name="month"/> (1 to 12).</summary>
    public static int MonthIndex(int year, int month) => (year * 12) + (month - 1);

    /// <summary>A <c>YYYY-MM</c> date as a month index, or null when it is not one.</summary>
    /// <remarks>
    /// A year before 1 is written as ISO 8601 writes it: signed and astronomical, so <c>0000</c> is
    /// 1 BCE and <c>-3200</c> is 3201 BCE, with four to six digits after the sign.
    /// </remarks>
    public static int? ParseMonth(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var match = MonthExpression().Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return month is >= 1 and <= 12 ? MonthIndex(year, month) : null;
    }

    /// <summary>A month index as the document writes it: <c>YYYY-MM</c>, or <c>-YYYY-MM</c> before year 0.</summary>
    public static string FormatMonth(int monthIndex)
    {
        // Floored, not truncated: month index -1 is December of year -1, not month 0 of year 0.
        var year = (int)Math.Floor(monthIndex / 12.0);
        var month = monthIndex - (year * 12);
        var sign = year < 0 ? "-" : "";
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{Math.Abs(year):D4}-{month + 1:D2}");
    }

    /// <summary>The canvas x of the start of a month.</summary>
    public static double XOf(int monthIndex) => (monthIndex - OriginMonth) * (double)UnitsPerMonth;

    /// <summary>The month whose start is nearest <paramref name="x"/> - what a month snap lands on.</summary>
    public static int NearestMonthAt(double x) =>
        OriginMonth + (int)Math.Round(x / UnitsPerMonth, MidpointRounding.AwayFromZero);

    /// <summary>The month <paramref name="x"/> falls inside - what a drop at a mid-month x means.</summary>
    public static int MonthContaining(double x) => OriginMonth + (int)Math.Floor(x / UnitsPerMonth);

    /// <summary>The width of a span of months.</summary>
    public static double WidthOf(int months) => months * (double)UnitsPerMonth;

    /// <summary>The top edge of a row.</summary>
    public static double TopOf(int row) => row * RowStep;

    /// <summary>The row whose top edge is nearest <paramref name="top"/>.</summary>
    public static int RowAtTop(double top) => (int)Math.Round(top / RowStep, MidpointRounding.AwayFromZero);

    /// <summary>The row whose vertical middle is nearest <paramref name="y"/>.</summary>
    public static int RowAtMiddle(double y) => RowAtTop(y - (TrendHeight / 2));

    [GeneratedRegex(@"^(-\d{4,6}|\d{4})-(\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex MonthExpression();
}
