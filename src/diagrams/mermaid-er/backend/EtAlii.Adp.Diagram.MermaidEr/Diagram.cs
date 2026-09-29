using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.MermaidEr;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `mermaid/er`.</summary>
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
