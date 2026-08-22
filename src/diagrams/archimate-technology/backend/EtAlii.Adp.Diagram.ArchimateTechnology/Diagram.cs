namespace EtAlii.Adp.Diagram.ArchimateTechnology;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `archimate/technology`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("archimate", "technology"),
        "ArchiMate — Technology layer");
}
