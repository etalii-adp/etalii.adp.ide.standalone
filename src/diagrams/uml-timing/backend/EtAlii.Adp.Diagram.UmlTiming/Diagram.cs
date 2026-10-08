using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.UmlTiming;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `uml/timing`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "timing"),
            "Timing diagram",
            "How states change against an explicit time axis, for timing-sensitive behaviour.",
            Icon: "mdi-timer-sand"),
    ];
}
