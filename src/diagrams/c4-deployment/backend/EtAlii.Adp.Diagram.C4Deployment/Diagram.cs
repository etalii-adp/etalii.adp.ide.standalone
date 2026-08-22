namespace EtAlii.Adp.Diagram.C4Deployment;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/deployment`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("c4", "deployment"),
        "Deployment (supplementary)");
}
