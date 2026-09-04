using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// What every handler does first: locate against <b>current</b> state, because undo and redo
/// dispatch the same command instance again later and the document has moved on since.
/// </summary>
internal static class DependencyGraphEdits
{
    /// <summary>The element an id names in the current model, or null.</summary>
    public static DependencyGraphElement? ElementOf(DependencyGraphModel model, string elementId) =>
        model.Elements.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, elementId, StringComparison.Ordinal));

    /// <summary>The relation an id names in the current model, or null.</summary>
    public static DependencyGraphRelation? RelationOf(DependencyGraphModel model, string relationId) =>
        model.Relations.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, relationId, StringComparison.Ordinal));

    /// <summary>The lines a range covers, captured for a byte-exact restore.</summary>
    public static LineSegment Capture(LineDocument document, LineRange range) =>
        new(
            range.Start,
            Enumerable.Range(range.Start, range.Length).Select(i => document.Lines[i].Text).ToList());
}
