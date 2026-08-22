namespace EtAlii.Adp.Diagram.C4Context;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/context`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("c4", "context"),
        "System Context");
}
