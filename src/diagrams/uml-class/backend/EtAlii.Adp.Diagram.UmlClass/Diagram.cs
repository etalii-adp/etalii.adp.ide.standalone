namespace EtAlii.Adp.Diagram.UmlClass;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/class`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("uml", "class"),
        "Class diagram");
}
