using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.UmlObject;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `uml/object`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "object"),
            "Object diagram",
            "A snapshot of instances and their links at one moment, to make a class diagram concrete.",
            Icon: "mdi-shape-outline"),
    ];
}
