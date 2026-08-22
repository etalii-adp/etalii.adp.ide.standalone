namespace EtAlii.Adp.Diagram.C4Context;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/context`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("c4", "context"),
        "System Context");
}
