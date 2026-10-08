using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.UmlSequence;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `uml/sequence`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "sequence"),
            "Sequence diagram",
            "Messages between lifelines in time order, for one scenario.",
            Icon: "mdi-swap-horizontal-bold"),
    ];
}
