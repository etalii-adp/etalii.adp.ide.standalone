namespace EtAlii.Adp.Diagram.ArchimateStrategy;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `archimate/strategy`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("archimate", "strategy"),
            "ArchiMate — Strategy layer",
            "Capabilities, resources and courses of action, above the level of any single system."),
    ];
}
