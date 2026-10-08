using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.DddEventStorming;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `ddd/event-storming`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("ddd", "event-storming"),
            "Event Storming",
            "A domain explored as a timeline of events, commands and the aggregates that react.",
            Icon: "mdi-lightning-bolt-outline"),
    ];
}
