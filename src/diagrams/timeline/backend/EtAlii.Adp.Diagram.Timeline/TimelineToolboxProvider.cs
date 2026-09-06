using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The palette: a period and a moment, each entry carrying only data and naming the add action
/// its drop commits (Requirement 9.2). There is no second implementation for a drop to disagree
/// with - the drop and the menu run the same command.
/// </summary>
public sealed class TimelineToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.Timeline.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new(
            "timeline.toolbox.element",
            "Element",
            "mdi-arrow-expand-horizontal",
            "Something with a begin and an end. Drop on the canvas at the time and row it starts.",
            TimelineContextActionProvider.AddElementActionId),
        new(
            "timeline.toolbox.moment",
            "Moment",
            "mdi-rhombus-medium",
            "A single point in time. Drop on the canvas at the time and row it marks.",
            TimelineContextActionProvider.AddMomentActionId),
    ];
}
