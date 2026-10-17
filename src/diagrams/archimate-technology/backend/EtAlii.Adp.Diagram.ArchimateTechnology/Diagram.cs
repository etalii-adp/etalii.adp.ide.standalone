using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.ArchimateTechnology;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `archimate/technology`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new ToolOrigin("archimate", "technology"),
            "ArchiMate — Technology layer",
            "The nodes, devices and system software applications actually run on.",
            Icon: "mdi-server"),
    ];
}
