using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.UmlCompositeStructure;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/composite-structure`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "composite-structure"),
            "Composite structure diagram",
            "What one class is made of internally: its parts, ports and connectors.",
            Icon: "mdi-group"),
    ];
}
