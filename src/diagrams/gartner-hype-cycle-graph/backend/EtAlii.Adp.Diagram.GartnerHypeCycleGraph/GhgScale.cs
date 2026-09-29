using System.Globalization;
using System.Text.RegularExpressions;
using EtAlii.Adp.Documents;

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
/// <para>
/// <b>A document may draw in a coarser step</b> - a year, a decade or a century, named by its
/// <see cref="GhgTimeUnit"/> - so that thousands of years fit on a canvas. Every step is
/// <see cref="UnitsPerStep"/> units wide and a snap lands on the start of a step. The methods that
/// take no unit are the month's, which is every document's unit unless it names another.
/// </para>
/// </remarks>
public static partial class GhgScale
{
    /// <summary>Canvas units per step of a diagram's time unit, whichever unit it is.</summary>
    public const int UnitsPerStep = 4;

    /// <summary>Canvas units per month, in a diagram drawn in months.</summary>
    public const int UnitsPerMonth = UnitsPerStep;

    /// <summary>The date at x = 0.</summary>
    public const string OriginDate = "1900-01";

    /// <summary>Every trend's height, in canvas units.</summary>
    public const double TrendHeight = 32;

    /// <summary>A trigger's diameter: half a trend's height (Requirement 2.1).</summary>
    public const double TriggerSize = TrendHeight / 2;

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
    public static double XOf(int monthIndex) => XOf(monthIndex, GhgTimeUnit.Month);

    /// <summary>The canvas x of the start of a month, in a diagram drawn in <paramref name="unit"/>.</summary>
    public static double XOf(int monthIndex, GhgTimeUnit unit) => (monthIndex - OriginMonth) * (double)UnitsPerStep / unit.Months;

    /// <summary>The month whose start is nearest <paramref name="x"/> - what a month snap lands on.</summary>
    public static int NearestMonthAt(double x) => NearestMonthAt(x, GhgTimeUnit.Month);

    /// <summary>
    /// The start of the step nearest <paramref name="x"/> in a diagram drawn in <paramref name="unit"/> -
    /// what a snap lands on, so a trend moved in a diagram of years starts in a January.
    /// </summary>
    public static int NearestMonthAt(double x, GhgTimeUnit unit) =>
        OriginMonth + ((int)Math.Round(x / UnitsPerStep, MidpointRounding.AwayFromZero) * unit.Months);

    /// <summary>The month <paramref name="x"/> falls inside - what a drop at a mid-month x means.</summary>
    public static int MonthContaining(double x) => MonthContaining(x, GhgTimeUnit.Month);

    /// <summary>The start of the step <paramref name="x"/> falls inside, in a diagram drawn in <paramref name="unit"/>.</summary>
    public static int MonthContaining(double x, GhgTimeUnit unit) =>
        OriginMonth + ((int)Math.Floor(x / UnitsPerStep) * unit.Months);

    /// <summary>The width of a span of months.</summary>
    public static double WidthOf(int months) => WidthOf(months, GhgTimeUnit.Month);

    /// <summary>The width of a span of months, in a diagram drawn in <paramref name="unit"/>.</summary>
    public static double WidthOf(int months, GhgTimeUnit unit) => months * (double)UnitsPerStep / unit.Months;

    /// <summary>The top edge of a row.</summary>
    public static double TopOf(int row) => row * RowStep;

    /// <summary>The row whose top edge is nearest <paramref name="top"/>.</summary>
    /// <remarks>
    /// The rule is <see cref="RowRounding.ToNearestRow"/>, shared with every row canvas and pinned by
    /// the golden fixture the client reads too (backend-centralization R9): a top exactly between two
    /// rows rounds away from zero, on both sides of the origin.
    /// </remarks>
    public static int RowAtTop(double top) => RowRounding.ToNearestRow(top, RowStep);

    /// <summary>The row whose vertical middle is nearest <paramref name="y"/>.</summary>
    public static int RowAtMiddle(double y) => RowAtTop(y - (TrendHeight / 2));

    /// <summary>
    /// A date as a trigger's label writes it, in the diagram's unit (Q2): <c>Dec 1947</c> in a
    /// diagram of months, and the year alone in one of years or coarser.
    /// </summary>
    public static string FormatWhen(int monthIndex, GhgTimeUnit unit) => FormatDate(monthIndex, unit, "MMM");

    /// <summary>The same date in full, for a tooltip: <c>December 1947</c>, or the year alone.</summary>
    public static string FormatWhenLong(int monthIndex, GhgTimeUnit unit) => FormatDate(monthIndex, unit, "MMMM");

    private static string FormatDate(int monthIndex, GhgTimeUnit unit, string monthFormat)
    {
        ArgumentNullException.ThrowIfNull(unit);

        var year = (int)Math.Floor(monthIndex / 12d);
        var month = monthIndex - (year * 12) + 1;
        var yearText = year.ToString(CultureInfo.InvariantCulture);
        if (unit.Months > 1)
        {
            return yearText;
        }

        // A month name from any year: only the name is used, and year 2000 is valid for every month.
        var name = new DateTime(2000, month, 1).ToString(monthFormat, CultureInfo.InvariantCulture);
        return $"{name} {yearText}";
    }

    [GeneratedRegex(@"^(-\d{4,6}|\d{4})-(\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex MonthExpression();
}
