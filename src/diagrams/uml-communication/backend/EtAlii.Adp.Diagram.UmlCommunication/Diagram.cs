using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.UmlCommunication;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `uml/communication`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
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
