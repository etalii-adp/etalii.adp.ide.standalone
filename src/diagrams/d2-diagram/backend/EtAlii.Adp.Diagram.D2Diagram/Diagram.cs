namespace EtAlii.Adp.Diagram.D2Diagram;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `d2/diagram`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("d2", "diagram"),
            "General-purpose declarative diagram",
            "A general-purpose diagram written as text and laid out automatically by D2."),
    ];
}
