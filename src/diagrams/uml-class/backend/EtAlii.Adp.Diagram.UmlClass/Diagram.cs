using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.UmlClass;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `uml/class`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "class"),
            "Class diagram",
            "Classes, their attributes and operations, and the associations between them.",
            Icon: "mdi-cube-outline"),
    ];
}
