using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.PlantumlC4;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `plantuml/c4`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("plantuml", "c4"),
            "C4-PlantUML diagram",
            "A C4 model written in PlantUML with the C4-PlantUML macros.",
            Icon: "mdi-earth-arrow-right"),
    ];
}
