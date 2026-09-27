using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The synthetic element id a canvas gesture uses to name a place where no element exists yet:
/// <c>new:{seconds},{row}</c>.
/// Built and parsed by the shared <see cref="GestureIds"/> grammar (backend-centralization R11.1).
/// </summary>
/// <remarks>
/// <para>
/// This exists because the context-action channel carries one element id per call and nothing
/// else - a drop's position and a relation-to-empty-space's landing point have no field of
/// their own. An element id is this module's to interpret: its resolver resolves ids, so it can
/// resolve one that names "the element about to exist here" as readily as one that names an
/// element that does. The id never reaches a selection, a file or the history - it lives for
/// exactly one ExecuteAction call.
/// </para>
/// <para>
/// Module-internal by construction: core resolves the id through this module's own resolver
/// and learns nothing.
/// </para>
/// </remarks>
public static class TimelineNewPlacement
{
    /// <summary>The id for a placement, as the canvas writes it.</summary>
    public static string IdFor(double seconds, int row) => GestureIds.RowPlacement(seconds, row);

    /// <summary>Whether <paramref name="elementId"/> is a placement id, and what it carries.</summary>
    public static bool TryParse(string? elementId, out double seconds, out int row) =>
        GestureIds.TryParseRowPlacement(elementId, out seconds, out row);
}
