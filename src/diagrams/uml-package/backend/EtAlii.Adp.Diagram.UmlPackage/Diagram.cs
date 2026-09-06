using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.UmlPackage;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/package`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "package"),
            "Package diagram",
            "How a model is grouped into packages, and which package depends on which.",
            Icon: "mdi-package-variant-closed"),
    ];
}
