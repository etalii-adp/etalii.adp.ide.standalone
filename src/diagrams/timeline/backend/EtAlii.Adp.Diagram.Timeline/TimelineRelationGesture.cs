namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The synthetic id a finished relation gesture carries: <c>rel:{from}-&gt;{to}</c>, where the
/// target is an element id or a <see cref="TimelineNewPlacement"/> for a release on empty
/// canvas.
/// </summary>
/// <remarks>
/// <para>
/// One call carrying the whole gesture, deliberately replacing the two-call protocol that kept
/// per-connection state between the calls. That state could go stale - an armed gesture nothing
/// ever completed - and the next drag's first call then <em>completed</em> against the leftover:
/// a relation between the wrong pair, observed in the running app. A gesture that arrives whole
/// has no state to corrupt and no order to get wrong.
/// </para>
/// <para>
/// The separator is <c>-&gt;</c> and the split takes its first occurrence, so a target that is
/// itself a placement (which contains no separator) parses unambiguously. An element whose own
/// id contains <c>-&gt;</c> would mislead the split; ids here are ShortGuids or hand-written
/// YAML keys, and a hand author who names an element <c>a-&gt;b</c> has bigger problems.
/// </para>
/// </remarks>
public static class TimelineRelationGesture
{
    private const string Prefix = "rel:";
    private const string Separator = "->";

    /// <summary>The id for a finished gesture from one element to another - or to a placement.</summary>
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
