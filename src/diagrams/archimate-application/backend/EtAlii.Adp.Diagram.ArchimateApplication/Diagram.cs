namespace EtAlii.Adp.Diagram.ArchimateApplication;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `archimate/application`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("archimate", "application"),
        "ArchiMate — Application layer");
}
