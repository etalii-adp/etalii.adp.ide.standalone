using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.AzureArchitecture;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `azure/architecture`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("azure", "architecture"),
            "Azure architecture diagram",
            "How a solution is built out of Azure services, drawn with the vendor's own icon set.",
            Icon: "mdi-microsoft-azure"),
    ];
}
