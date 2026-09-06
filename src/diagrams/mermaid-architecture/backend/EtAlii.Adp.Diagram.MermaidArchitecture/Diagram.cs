using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.MermaidArchitecture;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `mermaid/architecture`.</summary>
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
