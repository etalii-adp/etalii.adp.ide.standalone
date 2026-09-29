using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.MermaidFlowchart;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `mermaid/flowchart`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("mermaid", "flowchart"),
            "Flowchart",
            "A flow of steps and decisions, in Mermaid's flowchart syntax.",
            Icon: "mdi-arrow-decision-outline"),
    ];
}
