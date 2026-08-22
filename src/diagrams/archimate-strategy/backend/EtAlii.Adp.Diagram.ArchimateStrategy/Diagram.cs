namespace EtAlii.Adp.Diagram.ArchimateStrategy;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `archimate/strategy`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("archimate", "strategy"),
        "ArchiMate — Strategy layer");
}
