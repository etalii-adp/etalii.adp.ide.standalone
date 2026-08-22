namespace EtAlii.Adp.Diagram.ArchimateTechnology;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `archimate/technology`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("archimate", "technology"),
        "ArchiMate — Technology layer");
}
