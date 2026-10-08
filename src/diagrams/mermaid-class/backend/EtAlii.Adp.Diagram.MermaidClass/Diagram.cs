using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.MermaidClass;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `mermaid/class`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("mermaid", "class"),
            "Class diagram",
            "Classes, attributes and relationships, in Mermaid's class syntax.",
            Icon: "mdi-cube-outline"),
    ];
}
