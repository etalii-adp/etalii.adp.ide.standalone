namespace EtAlii.Adp.Diagram.UmlProfile;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/profile`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("uml", "profile"),
        "Profile diagram");
}
