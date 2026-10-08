using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.PlantumlUml;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `plantuml/uml`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("plantuml", "uml"),
            "UML diagram",
            "UML written as PlantUML text and rendered from it.",
            Icon: "mdi-drawing"),
    ];
}
