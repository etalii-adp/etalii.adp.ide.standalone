namespace EtAlii.Adp.Diagram.MermaidC4;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/c4`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("mermaid", "c4"),
        "C4 diagram (subset)");
}
