using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.UmlStateMachine;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `uml/state-machine`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "state-machine"),
            "State machine diagram",
            "The states an object can be in, and the events and guards that move it between them.",
            Icon: "mdi-state-machine"),
    ];
}
