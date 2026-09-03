using System.Globalization;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The synthetic element id a canvas gesture uses to name a place where no element exists yet:
/// <c>new:{x},{y}</c> - the timeline's placement mechanism on this family's coordinates.
/// </summary>
/// <remarks>
/// The context-action channel carries one element id per call and nothing else - a drop's
/// position has no field of its own. An element id is this module's to interpret, so its
/// resolver resolves one that names "the element about to exist here" as readily as one that
/// names an element that does. It lives for exactly one ExecuteAction call and never reaches a
/// selection, a file or the history.
/// </remarks>
public static class RdfNewPlacement
{
    private const string Prefix = "new:";

    /// <summary>The id for a placement, as the canvas writes it.</summary>
    public static string IdFor(double x, double y) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}{x},{y}");

    /// <summary>Whether <paramref name="elementId"/> is a placement id, and what it carries.</summary>
    public static bool TryParse(string? elementId, out double x, out double y)
    {
        x = 0;
        y = 0;

        if (elementId is null || !elementId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = elementId[Prefix.Length..].Split(',');
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
    }
}

/// <summary>
/// The synthetic id a finished relation gesture carries: <c>rel:{from}-&gt;{to}</c> - one call
/// carrying the whole gesture, deliberately stateless, exactly as the timeline established it.
/// </summary>
public static class RdfRelationGesture
{
    private const string Prefix = "rel:";
    private const string Separator = "->";

    /// <summary>The id for a finished gesture from one element to another.</summary>
    public static string IdFor(string fromElementId, string target) =>
        $"{Prefix}{fromElementId}{Separator}{target}";

    /// <summary>Whether <paramref name="elementId"/> is a relation gesture, and what it carries.</summary>
    public static bool TryParse(string? elementId, out string from, out string to)
    {
        from = "";
        to = "";

        if (elementId is null || !elementId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = elementId[Prefix.Length..];
        var separator = payload.IndexOf(Separator, StringComparison.Ordinal);
        if (separator <= 0 || separator >= payload.Length - Separator.Length)
        {
            return false;
        }

        from = payload[..separator];
        to = payload[(separator + Separator.Length)..];
        return true;
    }
}
