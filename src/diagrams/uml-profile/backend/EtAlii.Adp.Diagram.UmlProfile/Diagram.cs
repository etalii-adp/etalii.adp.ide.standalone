namespace EtAlii.Adp.Diagram.UmlProfile;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/profile`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "profile"),
            "Profile diagram",
            "A UML extension: the stereotypes and tagged values that adapt UML to a domain.",
            Icon: "mdi-tag-outline"),
    ];
}
