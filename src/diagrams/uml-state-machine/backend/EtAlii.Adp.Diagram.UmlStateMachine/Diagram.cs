using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.UmlStateMachine;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/state-machine`.</summary>
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
