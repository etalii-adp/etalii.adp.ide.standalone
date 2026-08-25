namespace EtAlii.Adp.Diagram.UmlTiming;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/timing`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "timing"),
            "Timing diagram",
            "How states change against an explicit time axis, for timing-sensitive behaviour."),
    ];
}
