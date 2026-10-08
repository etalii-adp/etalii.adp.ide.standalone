using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.ArchimateStrategy;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `archimate/strategy`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("archimate", "strategy"),
            "ArchiMate — Strategy layer",
            "Capabilities, resources and courses of action, above the level of any single system.",
            Icon: "mdi-chess-knight"),
    ];
}
