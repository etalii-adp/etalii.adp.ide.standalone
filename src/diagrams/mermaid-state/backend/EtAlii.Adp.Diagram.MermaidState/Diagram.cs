using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.MermaidState;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `mermaid/state`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
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
