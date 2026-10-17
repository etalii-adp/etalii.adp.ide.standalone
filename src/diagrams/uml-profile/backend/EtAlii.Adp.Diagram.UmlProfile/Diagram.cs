using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.UmlProfile;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `uml/profile`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new ToolOrigin("uml", "profile"),
            "Profile diagram",
            "A UML extension: the stereotypes and tagged values that adapt UML to a domain.",
            Icon: "mdi-tag-outline"),
    ];
}
