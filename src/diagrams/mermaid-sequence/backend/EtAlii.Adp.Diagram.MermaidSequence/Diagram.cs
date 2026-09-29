using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.MermaidSequence;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `mermaid/sequence`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("mermaid", "sequence"),
            "Sequence diagram",
            "Messages exchanged between participants over time, in Mermaid's sequence syntax.",
            Icon: "mdi-swap-horizontal"),
    ];
}
