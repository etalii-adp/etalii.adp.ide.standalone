namespace EtAlii.Adp.Diagram.MermaidFlowchart;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/flowchart`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("mermaid", "flowchart"),
        "Flowchart",
        "A flow of steps and decisions, in Mermaid's flowchart syntax.");
}
