namespace EtAlii.Adp.Diagram.C4Component;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/component`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("c4", "component"),
        "Component");
}
