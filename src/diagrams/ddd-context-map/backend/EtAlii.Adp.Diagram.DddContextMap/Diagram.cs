using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.DddContextMap;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `ddd/context-map`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("ddd", "context-map"),
            "Context map",
            "Where each bounded context ends, and which pattern governs each boundary between them.",
            Icon: "mdi-map-outline"),
    ];
}
