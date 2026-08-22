namespace EtAlii.Adp.Diagram.UmlComponent;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/component`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("uml", "component"),
        "Component diagram");
}
