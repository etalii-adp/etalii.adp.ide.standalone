using System.Globalization;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// What the line under a column shows of the rows in sight: a count, a share, the ends of the dates
/// or a figure over the numbers, as the view's column names it in <c>calculate</c>.
/// </summary>
/// <remarks>
/// A calculation that does not suit its property's type - left from a type the property had before,
/// or written by hand - shows nothing and is kept as written.
/// </remarks>
internal static class KnowledgeCalculations
{
    private static readonly string[] _counts = ["countAll", "countValues", "countUnique", "countEmpty", "countNotEmpty", "percentEmpty", "percentNotEmpty"];
    private static readonly string[] _dates = ["earliest", "latest", "dateRange"];
    private static readonly string[] _numbers = ["sum", "average", "median", "min", "max", "range"];

    /// <summary>The calculations a property of a type can have, in the order they are offered.</summary>
    public static IReadOnlyList<string> For(KnowledgeValueType type) => type.Figures switch
    {
        KnowledgeFigures.Numbers => [.. _counts, .. _numbers],
        KnowledgeFigures.Dates => [.. _counts, .. _dates],
        _ => _counts,
    };

    /// <summary>Whether the name is a calculation at all, of whatever type.</summary>
    public static bool IsKnown(string calculation) => _counts.Contains(calculation) || _dates.Contains(calculation) || _numbers.Contains(calculation);

    /// <summary>What the calculation comes to over the rows, as the line shows it; empty where it does not suit the property.</summary>
    public static string Of(KnowledgeProperty property, string calculation, IReadOnlyList<KnowledgeRow> rows)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(rows);

        var type = property.Type;
        if (!For(type).Contains(calculation))
        {
            return "";
        }

        // A box that is not ticked holds nothing, as a cell without a value does.
        List<IReadOnlyList<string>> values = [.. rows.Select(row => (IReadOnlyList<string>)[.. KnowledgeViewEngine.ValuesOf(row, property).Where(type.IsValue)])];
        var filled = values.Count(held => held.Count > 0);
        switch (calculation)
        {
            case "countAll":
                return Whole(rows.Count);
            case "countValues":
                return Whole(values.Sum(held => held.Count));
            case "countUnique":
                return Whole(values.SelectMany(held => held).Distinct(StringComparer.Ordinal).Count());
            case "countEmpty":
                return Whole(rows.Count - filled);
            case "countNotEmpty":
                return Whole(filled);
            case "percentEmpty":
                return Share(rows.Count - filled, rows.Count);
            case "percentNotEmpty":
                return Share(filled, rows.Count);
        }

        if (type.Figures == KnowledgeFigures.Numbers)
        {
            List<double> numbers = [.. values.SelectMany(held => held).Select(Number).OfType<double>().Order()];
            if (numbers.Count == 0)
            {
                return "";
            }

            return Figure(calculation switch
            {
                "sum" => numbers.Sum(),
                "average" => numbers.Average(),
                "median" => numbers.Count % 2 == 1 ? numbers[numbers.Count / 2] : (numbers[(numbers.Count / 2) - 1] + numbers[numbers.Count / 2]) / 2,
                "min" => numbers[0],
                "max" => numbers[^1],
                _ => numbers[^1] - numbers[0],
            });
        }

        // The dates as written, with the point each is: the ends are shown as their author wrote them.
        List<(long At, string Written)> moments =
        [
            .. values.SelectMany(held => held)
                .Select(value => (At: type.Moment(value), Written: value))
                .Where(moment => moment.At is not null)
                .Select(moment => (moment.At!.Value, moment.Written))
                .OrderBy(moment => moment.Value),
        ];
        if (moments.Count == 0)
        {
            return "";
        }

        return calculation switch
        {
            "earliest" => moments[0].Written,
            "latest" => moments[^1].Written,
            _ => Span(type.Between(moments[0].At, moments[^1].At)),
        };
    }

    private static double? Number(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : null;

    private static string Whole(int count) => count.ToString(CultureInfo.InvariantCulture);

    /// <summary>A figure without the noise of its arithmetic: ten decimals at most, and none it does not need.</summary>
    private static string Figure(double value) => Math.Round(value, 10).ToString("0.##########", CultureInfo.InvariantCulture);

    private static string Share(int part, int all) => all == 0 ? "" : Figure(Math.Round(100.0 * part / all, 1)) + "%";

    /// <summary>How long it is from the earliest to the latest, in the largest unit that says it whole enough: days, else hours, else minutes.</summary>
    private static string Span(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return Unit(Math.Round(span.TotalDays, 1), "day");
        }

        return span.TotalHours >= 1 ? Unit(Math.Round(span.TotalHours, 1), "hour") : Unit(Math.Round(span.TotalMinutes), "minute");
    }

    private static string Unit(double amount, string unit) => $"{Figure(amount)} {unit}{(Math.Abs(amount - 1) < double.Epsilon ? "" : "s")}";
}
