namespace EtAlii.Adp.Diagram.MermaidC4;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/c4`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("mermaid", "c4"),
        "C4 diagram (subset)");
}
