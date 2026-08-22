namespace EtAlii.Adp.Diagram.C4Container;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/container`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("c4", "container"),
        "Container");
}
