namespace EtAlii.Adp.Diagram.ArchimateMotivation;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `archimate/motivation`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("archimate", "motivation"),
        "ArchiMate — Motivation layer");
}
