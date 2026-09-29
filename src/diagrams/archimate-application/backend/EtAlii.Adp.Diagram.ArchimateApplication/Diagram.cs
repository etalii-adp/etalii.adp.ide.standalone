using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.ArchimateApplication;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `archimate/application`.</summary>
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
