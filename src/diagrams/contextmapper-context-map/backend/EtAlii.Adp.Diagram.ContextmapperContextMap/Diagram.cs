using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.ContextmapperContextMap;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `contextmapper/context-map`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
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
