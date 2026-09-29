using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.UmlDeployment;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `uml/deployment`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "deployment"),
            "Deployment diagram",
            "Artifacts placed on nodes: what is installed where, and over which links.",
            Icon: "mdi-server-network-outline"),
    ];
}
