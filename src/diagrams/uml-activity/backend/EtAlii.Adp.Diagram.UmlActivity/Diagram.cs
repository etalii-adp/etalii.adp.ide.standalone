using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.UmlActivity;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `uml/activity`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "activity"),
            "Activity diagram",
            "A workflow as actions, decisions and parallel branches - the UML take on a flowchart.",
            Icon: "mdi-arrow-decision-auto"),
    ];
}
