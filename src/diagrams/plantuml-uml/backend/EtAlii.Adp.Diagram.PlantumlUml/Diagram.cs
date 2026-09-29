using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.PlantumlUml;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `plantuml/uml`.</summary>
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
