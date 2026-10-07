using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The palette: a period and a moment, each entry carrying only data and naming the add action
/// its drop commits (Requirement 9.2). There is no second implementation for a drop to disagree
/// with - the drop and the menu run the same command.
/// </summary>
/// <remarks>Derived from the definition's toolbox and the wire ids of its <c>x-timeline</c> block (<see cref="TimelineDefinition"/>).</remarks>
public sealed class TimelineToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.Timeline.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items => TimelineDefinition.Toolbox;
}
