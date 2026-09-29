using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.ContextmapperContextMap;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `contextmapper/context-map`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("contextmapper", "context-map"),
            "Context Mapper context map",
            "Bounded contexts and the strategic relationships between the teams that own them.",
            Icon: "mdi-map-legend"),
    ];
}
