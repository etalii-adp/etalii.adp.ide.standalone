namespace EtAlii.Adp.Diagram.MermaidClass;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/class`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("mermaid", "class"),
            "Class diagram",
            "Classes, attributes and relationships, in Mermaid's class syntax."),
    ];
}
