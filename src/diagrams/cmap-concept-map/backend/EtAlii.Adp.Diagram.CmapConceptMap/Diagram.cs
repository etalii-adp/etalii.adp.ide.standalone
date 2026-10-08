using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.CmapConceptMap;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `cmap/concept-map`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("cmap", "concept-map"),
            "Concept map",
            "Concepts joined by labelled phrases, so each connection states a proposition.",
            Icon: "mdi-graph-outline"),
    ];
}
