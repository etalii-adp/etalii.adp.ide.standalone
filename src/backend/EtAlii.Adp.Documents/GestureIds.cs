using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace EtAlii.Adp.Documents;

/// <summary>
/// The ids a canvas gesture sends in place of an element that does not exist yet: a new placement
/// (<c>new:</c>) or a proposed relation (<c>rel:</c>). Built and parsed one way, on both tiers
/// (backend-centralization R11).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two placement shapes, because two kinds of canvas place things differently.</b> A free canvas
/// sends the point in canvas coordinates, <c>new:{x},{y}</c> - causal-loop, databricks and rdf.
/// A row canvas sends the horizontal position and a whole row, <c>new:{x},{row}</c> - timeline and
/// dependency-graph, whose clients round the pointer to the nearest row before building the id. The
/// two share a prefix and a separator, so which one an id means is decided by the module that
/// parses it: <c>new:12,2.5</c> is a valid point and an invalid row placement.
/// </para>
/// <para>
/// <b>A relation id is split at its FIRST arrow</b>, as all five copies before this one split it,
/// and both ends must be non-empty (R11.2, landed on develop at <c>d2fb4e7e</c> for causal-loop, the
/// last copy to accept an empty end). A relation may start at a placement that does not exist yet -
/// dependency-graph proposes <c>rel:new:120,3-&gt;b</c> - so an end may contain <c>:</c> and
/// <c>,</c>. A SOURCE id containing <c>-&gt;</c> cannot round-trip, because the first arrow is taken
/// as the separator; the golden fixture pins that rather than leaving it to be rediscovered.
/// </para>
/// <para>
/// <b>Numbers are invariant-culture and parsed as floats</b>, exponents included. The client formats
/// with JavaScript's number-to-string and this class with .NET's round-trip format, which agree on
/// integers and ordinary decimals and differ on exponents (<c>1e-7</c> against <c>1E-07</c>) - both
/// of which parse here. The fixture's round-trip cases are therefore the values both tiers format
/// identically.
/// </para>
/// <para>
/// <b>Known and deliberately unchanged: <c>NaN</c> and <c>Infinity</c> parse as coordinates</b>,
/// because <c>double.TryParse</c> accepts them and all five copies this replaces did too. Refusing
/// them is a behaviour change no criterion asks for, so it is recorded here rather than made, and
/// the fixture does not list them as valid.
/// </para>
/// </remarks>
public static class GestureIds
{
    /// <summary>What every new-placement id starts with.</summary>
    public const string PlacementPrefix = "new:";

    /// <summary>What every relation id starts with.</summary>
    public const string RelationPrefix = "rel:";

    /// <summary>What separates a relation id's source from its target.</summary>
    public const string RelationSeparator = "->";

    private const char PlacementSeparator = ',';

    /// <summary>Whether <paramref name="elementId"/> is a new placement of either shape, without parsing it.</summary>
    public static bool IsPlacement([NotNullWhen(true)] string? elementId) =>
        elementId is not null && elementId.StartsWith(PlacementPrefix, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="elementId"/> is a relation id, without parsing it.</summary>
    public static bool IsRelation([NotNullWhen(true)] string? elementId) =>
        elementId is not null && elementId.StartsWith(RelationPrefix, StringComparison.Ordinal);

    /// <summary>A free canvas's placement id: <c>new:{x},{y}</c>.</summary>
    public static string Placement(double x, double y) =>
        string.Create(CultureInfo.InvariantCulture, $"{PlacementPrefix}{x}{PlacementSeparator}{y}");

    /// <summary>Parses a free canvas's placement id.</summary>
    public static bool TryParsePlacement(string? elementId, out double x, out double y)
    {
        y = 0;
        return TrySplitPlacement(elementId, out x, out var second)
            && double.TryParse(second, NumberStyles.Float, CultureInfo.InvariantCulture, out y);
    }

    /// <summary>A row canvas's placement id: <c>new:{x},{row}</c>.</summary>
    public static string RowPlacement(double x, int row) =>
        string.Create(CultureInfo.InvariantCulture, $"{PlacementPrefix}{x}{PlacementSeparator}{row}");

    /// <summary>Parses a row canvas's placement id; a row that is not a whole number is refused.</summary>
    public static bool TryParseRowPlacement(string? elementId, out double x, out int row)
    {
        row = 0;
        return TrySplitPlacement(elementId, out x, out var second)
            && int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out row);
    }

    /// <summary>A proposed relation's id: <c>rel:{from}-&gt;{to}</c>.</summary>
    public static string Relation(string from, string to) => $"{RelationPrefix}{from}{RelationSeparator}{to}";

    /// <summary>Parses a relation id, refusing one whose source or target is empty (R11.2).</summary>
    public static bool TryParseRelation(string? elementId, out string from, out string to)
    {
        from = "";
        to = "";
        if (!IsRelation(elementId))
        {
            return false;
        }

        var body = elementId[RelationPrefix.Length..];
        var separator = body.IndexOf(RelationSeparator, StringComparison.Ordinal);
        if (separator <= 0 || separator + RelationSeparator.Length >= body.Length)
        {
            // No arrow, or nothing before it, or nothing after it - an empty end, which the writer
            // used to emit as `link a ->  +` in causal-loop before R11.2 was fixed there.
            return false;
        }

        from = body[..separator];
        to = body[(separator + RelationSeparator.Length)..];
        return true;
    }

    // Exactly two components after the prefix: one too few, one too many, or a trailing separator
    // (which makes a third, empty one) are all refused. The first is always a coordinate; what the
    // second is depends on the shape, so it is handed back unparsed.
    private static bool TrySplitPlacement(string? elementId, out double x, out string second)
    {
        x = 0;
        second = "";
        if (!IsPlacement(elementId))
        {
            return false;
        }

        var parts = elementId[PlacementPrefix.Length..].Split(PlacementSeparator);
        if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x))
        {
            return false;
        }

        second = parts[1];
        return true;
    }
}
