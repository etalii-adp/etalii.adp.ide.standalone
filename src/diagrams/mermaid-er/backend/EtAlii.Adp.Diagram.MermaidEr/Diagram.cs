namespace EtAlii.Adp.Diagram.MermaidEr;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/er`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("mermaid", "er"),
        "Entity-Relationship diagram");
}
