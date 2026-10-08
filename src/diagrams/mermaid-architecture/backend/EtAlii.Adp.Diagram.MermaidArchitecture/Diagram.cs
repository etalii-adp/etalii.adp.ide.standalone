using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.MermaidArchitecture;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `mermaid/architecture`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("mermaid", "architecture"),
            "Architecture diagram",
            "Services and their connections, in Mermaid's architecture syntax.",
            Icon: "mdi-office-building-outline"),
    ];
}
