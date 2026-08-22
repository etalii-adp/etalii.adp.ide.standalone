namespace EtAlii.Adp.Diagram.MermaidSequence;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/sequence`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("mermaid", "sequence"),
        "Sequence diagram");
}
