using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.StructurizrC4;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `structurizr/c4`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("structurizr", "c4"),
            "C4 model (\"model once, view many\")",
            "One C4 model in the Structurizr DSL, with several views generated from it.",
            Icon: "mdi-city-variant-outline"),
    ];
}
