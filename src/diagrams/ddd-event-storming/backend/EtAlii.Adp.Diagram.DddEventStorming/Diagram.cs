namespace EtAlii.Adp.Diagram.DddEventStorming;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `ddd/event-storming`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("ddd", "event-storming"),
        "Event Storming");
}
