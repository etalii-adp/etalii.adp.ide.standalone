namespace EtAlii.Adp.Diagram.ArchimateStrategy;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `archimate/strategy`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("archimate", "strategy"),
        "ArchiMate — Strategy layer");
}
