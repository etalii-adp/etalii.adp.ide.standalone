namespace EtAlii.Adp.Diagram.ArchimateApplication;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `archimate/application`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("archimate", "application"),
        "ArchiMate — Application layer");
}
