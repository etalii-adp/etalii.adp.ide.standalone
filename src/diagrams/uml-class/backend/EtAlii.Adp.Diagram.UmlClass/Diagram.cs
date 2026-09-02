namespace EtAlii.Adp.Diagram.UmlClass;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/class`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "class"),
            "Class diagram",
            "Classes, their attributes and operations, and the associations between them.",
            Icon: "mdi-cube-outline"),
    ];
}
