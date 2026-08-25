namespace EtAlii.Adp.Diagram.DddEventStorming;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `ddd/event-storming`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("ddd", "event-storming"),
            "Event Storming",
            "A domain explored as a timeline of events, commands and the aggregates that react."),
    ];
}
