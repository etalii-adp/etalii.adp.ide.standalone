namespace EtAlii.Adp.Diagram.StructurizrC4;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `structurizr/c4`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("structurizr", "c4"),
        "C4 model (\"model once, view many\")");
}
