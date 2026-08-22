namespace EtAlii.Adp.Diagram.UmlCompositeStructure;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/composite-structure`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("uml", "composite-structure"),
        "Composite structure diagram");
}
