namespace EtAlii.Adp.Diagram.UmlActivity;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/activity`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("uml", "activity"),
        "Activity diagram");
}
