namespace EtAlii.Adp.Diagram.ArchimateBusiness;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `archimate/business`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("archimate", "business"),
        "ArchiMate — Business layer",
        "The business layer: actors, roles, processes and the services they offer each other.");
}
