using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.UmlUseCase;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `uml/use-case`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "use-case"),
            "Use case diagram",
            "What actors want from a system, as use cases and the relationships between them.",
            Icon: "mdi-account-group-outline"),
    ];
}
