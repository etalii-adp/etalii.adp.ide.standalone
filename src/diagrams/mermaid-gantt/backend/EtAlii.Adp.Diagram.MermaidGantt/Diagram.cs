namespace EtAlii.Adp.Diagram.MermaidGantt;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/gantt`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("mermaid", "gantt"),
        "Gantt chart");
}
