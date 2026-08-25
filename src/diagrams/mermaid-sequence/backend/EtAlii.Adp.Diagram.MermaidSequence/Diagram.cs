namespace EtAlii.Adp.Diagram.MermaidSequence;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/sequence`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("mermaid", "sequence"),
            "Sequence diagram",
            "Messages exchanged between participants over time, in Mermaid's sequence syntax."),
    ];
}
