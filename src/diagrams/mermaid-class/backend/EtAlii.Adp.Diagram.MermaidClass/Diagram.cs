namespace EtAlii.Adp.Diagram.MermaidClass;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/class`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("mermaid", "class"),
        "Class diagram");
}
