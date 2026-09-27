using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The palette: a Trend, a Trigger and a Note, described as data, each dropping its add action
/// (Requirement 11.1, and ghg-triggers-and-notes Requirement 7.1).
/// </summary>
/// <remarks>
/// Influences are drawn, not dropped, so the palette has no item for one. The drop and the placement
/// menu run the same action, so there is no second implementation for a drop to disagree with.
/// </remarks>
public sealed class GhgToolboxProvider : IDiagramToolboxProvider
{
    /// <summary>The Trend item's id.</summary>
    public const string TrendItemId = "ghg.toolbox.trend";

    /// <summary>The Trigger item's id.</summary>
    public const string TriggerItemId = "ghg.toolbox.trigger";

    /// <summary>The Note item's id.</summary>
    public const string NoteItemId = "ghg.toolbox.note";

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.HypeCycleGraph.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new(
            TrendItemId,
            "Trend",
            "mdi-arrow-right-bold-box-outline",
            "A trend through the hype cycle. Drop it where it starts; it is a year long with all four phases.",
            GhgContextActionProvider.AddTrendActionId),
        new(
            TriggerItemId,
            "Trigger",
            "mdi-circle-slice-8",
            "A moment in time that set trends off - an invention, a political moment, a disaster. Drop it where it happened; draw influences from it.",
            GhgContextActionProvider.AddTriggerActionId),
        new(
            NoteItemId,
            "Note",
            "mdi-note-text-outline",
            "A remark of your own, placed where it applies. Drop it and start typing.",
            GhgContextActionProvider.AddNoteActionId),
    ];
}
