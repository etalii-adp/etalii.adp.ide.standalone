namespace EtAlii.Adp.Diagram.UmlProfile;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/profile`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("uml", "profile"),
        "Profile diagram");
}
