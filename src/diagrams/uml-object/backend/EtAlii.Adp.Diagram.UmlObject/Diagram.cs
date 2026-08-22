namespace EtAlii.Adp.Diagram.UmlObject;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/object`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("uml", "object"),
        "Object diagram");
}
