using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.MermaidGantt;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `mermaid/gantt`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new ToolOrigin("mermaid", "gantt"),
            "Gantt chart",
            "Tasks over time, with dependencies and milestones, in Mermaid's Gantt syntax.",
            Icon: "mdi-chart-gantt"),
    ];
}
