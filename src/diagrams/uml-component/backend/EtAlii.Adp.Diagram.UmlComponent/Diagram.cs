using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.UmlComponent;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `uml/component`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "component"),
            "Component diagram",
            "Components, the interfaces they provide and require, and how they plug together.",
            Icon: "mdi-toy-brick-outline"),
    ];
}
