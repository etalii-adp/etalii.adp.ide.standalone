using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// What an element id selects. The canvas and the providers share one vocabulary, so a menu, a
/// drop and a keyboard shortcut all address the same thing the same way.
/// </summary>
/// <remarks>
/// Three drawn kinds and one gesture. <c>variable:{id}</c> and <c>loop:{identifier}</c> name one
/// statement each; <c>link:{from}|{to}</c> names a link by its two ends, because a link has no
/// identity of its own in the document beyond the pair it joins. <c>new:{x},{y}</c> is not an
/// element at all - it is where the user asked for something to be created. That id and the
/// <c>rel:</c> one are built and parsed by the shared <see cref="GestureIds"/> grammar
/// (backend-centralization R11.1).
/// </remarks>
public static class CausalLoopSelection
{
    private const string VariablePrefix = "variable:";
    private const string LoopPrefix = "loop:";
    private const string LinkPrefix = "link:";

    /// <summary>The variable an id names, or null.</summary>
    public static string? VariableOf(string? elementId) =>
        elementId is not null && elementId.StartsWith(VariablePrefix, StringComparison.Ordinal)
            ? elementId[VariablePrefix.Length..]
            : null;

    /// <summary>The loop an id names, or null.</summary>
    public static string? LoopOf(string? elementId) =>
        elementId is not null && elementId.StartsWith(LoopPrefix, StringComparison.Ordinal)
            ? elementId[LoopPrefix.Length..]
            : null;

    /// <summary>The two ends of the link an id names, or null.</summary>
    public static (string From, string To)? LinkOf(string? elementId)
    {
        if (elementId is null || !elementId.StartsWith(LinkPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var body = elementId[LinkPrefix.Length..];
        var separator = body.IndexOf('|', StringComparison.Ordinal);
        return separator > 0 ? (body[..separator], body[(separator + 1)..]) : null;
    }

    /// <summary>The id the canvas writes when a user asks for something new at a point.</summary>
    public static string PlacementFor(double x, double y) => GestureIds.Placement(x, y);

    /// <summary>Whether an id is a placement rather than an element.</summary>
    public static bool IsPlacement(string? elementId) => GestureIds.IsPlacement(elementId);

    /// <summary>The point a placement id names, or null when the id is not a placement or is malformed.</summary>
    public static (double X, double Y)? PlacementPoint(string? elementId) =>
        GestureIds.TryParsePlacement(elementId, out var x, out var y) ? (x, y) : null;

    /// <summary>
    /// The id the canvas writes when a link is drawn from one variable to another - the id a
    /// right-drag connect carries: <c>rel:{from}-&gt;{to}</c>, the two ends of the link to state.
    /// </summary>
    public static string RelationFor(string from, string to) => GestureIds.Relation(from, to);

    /// <summary>The two ends the relation id names, or null when it is not one or either end is empty.</summary>
    /// <remarks>
    /// Both ends, as the shared grammar requires (R11.2): 'rel:a->' once read as a link to an empty
    /// id, which the writer emitted as 'link a ->  +'.
    /// </remarks>
    public static (string From, string To)? RelationOf(string? elementId) =>
        GestureIds.TryParseRelation(elementId, out var from, out var to) ? (from, to) : null;
}
