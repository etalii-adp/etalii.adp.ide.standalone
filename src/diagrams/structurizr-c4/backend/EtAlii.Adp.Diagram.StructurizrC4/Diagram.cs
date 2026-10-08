using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.StructurizrC4;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `structurizr/c4`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("structurizr", "c4"),
            "C4 model",
            "One C4 model in the Structurizr DSL, with several views generated from it.",
            Icon: "mdi-city-variant-outline"),
    ];
}
