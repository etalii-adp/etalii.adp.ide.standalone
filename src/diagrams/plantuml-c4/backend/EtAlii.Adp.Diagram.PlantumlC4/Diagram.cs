namespace EtAlii.Adp.Diagram.PlantumlC4;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `plantuml/c4`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("plantuml", "c4"),
            "C4 diagram, via C4-PlantUML",
            "A C4 model written in PlantUML with the C4-PlantUML macros.",
            Icon: "mdi-earth-arrow-right"),
    ];
}
