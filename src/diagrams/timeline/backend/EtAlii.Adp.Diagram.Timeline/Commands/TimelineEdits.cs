using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// What every handler does first: locate against <b>current</b> state, because undo and redo
/// dispatch the same command instance again later and the document has moved on since.
/// </summary>
internal static class TimelineEdits
{
    /// <summary>The element an id names in the current model, or null.</summary>
    public static TimelineElement? ElementOf(TimelineModel model, string elementId) =>
        model.Elements.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, elementId, StringComparison.Ordinal));

    /// <summary>The connection an id names in the current model, or null.</summary>
    public static TimelineConnection? ConnectionOf(TimelineModel model, string connectionId) =>
        model.Connections.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, connectionId, StringComparison.Ordinal));

    /// <summary>The lines a range covers, captured for a byte-exact restore.</summary>
    public static LineSegment Capture(LineDocument document, LineRange range) =>
        new(
            range.Start,
            Enumerable.Range(range.Start, range.Length).Select(i => document.Lines[i].Text).ToList());
}
