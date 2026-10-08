using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.ArchimateApplication;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `archimate/application`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("archimate", "application"),
            "ArchiMate — Application layer",
            "How application services, components and their interfaces support the business.",
            Icon: "mdi-apps"),
    ];
}
