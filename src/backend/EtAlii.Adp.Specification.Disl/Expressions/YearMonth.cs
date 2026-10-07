using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The <c>yearMonth</c> primitive (DISL §4.2): a month without a day in astronomical year numbering,
/// which CEL sees as the month index <c>year × 12 + (month − 1)</c> and JSON writes as <c>±YYYY-MM</c>
/// with four to six year digits.
/// </summary>
internal static partial class YearMonth
{
    private static readonly string[] ShortNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static readonly string[] LongNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    /// <summary><c>yearMonth(y, m)</c>: the month index of month <paramref name="month"/> (1 to 12) of <paramref name="year"/>.</summary>
    public static long Of(long year, long month) =>
        month is >= 1 and <= 12 ? (year * 12) + (month - 1) : throw new CelException($"yearMonth() takes a month from 1 to 12, not {month}.");

    /// <summary><c>ym.year()</c>: floored, so month index −1 is December of year −1.</summary>
    public static long YearOf(long index) => Math.DivRem(index, 12) is var (quotient, remainder) && remainder < 0 ? quotient - 1 : quotient;

    /// <summary><c>ym.month()</c>, from 1 to 12.</summary>
    public static long MonthOf(long index) => index - (YearOf(index) * 12) + 1;

    /// <summary>The month index of the JSON form, or null for text not in it or with a month outside 1 to 12.</summary>
    public static long? Parse(string text)
    {
        var match = JsonForm().Match(text);
        if (!match.Success) return null;
        var year = long.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        var month = long.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
        return month is >= 1 and <= 12 ? (year * 12) + (month - 1) : null;
    }

    /// <summary>
    /// <c>formatYearMonth(i, pattern)</c>: LDML, where <c>u</c> is the signed astronomical year padded
    /// to the run's length, <c>M</c> and <c>MM</c> the month's number, <c>MMM</c> and <c>MMMM</c> its
    /// English name, and quoted text literal (§5.5). Every other letter is refused, <c>y</c> included,
    /// whose era is ambiguous before year 1.
    /// </summary>
    public static string Format(long index, string pattern)
    {
        var year = YearOf(index);
        var month = MonthOf(index);
        var text = new StringBuilder();
        for (var i = 0; i < pattern.Length;)
        {
            var c = pattern[i];
            if (c == '\'')
            {
                var close = pattern.IndexOf('\'', i + 1);
                if (close == i + 1)
                {
                    text.Append('\'');
                    i += 2;
                    continue;
                }
                if (close < 0) throw new CelException("formatYearMonth() was given a pattern with an unclosed quote.");
                text.Append(pattern, i + 1, close - i - 1);
                i = close + 1;
                continue;
            }
            if (!char.IsAsciiLetter(c))
            {
                text.Append(c);
                i++;
                continue;
            }

            var run = 1;
            while (i + run < pattern.Length && pattern[i + run] == c) run++;
            text.Append(c switch
            {
                'u' => (year < 0 ? "-" : "") + Math.Abs(year).ToString(CultureInfo.InvariantCulture).PadLeft(run, '0'),
                'M' when run <= 2 => month.ToString(CultureInfo.InvariantCulture).PadLeft(run, '0'),
                'M' when run == 3 => ShortNames[month - 1],
                'M' when run == 4 => LongNames[month - 1],
                _ => throw new CelException($"formatYearMonth() formats a month index with the pattern letters u and M only, not '{c}' (DISL §5.5)."),
            });
            i += run;
        }
        return text.ToString();
    }

    [GeneratedRegex("^(?<year>-?[0-9]{4,6})-(?<month>[0-9]{2})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JsonForm();
}
