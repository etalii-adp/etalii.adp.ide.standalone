using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.MermaidState;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/state`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("mermaid", "state"),
            "State diagram",
            "States and the events that move between them, in Mermaid's state syntax.",
            Icon: "mdi-state-machine"),
    ];
}
