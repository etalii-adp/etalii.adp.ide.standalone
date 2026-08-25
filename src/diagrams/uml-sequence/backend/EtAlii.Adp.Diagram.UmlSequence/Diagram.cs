namespace EtAlii.Adp.Diagram.UmlSequence;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/sequence`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "sequence"),
            "Sequence diagram",
            "Messages between lifelines in time order, for one scenario."),
    ];
}
