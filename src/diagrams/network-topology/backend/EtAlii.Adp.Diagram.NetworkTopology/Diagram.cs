using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.NetworkTopology;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `network/topology`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("network", "topology"),
            "Network topology diagram",
            "Hosts, links and segments: what is connected to what, and how.",
            Icon: "mdi-lan"),
    ];
}
