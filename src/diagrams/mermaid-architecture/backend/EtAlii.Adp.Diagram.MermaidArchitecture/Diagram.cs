namespace EtAlii.Adp.Diagram.MermaidArchitecture;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/architecture`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("mermaid", "architecture"),
        "Architecture diagram");
}
