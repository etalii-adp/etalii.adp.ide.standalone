using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The synthetic element id a canvas gesture uses to name a place where no element exists yet:
/// <c>new:{x},{y}</c> - the timeline's placement mechanism on this family's coordinates.
/// Built and parsed by the shared <see cref="GestureIds"/> grammar (backend-centralization R11.1).
/// </summary>
/// <remarks>
/// The context-action channel carries one element id per call and nothing else - a drop's
/// position has no field of its own. An element id is this module's to interpret, so its
/// resolver resolves one that names "the element about to exist here" as readily as one that
/// names an element that does. It lives for exactly one ExecuteAction call and never reaches a
/// selection, a file or the history.
/// </remarks>
public static class DatabricksNewPlacement
{
    /// <summary>The id for a placement, as the canvas writes it.</summary>
    public static string IdFor(double x, double y) => GestureIds.Placement(x, y);

    /// <summary>
    /// Whether <paramref name="elementId"/> is a well-formed placement id. Where it points is not
    /// read here: the authored drop position is the client's follow-up layout write.
    /// </summary>
    public static bool IsPlacement(string? elementId) =>
        GestureIds.TryParsePlacement(elementId, out _, out _);
}

/// <summary>
/// The synthetic id a finished relation gesture carries: <c>rel:{from}-&gt;{to}</c> - one call
/// carrying the whole gesture, deliberately stateless, exactly as the timeline established it:
/// the two-call protocol it replaces kept an armed source between calls, and a stale arm made
/// the next drag relate the wrong pair.
/// Built and parsed by the shared <see cref="GestureIds"/> grammar (backend-centralization R11.1).
/// </summary>
public static class DatabricksRelationGesture
{
    /// <summary>Whether <paramref name="elementId"/> is a relation gesture, and what it carries.</summary>
    public static bool TryParse(string? elementId, out string from, out string to) =>
        GestureIds.TryParseRelation(elementId, out from, out to);
}
