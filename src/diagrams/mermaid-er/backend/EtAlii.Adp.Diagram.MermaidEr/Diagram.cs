using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.MermaidEr;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `mermaid/er`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("mermaid", "er"),
            "Entity-relationship diagram",
            "Entities and relationships, in Mermaid's ER syntax.",
            Icon: "mdi-database-outline"),
    ];
}
