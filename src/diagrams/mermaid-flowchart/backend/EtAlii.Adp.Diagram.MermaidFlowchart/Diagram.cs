namespace EtAlii.Adp.Diagram.MermaidFlowchart;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/flowchart`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("mermaid", "flowchart"),
        "Flowchart");
}
