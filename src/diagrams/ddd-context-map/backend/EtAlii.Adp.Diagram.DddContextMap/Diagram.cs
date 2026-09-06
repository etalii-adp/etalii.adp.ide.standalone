using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.DddContextMap;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `ddd/context-map`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("ddd", "context-map"),
            "Bounded Context / Context Map",
            "Where each bounded context ends, and which pattern governs each boundary between them.",
            Icon: "mdi-map-outline"),
    ];
}
