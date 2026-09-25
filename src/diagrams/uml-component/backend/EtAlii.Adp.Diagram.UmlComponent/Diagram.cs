using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.UmlComponent;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/component`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "component"),
            "Component diagram",
            "Components, the interfaces they provide and require, and how they plug together.",
            Icon: "mdi-toy-brick-outline"),
    ];
}
