namespace EtAlii.Adp.Diagram.D2Diagram;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `d2/diagram`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("d2", "diagram"),
        "General-purpose declarative diagram");
}
