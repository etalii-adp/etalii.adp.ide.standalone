namespace EtAlii.Adp.Diagram.UmlObject;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/object`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("uml", "object"),
        "Object diagram");
}
