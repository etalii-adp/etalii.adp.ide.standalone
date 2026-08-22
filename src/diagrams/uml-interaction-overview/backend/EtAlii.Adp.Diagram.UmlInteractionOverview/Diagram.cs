namespace EtAlii.Adp.Diagram.UmlInteractionOverview;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/interaction-overview`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("uml", "interaction-overview"),
        "Interaction overview diagram");
}
