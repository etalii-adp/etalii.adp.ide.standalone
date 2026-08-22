namespace EtAlii.Adp.Diagram.UmlTiming;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/timing`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("uml", "timing"),
        "Timing diagram");
}
