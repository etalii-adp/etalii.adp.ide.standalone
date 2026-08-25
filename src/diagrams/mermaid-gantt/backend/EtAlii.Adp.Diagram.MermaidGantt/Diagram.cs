namespace EtAlii.Adp.Diagram.MermaidGantt;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/gantt`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("mermaid", "gantt"),
            "Gantt chart",
            "Tasks over time, with dependencies and milestones, in Mermaid's Gantt syntax."),
    ];
}
