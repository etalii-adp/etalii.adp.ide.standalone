namespace EtAlii.Adp.Diagram.C4SystemLandscape;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/system-landscape`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("c4", "system-landscape"),
        "System Landscape (supplementary)");
}
