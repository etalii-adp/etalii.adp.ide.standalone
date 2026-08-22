namespace EtAlii.Adp.Diagram.MermaidGantt;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/gantt`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("mermaid", "gantt"),
        "Gantt chart");
}
