using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.UmlInteractionOverview;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `uml/interaction-overview`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "interaction-overview"),
            "Interaction overview diagram",
            "Several interactions stitched into one flow, so an activity's branches lead into scenarios.",
            Icon: "mdi-map-marker-path"),
    ];
}
