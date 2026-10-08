using System.Globalization;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>How a value is written on the canvas: a number inside the document's own words.</summary>
/// <remarks>
/// <b>One placeholder, and nothing to learn.</b> A format is any text with <c>{value}</c> in it -
/// <c>€{value}M</c>, <c>{value} TWh</c>, <c>({value})</c> - so money, energy and people read in
/// their own units without the module knowing what any of them are. A format without the
/// placeholder is shown with the number after it, so a format of <c>TWh</c> still says how much.
/// </remarks>
public static class SankeyFormat
{
    /// <summary>The placeholder a format writes the number in.</summary>
    private const string Placeholder = "{value}";

    /// <summary>The number alone - what a document that states no format draws.</summary>
    public const string Plain = Placeholder;

    /// <summary>The value written through the format.</summary>
    public static string Write(string format, double value)
    {
        var number = Number(value);
        if (string.IsNullOrEmpty(format))
        {
            return number;
        }

        return format.Contains(Placeholder, StringComparison.Ordinal)
            ? format.Replace(Placeholder, number, StringComparison.Ordinal)
            : $"{number} {format}";
    }

    /// <summary>A number as a reader reads it: thousands grouped, at most two decimals, none when whole.</summary>
    private static string Number(double value) => value.ToString("#,0.##", CultureInfo.InvariantCulture);
}
