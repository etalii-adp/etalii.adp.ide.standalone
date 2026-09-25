using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.UmlCommunication;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/communication`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "communication"),
            "Communication diagram",
            "The same collaboration as a sequence diagram, arranged by who talks to whom rather than by time.",
            Icon: "mdi-forum-outline"),
    ];
}
