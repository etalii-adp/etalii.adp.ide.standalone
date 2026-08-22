namespace EtAlii.Adp.Diagram.StructurizrC4;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `structurizr/c4`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("structurizr", "c4"),
        "C4 model (\"model once, view many\")");
}
